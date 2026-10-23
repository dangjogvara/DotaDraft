using DotaDraft.Api.Data;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Services.MatchupSources;

// Sums hero-pair totals over consecutive match ID windows, newest first, then replaces all matchups
// in one save. Nothing is written unless at least half the windows succeeded, so a bad run can't
// swap good data for a tiny sample
public class PublicMatchupSource(
    IOpenDotaClient openDota,
    AppDbContext db,
    ILogger<PublicMatchupSource> logger,
    IOptions<OpenDotaOptions> options,
    IOptions<MatchupOptions> matchupOptions) : IMatchupSource
{
    private readonly OpenDotaOptions _options = options.Value;
    private readonly MatchupOptions _matchupOptions = matchupOptions.Value;

    public async Task<SeedResult> SeedAsync(IReadOnlyList<Hero> heroes, CancellationToken ct)
    {
        var validHeroIds = heroes.Select(h => h.Id).ToHashSet();

        long latestMatchId;
        try
        {
            latestMatchId = await openDota.GetLatestPublicMatchIdAsync(ct);
        }
        catch (OpenDotaException ex)
        {
            logger.LogError(ex, "Failed to find the latest public match on OpenDota.");
            return SeedResult.Failed(SeedFailure.Upstream, "Failed to find the latest public match on OpenDota.");
        }

        var windowSize = _matchupOptions.WindowSize;
        var windowCount = _matchupOptions.WindowCount;
        var totals = new Dictionary<(int HeroId, int AgainstHeroId), (int Games, int Wins)>();
        var failedWindows = 0;

        logger.LogInformation(
            "Starting public matchup ingestion: {WindowCount} windows of {WindowSize} match IDs back from {LatestMatchId}...",
            windowCount, windowSize, latestMatchId);

        for (var i = 0; i < windowCount; i++)
        {
            // Rate limiting: the latest-match query above was the first request
            await Task.Delay(_options.MatchupRequestDelayMs, ct);

            var toMatchId = latestMatchId - (long)i * windowSize;
            var fromMatchId = toMatchId - windowSize + 1;

            IReadOnlyList<OpenDotaPublicMatchupDto> rows;
            try
            {
                rows = await openDota.GetPublicMatchupsAsync(fromMatchId, toMatchId, _matchupOptions.MinAvgRankTier, ct);
            }
            catch (OpenDotaException ex)
            {
                logger.LogError(ex, "Failed to retrieve public matchups for match IDs {From}-{To}", fromMatchId, toMatchId);
                failedWindows++;
                continue;
            }

            foreach (var row in rows)
            {
                if (!validHeroIds.Contains(row.HeroId) || !validHeroIds.Contains(row.AgainstHeroId)) continue;

                var (games, wins) = totals.GetValueOrDefault((row.HeroId, row.AgainstHeroId));
                totals[(row.HeroId, row.AgainstHeroId)] = (games + row.Games, wins + row.Wins);
            }

            logger.LogInformation("Fetched window {Window}/{WindowCount} ({Rows} hero pairs).", i + 1, windowCount, rows.Count);
        }

        var succeededWindows = windowCount - failedWindows;
        if (succeededWindows * 2 < windowCount || totals.Count == 0)
        {
            logger.LogError("Only {Succeeded} of {WindowCount} windows returned data; keeping existing matchups.",
                succeededWindows, windowCount);
            return SeedResult.Failed(SeedFailure.Upstream,
                    $"Only {succeededWindows} of {windowCount} match windows could be fetched; existing matchups were kept.")
                with { FailedWindows = failedWindows };
        }

        // Replace the table's contents: update or add every pair seen, and remove pairs that weren't,
        // so no rows from an older seed or the pro source linger
        var existingMatchups = await db.HeroMatchups.ToDictionaryAsync(m => (m.HeroId, m.AgainstHeroId), ct);
        foreach (var ((heroId, againstHeroId), (games, wins)) in totals)
        {
            if (!existingMatchups.TryGetValue((heroId, againstHeroId), out var matchup))
            {
                matchup = new HeroMatchup { HeroId = heroId, AgainstHeroId = againstHeroId };
                db.HeroMatchups.Add(matchup);
            }

            matchup.SetResults(games, wins);
        }

        var stale = existingMatchups.Where(e => !totals.ContainsKey(e.Key)).Select(e => e.Value).ToList();
        db.HeroMatchups.RemoveRange(stale);

        var (inserted, updated) = db.ChangeTracker.CountPendingWrites();
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save public matchups to the database.");
            return SeedResult.Failed(SeedFailure.Database, "Failed to save matchups to the database.")
                with { FailedWindows = failedWindows };
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        logger.LogInformation(
            "Public matchup ingestion complete: {Pairs} pairs, {Inserted} inserted, {Updated} updated, {Removed} removed.",
            totals.Count, inserted, updated, stale.Count);

        if (failedWindows == 0)
            return SeedResult.Success(inserted + updated);

        return SeedResult.Failed(SeedFailure.Upstream,
                $"{failedWindows} of {windowCount} match windows failed; matchups were saved from the other {succeededWindows}.",
                inserted + updated)
            with { FailedWindows = failedWindows };
    }
}
