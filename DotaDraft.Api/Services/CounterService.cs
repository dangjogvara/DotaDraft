using DotaDraft.Api.Data;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Services;

// Ranks counter picks from the stored matchups; see CounterRanking for the formula
public class CounterService(AppDbContext db, IOptions<CounterOptions> options)
{
    private readonly int _priorGames = options.Value.PriorGames;

    public async Task<DraftCountersResult> GetDraftCountersAsync(
        IReadOnlyList<int> enemyIds, IReadOnlyList<int> excludedIds, int limit, CancellationToken ct)
    {
        // Enemies and excluded heroes are never suggested
        var unavailableIds = enemyIds.Concat(excludedIds).ToList();

        var unknown = await FindUnknownHeroIdsAsync(unavailableIds, ct);
        if (unknown.Count > 0)
            return new DraftCountersResult([], unknown);

        var candidates = await db.Heroes.AsNoTracking().Where(h => !unavailableIds.Contains(h.Id)).ToListAsync(ct);

        // Heroes without games against any of the enemies have nothing to rank them by, so they're left out
        var counters = (await ScoreAsync(candidates, enemyIds, ct))
            .Where(c => c.Matchups.Any(m => m.GamesPlayed > 0))
            .OrderByDescending(c => c.TotalAdvantage)
            .ThenByDescending(c => c.Matchups.Sum(e => e.GamesPlayed))
            .Take(limit)
            .ToList();

        return new DraftCountersResult(counters, []);
    }

    /// <summary>How each of your team's heroes does against the enemy lineup, in pick order.</summary>
    public async Task<TeamMatchupResult> GetTeamMatchupAsync(
        IReadOnlyList<int> teamIds, IReadOnlyList<int> enemyIds, CancellationToken ct)
    {
        var unknown = await FindUnknownHeroIdsAsync(teamIds.Concat(enemyIds).ToList(), ct);
        if (unknown.Count > 0)
            return new TeamMatchupResult(null, unknown);

        var heroesById = await db.Heroes.AsNoTracking().Where(h => teamIds.Contains(h.Id)).ToDictionaryAsync(h => h.Id, ct);
        var heroes = await ScoreAsync(teamIds.Select(id => heroesById[id]).ToList(), enemyIds, ct);

        return new TeamMatchupResult(new TeamMatchupDto(heroes.Sum(h => h.TotalAdvantage), heroes), []);
    }

    private async Task<List<int>> FindUnknownHeroIdsAsync(IReadOnlyList<int> heroIds, CancellationToken ct)
    {
        var known = await db.Heroes.Where(h => heroIds.Contains(h.Id)).Select(h => h.Id).ToListAsync(ct);
        return heroIds.Except(known).ToList();
    }

    // Each hero's edge against every enemy, summed, in the order given; see CounterRanking for the formula.
    // Counter suggestions and your team's matchup both come from here, so their numbers always agree.
    // An enemy without matchup data contributes 0: no evidence either way
    private async Task<List<DraftCounterDto>> ScoreAsync(IReadOnlyList<Hero> heroes, IReadOnlyList<int> enemyIds, CancellationToken ct)
    {
        var heroIds = heroes.Select(h => h.Id).ToList();
        var matchups = await db.HeroMatchups
            .AsNoTracking()
            .Where(m => enemyIds.Contains(m.AgainstHeroId) && heroIds.Contains(m.HeroId) && m.GamesPlayed > 0)
            .Select(m => new { m.HeroId, m.AgainstHeroId, m.Wins, m.GamesPlayed, m.WinRate })
            .ToListAsync(ct);
        var byHero = matchups.ToLookup(m => m.HeroId);

        var baselines = await LoadBaselineWinRatesAsync(ct);

        return heroes
            .Select(hero =>
            {
                var baseline = baselines.GetValueOrDefault(hero.Id, 0.5);
                var byEnemy = byHero[hero.Id].ToDictionary(m => m.AgainstHeroId);

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
            .ToList();
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
