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

            // Overwrite existing heroes too, so a patch's renames and new art are picked up
            hero.Name = dto.Name;
            hero.LocalizedName = dto.LocalizedName;
            hero.PrimaryAttr = dto.PrimaryAttr;
            hero.Roles = dto.Roles;
            // OpenDota ends image paths with an empty query string ("...png?")
            hero.ImgUrl = string.IsNullOrWhiteSpace(dto.Img)
                ? null
                : $"{_options.SteamCdnBaseUrl.TrimEnd('/')}/{dto.Img.TrimStart('/').TrimEnd('?')}";
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

    // Nothing is written unless at least half the windows succeeded, so a bad run can't swap good data for a tiny
    // sample. Returns the number of matchup rows written
    public async Task<int> SeedMatchupsAsync(CancellationToken ct = default)
    {
        var validHeroIds = await db.Heroes.Select(h => h.Id).ToHashSetAsync(ct);
        if (validHeroIds.Count == 0)
            throw new InvalidOperationException("The Heroes table is empty, so there are no heroes to seed matchups for.");

        var latestMatchId = await openDota.GetLatestPublicMatchIdAsync(ct);
        var (totals, attemptedWindows, failedWindows) = await FetchTotalsAsync(latestMatchId, validHeroIds, ct);

        var succeededWindows = attemptedWindows - failedWindows;
        if (succeededWindows * 2 < attemptedWindows || totals.Count == 0)
            throw new OpenDotaException(
                $"Only {succeededWindows} of {attemptedWindows} match windows could be fetched; existing matchups were kept.");

        var saved = await ReplaceMatchupsAsync(totals, ct);
        if (failedWindows > 0)
            logger.LogWarning("{Failed} of {WindowCount} match windows failed; matchups were saved from the other {Succeeded}.",
                failedWindows, attemptedWindows, succeededWindows);

        return saved;
    }

    // A failed window is counted rather than fatal; the caller decides whether enough worked
    private async Task<(MatchupTotals Totals, int AttemptedWindows, int FailedWindows)> FetchTotalsAsync(
        long latestMatchId, HashSet<int> validHeroIds, CancellationToken ct)
    {
        var windowSize = _matchupOptions.WindowSize;
        var windowCount = _matchupOptions.WindowCount;
        var totals = new MatchupTotals();
        var attemptedWindows = 0;
        var failedWindows = 0;

        logger.LogInformation(
            "Starting public matchup ingestion: {WindowCount} windows of {WindowSize} match IDs back from {LatestMatchId}...",
            windowCount, windowSize, latestMatchId);

        for (var i = 0; i < windowCount; i++)
        {
            var toMatchId = latestMatchId - (long)i * windowSize;
            if (toMatchId < 1) break;
            var fromMatchId = Math.Max(1, toMatchId - windowSize + 1);
            attemptedWindows++;

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

        return (totals, attemptedWindows, failedWindows);
    }

    // Replaces the whole table and every hero's baseline in one save, so baselines and matchups always agree
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
