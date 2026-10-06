using DotaDraft.Api.Data;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Services;

public class OpenDotaService(
    HttpClient httpClient,
    AppDbContext db,
    ILogger<OpenDotaService> logger,
    IOptions<OpenDotaOptions> options)
{
    private readonly OpenDotaOptions _options = options.Value;

    public async Task<int> SeedHeroesAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Fetching heroes from OpenDota API...");

        List<OpenDotaHeroDto>? heroDtos;
        try
        {
            heroDtos = await httpClient.GetFromJsonAsync<List<OpenDotaHeroDto>>("api/heroStats", ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to retrieve heroes from OpenDota.");
            return 0;
        }

        heroDtos ??= [];
        if (heroDtos.Count == 0)
        {
            logger.LogWarning("No heroes returned from OpenDota.");
            return 0;
        }

        var existingIds = await db.Heroes
            .Select(h => h.Id)
            .ToHashSetAsync(ct);

        var newHeroes = heroDtos
            .Where(dto => !existingIds.Contains(dto.Id))
            .Select(dto => new Hero
            {
                Id = dto.Id,
                Name = dto.Name,
                LocalizedName = dto.LocalizedName,
                PrimaryAttr = dto.PrimaryAttr,
                Roles = dto.Roles,
                // Prefix relative path ("/apps/dota2/images/...") with Steam's CDN host
                ImgUrl = string.IsNullOrWhiteSpace(dto.Img) ? null : $"{_options.SteamCdnBaseUrl}{dto.Img}"
            })
            .ToList();

        if (newHeroes.Count > 0)
        {
            db.Heroes.AddRange(newHeroes);
            try
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Saved {Count} new heroes to PostgreSQL.", newHeroes.Count);
            }
            catch (DbUpdateException ex)
            {
                logger.LogError(ex, "Failed to save new heroes to the database.");
                // Detach the failed entities so later saves on this context don't retry them
                db.ChangeTracker.Clear();
                return 0;
            }
        }
        else
        {
            logger.LogInformation("All heroes are already up to date.");
        }

        return newHeroes.Count;
    }

    public async Task<int> SeedMatchupsAsync(CancellationToken ct = default)
    {
        // 1. Ensure the Heroes table is populated
        var heroes = await db.Heroes.OrderBy(h => h.Id).ToListAsync(ct);
        if (heroes.Count == 0)
        {
            logger.LogWarning("Cannot seed matchups: Heroes table is empty. Seed heroes first.");
            return 0;
        }

        var validHeroIds = heroes.Select(h => h.Id).ToHashSet();
        int totalSaved = 0;

        // 2. Preload all existing (HeroId, AgainstHeroId) pairs once, instead of one query per hero
        var existingMatchupKeys = (await db.HeroMatchups
                .Select(m => new { m.HeroId, m.AgainstHeroId })
                .ToListAsync(ct))
            .Select(m => (m.HeroId, m.AgainstHeroId))
            .ToHashSet();

        logger.LogInformation("Starting matchup ingestion for {Count} heroes...", heroes.Count);

        for (var i = 0; i < heroes.Count; i++)
        {
            var hero = heroes[i];

            // If the user cancelled or server is stopping, exit gracefully
            if (ct.IsCancellationRequested) break;

            logger.LogInformation("Fetching matchups for {HeroName} ({HeroId})...", hero.LocalizedName, hero.Id);

            List<OpenDotaMatchupDto>? matchupDtos;
            try
            {
                matchupDtos = await httpClient.GetFromJsonAsync<List<OpenDotaMatchupDto>>(
                    $"api/heroes/{hero.Id}/matchups", ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to retrieve matchups for hero ID {HeroId}", hero.Id);
                continue;
            }

            if (matchupDtos is null || matchupDtos.Count == 0) continue;

            var newMatchups = new List<HeroMatchup>();

            foreach (var dto in matchupDtos)
            {
                // Only insert if the opponent exists in our database and isn't already tracked
                if (!validHeroIds.Contains(dto.HeroId) || existingMatchupKeys.Contains((hero.Id, dto.HeroId)))
                    continue;

                decimal winRate = dto.GamesPlayed > 0
                    ? Math.Round(((decimal)dto.Wins / dto.GamesPlayed) * 100m, 2)
                    : 0m;

                newMatchups.Add(new HeroMatchup
                {
                    HeroId = hero.Id,
                    AgainstHeroId = dto.HeroId,
                    GamesPlayed = dto.GamesPlayed,
                    Wins = dto.Wins,
                    Advantage = winRate
                });
                existingMatchupKeys.Add((hero.Id, dto.HeroId));
            }

            if (newMatchups.Count > 0)
            {
                db.HeroMatchups.AddRange(newMatchups);
                try
                {
                    await db.SaveChangesAsync(ct);
                    totalSaved += newMatchups.Count;
                }
                catch (DbUpdateException ex)
                {
                    logger.LogError(ex, "Failed to save matchups for hero ID {HeroId}", hero.Id);
                    // Detach the failed entities so the next hero's save doesn't retry them
                    db.ChangeTracker.Clear();
                }
            }

            // 3. Rate limiting: stays under OpenDota's request limit. Skip the delay after the last hero.
            if (i < heroes.Count - 1)
                await Task.Delay(_options.MatchupRequestDelayMs, ct);
        }

        logger.LogInformation("Matchup ingestion complete. Total rows inserted: {TotalSaved}", totalSaved);
        return totalSaved;
    }
}