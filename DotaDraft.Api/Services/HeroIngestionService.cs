using DotaDraft.Api.Data;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MatchupTotals = System.Collections.Generic.Dictionary<(int HeroId, int AgainstHeroId), (int Games, int Wins)>;

namespace DotaDraft.Api.Services;

public class HeroIngestionService(
    IOpenDotaClient openDota,
    AppDbContext db,
    ILogger<HeroIngestionService> logger,
    IOptions<OpenDotaOptions> options,
    IOptions<MatchupOptions> matchupOptions)
{
    private readonly OpenDotaOptions _options = options.Value;
    private readonly MatchupOptions _matchupOptions = matchupOptions.Value;

    // Adds new heroes and updates changed ones. Returns the number of heroes written; throws
    // OpenDotaException when OpenDota fails, and DbUpdateException when saving fails
    public async Task<int> SeedHeroesAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Fetching heroes from OpenDota API...");

        var heroDtos = await openDota.GetHeroStatsAsync(ct);
        if (heroDtos.Count == 0)
            throw new OpenDotaException("OpenDota returned no heroes.");

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

        int saved;
        try
        {
            saved = await db.SaveChangesAsync(ct);
        }
        finally
        {
            // The matchup seed runs on the same context, so it mustn't retry heroes that failed to save
            db.ChangeTracker.Clear();
        }

        logger.LogInformation("Saved {Saved} new or changed heroes.", saved);
        return saved;
    }

    // Sums hero-pair totals over consecutive match ID windows, newest first, then replaces all matchups
    // in one save. Nothing is written unless at least half the windows succeeded, so a bad run can't
    // swap good data for a tiny sample. Returns the number of rows written (added, changed or removed);
    // throws OpenDotaException when too little could be fetched, and DbUpdateException when saving fails
    public async Task<int> SeedMatchupsAsync(CancellationToken ct = default)
    {
        var validHeroIds = await db.Heroes.Select(h => h.Id).ToHashSetAsync(ct);
        if (validHeroIds.Count == 0)
            throw new InvalidOperationException("The Heroes table is empty, so there are no heroes to seed matchups for.");

        var latestMatchId = await openDota.GetLatestPublicMatchIdAsync(ct);
        var (totals, failedWindows) = await FetchTotalsAsync(latestMatchId, validHeroIds, ct);

        var windowCount = _matchupOptions.WindowCount;
        var succeededWindows = windowCount - failedWindows;
        if (succeededWindows * 2 < windowCount || totals.Count == 0)
            throw new OpenDotaException(
                $"Only {succeededWindows} of {windowCount} match windows could be fetched; existing matchups were kept.");

        var saved = await ReplaceMatchupsAsync(totals, ct);
        if (failedWindows > 0)
            logger.LogWarning("{Failed} of {WindowCount} match windows failed; matchups were saved from the other {Succeeded}.",
                failedWindows, windowCount, succeededWindows);

        return saved;
    }

    // Fetches each window of match IDs, walking back from the newest match, and adds up games and wins per
    // hero pair. A failed window is logged and counted rather than fatal; the caller decides if enough worked
    private async Task<(MatchupTotals Totals, int FailedWindows)> FetchTotalsAsync(
        long latestMatchId, HashSet<int> validHeroIds, CancellationToken ct)
    {
        var windowSize = _matchupOptions.WindowSize;
        var windowCount = _matchupOptions.WindowCount;
        var totals = new MatchupTotals();
        var failedWindows = 0;

        logger.LogInformation(
            "Starting public matchup ingestion: {WindowCount} windows of {WindowSize} match IDs back from {LatestMatchId}...",
            windowCount, windowSize, latestMatchId);

        for (var i = 0; i < windowCount; i++)
        {
            // Rate limiting: the latest-match query was the first request
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
                logger.LogWarning(ex, "Failed to retrieve public matchups for match IDs {From}-{To}", fromMatchId, toMatchId);
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

        return (totals, failedWindows);
    }

    // Replaces the table's contents in one save: updates or adds every pair seen, and removes pairs that
    // weren't, so no rows from an older seed linger. Every hero's baseline win rate is set from the same totals
    // in that save, so baselines and matchups always agree. Returns the number of matchup rows written
    private async Task<int> ReplaceMatchupsAsync(MatchupTotals totals, CancellationToken ct)
    {
        var totalsByHero = totals
            .GroupBy(t => t.Key.HeroId)
            .ToDictionary(g => g.Key, g => (Wins: g.Sum(t => (long)t.Value.Wins), Games: g.Sum(t => (long)t.Value.Games)));
        foreach (var hero in await db.Heroes.ToListAsync(ct))
        {
            var (wins, games) = totalsByHero.GetValueOrDefault(hero.Id);
            hero.BaselineWinRate = CounterRanking.BaselineWinRate(wins, games);
        }

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

        // Counted before saving: the save also writes the heroes whose baseline changed
        var saved = db.ChangeTracker.Entries<HeroMatchup>()
            .Count(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }

        logger.LogInformation(
            "Public matchup ingestion complete: {Pairs} pairs, {Saved} rows written ({Removed} removed).",
            totals.Count, saved, stale.Count);
        return saved;
    }
}
