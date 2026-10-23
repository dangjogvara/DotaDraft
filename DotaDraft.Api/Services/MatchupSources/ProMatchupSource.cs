using DotaDraft.Api.Data;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Services.MatchupSources;

// One OpenDota request per hero, upserting that hero's matchups before moving on, so a failure
// only loses the heroes it hit
public class ProMatchupSource(
    IOpenDotaClient openDota,
    AppDbContext db,
    ILogger<ProMatchupSource> logger,
    IOptions<OpenDotaOptions> options) : IMatchupSource
{
    private readonly OpenDotaOptions _options = options.Value;

    public async Task<SeedResult> SeedAsync(IReadOnlyList<Hero> heroes, CancellationToken ct)
    {
        var validHeroIds = heroes.Select(h => h.Id).ToHashSet();
        int totalSaved = 0;
        var upstreamFailures = new List<int>();
        var databaseFailures = new List<int>();

        logger.LogInformation("Starting matchup ingestion for {Count} heroes...", heroes.Count);

        for (var i = 0; i < heroes.Count; i++)
        {
            var hero = heroes[i];

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

            var existingMatchups = await db.HeroMatchups
                .Where(m => m.HeroId == hero.Id)
                .ToDictionaryAsync(m => m.AgainstHeroId, ct);

            foreach (var dto in matchupDtos)
            {
                if (!validHeroIds.Contains(dto.HeroId)) continue;

                if (!existingMatchups.TryGetValue(dto.HeroId, out var matchup))
                {
                    matchup = new HeroMatchup { HeroId = hero.Id, AgainstHeroId = dto.HeroId };
                    db.HeroMatchups.Add(matchup);
                    existingMatchups[dto.HeroId] = matchup;
                }

                matchup.SetResults(dto.GamesPlayed, dto.Wins);
            }

            var (inserted, updated) = db.ChangeTracker.CountPendingWrites();
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
}
