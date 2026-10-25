using DotaDraft.Api.Data;
using DotaDraft.Api.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Services;

// Ranks counter picks from the stored matchups; see CounterRanking for the formula
public class CounterService(AppDbContext db, IOptions<CounterOptions> options)
{
    private readonly int _priorGames = options.Value.PriorGames;

    /// <returns>The best counters to the hero, or null if it doesn't exist.</returns>
    public async Task<IReadOnlyList<HeroCounterDto>?> GetHeroCountersAsync(int heroId, int limit, CancellationToken ct)
    {
        if (!await db.Heroes.AnyAsync(h => h.Id == heroId, ct))
            return null;

        // At most one row per hero
        var matchups = await db.HeroMatchups
            .AsNoTracking()
            .Where(m => m.AgainstHeroId == heroId && m.GamesPlayed > 0)
            .Select(m => new
            {
                m.Hero.Id,
                m.Hero.LocalizedName,
                m.Hero.PrimaryAttr,
                m.Hero.ImgUrl,
                m.Wins,
                m.GamesPlayed,
                m.WinRate
            })
            .ToListAsync(ct);

        var baselines = await LoadBaselineWinRatesAsync(ct);

        return matchups
            .Select(m =>
            {
                var baseline = baselines.GetValueOrDefault(m.Id, 0.5);
                return new HeroCounterDto(
                    m.Id,
                    m.LocalizedName,
                    m.PrimaryAttr,
                    m.ImgUrl,
                    ToPoints(CounterRanking.Advantage(m.Wins, m.GamesPlayed, baseline, _priorGames)),
                    m.WinRate,
                    ToPoints(baseline),
                    m.GamesPlayed);
            })
            .OrderByDescending(c => c.Advantage)
            .ThenByDescending(c => c.GamesPlayed)
            .Take(limit)
            .ToList();
    }

    public async Task<DraftCountersResult> GetDraftCountersAsync(IReadOnlyList<int> enemyIds, int limit, CancellationToken ct)
    {
        var known = await db.Heroes.Where(h => enemyIds.Contains(h.Id)).Select(h => h.Id).ToListAsync(ct);
        var unknown = enemyIds.Except(known).ToList();
        if (unknown.Count > 0)
            return new DraftCountersResult([], unknown);

        // Every non-enemy hero's results against any of the enemies
        var matchups = await db.HeroMatchups
            .AsNoTracking()
            .Where(m => enemyIds.Contains(m.AgainstHeroId) && !enemyIds.Contains(m.HeroId) && m.GamesPlayed > 0)
            .Select(m => new { m.HeroId, m.AgainstHeroId, m.Wins, m.GamesPlayed, m.WinRate })
            .ToListAsync(ct);

        var candidateIds = matchups.Select(m => m.HeroId).Distinct().ToList();
        var heroes = await db.Heroes
            .AsNoTracking()
            .Where(h => candidateIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, ct);

        var baselines = await LoadBaselineWinRatesAsync(ct);

        // Sum each candidate's advantage over all enemies, so countering several picks ranks highest.
        // An enemy without matchup data contributes 0: no evidence either way
        var counters = matchups
            .GroupBy(m => m.HeroId)
            .Select(g =>
            {
                var hero = heroes[g.Key];
                var baseline = baselines.GetValueOrDefault(g.Key, 0.5);
                var byEnemy = g.ToDictionary(m => m.AgainstHeroId);

                var perEnemy = enemyIds
                    .Select(enemyId => byEnemy.TryGetValue(enemyId, out var m)
                        ? new EnemyMatchupDto(
                            enemyId,
                            ToPoints(CounterRanking.Advantage(m.Wins, m.GamesPlayed, baseline, _priorGames)),
                            m.WinRate,
                            m.GamesPlayed)
                        : new EnemyMatchupDto(enemyId, 0m, null, 0))
                    .ToList();

                return new DraftCounterDto(
                    hero.Id,
                    hero.LocalizedName,
                    hero.PrimaryAttr,
                    hero.ImgUrl,
                    perEnemy.Sum(e => e.Advantage),
                    ToPoints(baseline),
                    perEnemy);
            })
            .OrderByDescending(c => c.TotalAdvantage)
            .ThenByDescending(c => c.Matchups.Sum(e => e.GamesPlayed))
            .Take(limit)
            .ToList();

        return new DraftCountersResult(counters, []);
    }

    // Each hero's overall win rate across all its matchups, as a fraction, so general strength
    // isn't mistaken for countering. Heroes without games default to 0.5
    private Task<Dictionary<int, double>> LoadBaselineWinRatesAsync(CancellationToken ct) =>
        db.HeroMatchups
            .AsNoTracking()
            .GroupBy(m => m.HeroId)
            .Select(g => new { HeroId = g.Key, Wins = g.Sum(m => m.Wins), Games = g.Sum(m => m.GamesPlayed) })
            .ToDictionaryAsync(b => b.HeroId, b => b.Games > 0 ? (double)b.Wins / b.Games : 0.5, ct);

    private static decimal ToPoints(double fraction) => Math.Round((decimal)fraction * 100m, 2);
}
