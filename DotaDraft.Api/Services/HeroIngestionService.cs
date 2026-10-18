using DotaDraft.Api.Data;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Services;

public class HeroIngestionService(
    IOpenDotaClient openDota,
    AppDbContext db,
    ILogger<HeroIngestionService> logger,
    IOptions<OpenDotaOptions> options)
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

        var (inserted, updated) = CountPendingWrites();
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
        // 1. Ensure the Heroes table is populated
        var heroes = await db.Heroes.AsNoTracking().OrderBy(h => h.Id).ToListAsync(ct);
        if (heroes.Count == 0)
        {
            logger.LogWarning("Cannot seed matchups: Heroes table is empty. Seed heroes first.");
            return SeedResult.Failed(SeedFailure.MissingPrerequisite, "Heroes table is empty. Seed heroes first.");
        }

        var validHeroIds = heroes.Select(h => h.Id).ToHashSet();
        int totalSaved = 0;
        var upstreamFailures = new List<int>();
        var databaseFailures = new List<int>();

        logger.LogInformation("Starting matchup ingestion for {Count} heroes...", heroes.Count);

        for (var i = 0; i < heroes.Count; i++)
        {
            var hero = heroes[i];

            // If the user cancelled or server is stopping, exit gracefully
            if (ct.IsCancellationRequested) break;

            // Rate limiting: wait before every request but the first, so the delay also
            // applies after a failed or empty response that skips the rest of the loop
            if (i > 0)
                await Task.Delay(_options.MatchupRequestDelayMs, ct);

            logger.LogInformation("Fetching matchups for {HeroName} ({HeroId})...", hero.LocalizedName, hero.Id);

            IReadOnlyList<OpenDotaMatchupDto> matchupDtos;
            try
            {
                matchupDtos = await openDota.GetHeroMatchupsAsync(hero.Id, ct);
            }
            catch (OpenDotaException ex)
            {
                logger.LogError(ex, "Failed to retrieve matchups for hero ID {HeroId}", hero.Id);
                upstreamFailures.Add(hero.Id);
                continue;
            }

            if (matchupDtos.Count == 0) continue;

            // 2. Load this hero's existing matchups so they can be updated in place
            var existingMatchups = await db.HeroMatchups
                .Where(m => m.HeroId == hero.Id)
                .ToDictionaryAsync(m => m.AgainstHeroId, ct);

            foreach (var dto in matchupDtos)
            {
                // Skip opponents that don't exist in our database
                if (!validHeroIds.Contains(dto.HeroId)) continue;

                if (!existingMatchups.TryGetValue(dto.HeroId, out var matchup))
                {
                    matchup = new HeroMatchup { HeroId = hero.Id, AgainstHeroId = dto.HeroId };
                    db.HeroMatchups.Add(matchup);
                    existingMatchups[dto.HeroId] = matchup;
                }

                matchup.GamesPlayed = dto.GamesPlayed;
                matchup.Wins = dto.Wins;
                matchup.WinRate = dto.GamesPlayed > 0
                    ? Math.Round(((decimal)dto.Wins / dto.GamesPlayed) * 100m, 2)
                    : 0m;
            }

            var (inserted, updated) = CountPendingWrites();
            if (inserted + updated > 0)
            {
                try
                {
                    await db.SaveChangesAsync(ct);
                    totalSaved += inserted + updated;
                }
                catch (DbUpdateException ex)
                {
                    logger.LogError(ex, "Failed to save matchups for hero ID {HeroId}", hero.Id);
                    databaseFailures.Add(hero.Id);
                }
            }

            // Detach this hero's matchups: keeps the tracker small across the run,
            // and after a failure stops the next hero's save from retrying them
            db.ChangeTracker.Clear();
        }

        logger.LogInformation("Matchup ingestion complete. Total rows inserted or updated: {TotalSaved}", totalSaved);

        if (upstreamFailures.Count == 0 && databaseFailures.Count == 0)
            return SeedResult.Success(totalSaved);

        // A database failure is reported over an upstream one, since it points at our side
        var failure = databaseFailures.Count > 0 ? SeedFailure.Database : SeedFailure.Upstream;
        var failedHeroIds = upstreamFailures.Concat(databaseFailures).Order().ToList();
        return SeedResult.Failed(failure, $"Matchups failed for {failedHeroIds.Count} of {heroes.Count} heroes.", totalSaved)
            with { FailedHeroIds = failedHeroIds };
    }

    // Entries() runs change detection, so modified properties on loaded entities are counted
    private (int Inserted, int Updated) CountPendingWrites()
    {
        var entries = db.ChangeTracker.Entries().ToList();
        return (entries.Count(e => e.State == EntityState.Added), entries.Count(e => e.State == EntityState.Modified));
    }
}