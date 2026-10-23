using DotaDraft.Api.Data;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Models;
using DotaDraft.Api.Services.MatchupSources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Services;

public class HeroIngestionService(
    IOpenDotaClient openDota,
    AppDbContext db,
    ILogger<HeroIngestionService> logger,
    IOptions<OpenDotaOptions> options,
    IMatchupSource matchupSource)
{
    private readonly OpenDotaOptions _options = options.Value;

    public async Task<SeedResult> SeedHeroesAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Fetching heroes from OpenDota API...");

        IReadOnlyList<OpenDotaHeroDto> heroDtos;
        try
        {
            heroDtos = await openDota.GetHeroStatsAsync(ct);
        }
        catch (OpenDotaException ex)
        {
            logger.LogError(ex, "Failed to retrieve heroes from OpenDota.");
            return SeedResult.Failed(SeedFailure.Upstream, "Failed to retrieve heroes from OpenDota.");
        }

        if (heroDtos.Count == 0)
        {
            logger.LogWarning("No heroes returned from OpenDota.");
            return SeedResult.Failed(SeedFailure.Upstream, "OpenDota returned no heroes.");
        }

        var existingHeroes = await db.Heroes.ToDictionaryAsync(h => h.Id, ct);

        foreach (var dto in heroDtos)
        {
            if (!existingHeroes.TryGetValue(dto.Id, out var hero))
            {
                hero = new Hero { Id = dto.Id, Name = dto.Name, LocalizedName = dto.LocalizedName, PrimaryAttr = dto.PrimaryAttr };
                db.Heroes.Add(hero);
            }

            // Overwrite existing heroes too, so renames and new art from a patch are picked up.
            // EF only issues UPDATEs for properties whose value actually changed.
            hero.Name = dto.Name;
            hero.LocalizedName = dto.LocalizedName;
            hero.PrimaryAttr = dto.PrimaryAttr;
            hero.Roles = dto.Roles;
            // Prefix relative path ("/apps/dota2/images/...png?") with Steam's CDN host. OpenDota ends paths
            // with an empty query string; trimming only a trailing '?' keeps any real query intact
            hero.ImgUrl = string.IsNullOrWhiteSpace(dto.Img) ? null : $"{_options.SteamCdnBaseUrl}{dto.Img.TrimEnd('?')}";
        }

        var (inserted, updated) = db.ChangeTracker.CountPendingWrites();
        if (inserted + updated > 0)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Inserted {Inserted} and updated {Updated} heroes.", inserted, updated);
            }
            catch (DbUpdateException ex)
            {
                logger.LogError(ex, "Failed to save heroes to the database.");
                // Detach the failed entities so later saves on this context don't retry them
                db.ChangeTracker.Clear();
                return SeedResult.Failed(SeedFailure.Database, "Failed to save heroes to the database.");
            }
        }
        else
        {
            logger.LogInformation("All heroes are already up to date.");
        }

        return SeedResult.Success(inserted + updated);
    }

    public async Task<SeedResult> SeedMatchupsAsync(CancellationToken ct = default)
    {
        var heroes = await db.Heroes.AsNoTracking().OrderBy(h => h.Id).ToListAsync(ct);
        if (heroes.Count == 0)
        {
            logger.LogWarning("Cannot seed matchups: Heroes table is empty. Seed heroes first.");
            return SeedResult.Failed(SeedFailure.MissingPrerequisite, "Heroes table is empty. Seed heroes first.");
        }

        return await matchupSource.SeedAsync(heroes, ct);
    }
}
