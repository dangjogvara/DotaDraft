using DotaDraft.Api.Data;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Endpoints;

public static class DraftEndpoints
{
    public const int MaxEnemies = 5;

    public static void RegisterDraftEndpoints(this WebApplication app)
    {
        // Repeated keys bind to the array: /api/counters?against=1&against=14
        app.MapGet("/api/counters", GetDraftCounters);
    }

    private static async Task<IResult> GetDraftCounters(
        int[]? against,
        AppDbContext db,
        IOptions<CounterOptions> options,
        int limit = 10,
        CancellationToken ct = default)
    {
        // 1. Validate the enemy lineup: 1-5 distinct heroes that exist
        var enemyIds = against ?? [];
        var error = enemyIds switch
        {
            [] => "Pass 1 to 5 enemy hero IDs, e.g. ?against=1&against=14.",
            { Length: > MaxEnemies } => $"A lineup has at most {MaxEnemies} enemies.",
            _ when enemyIds.Distinct().Count() != enemyIds.Length => "Enemy hero IDs must be distinct.",
            _ => null
        };
        if (error is null)
        {
            var known = await db.Heroes.Where(h => enemyIds.Contains(h.Id)).Select(h => h.Id).ToListAsync(ct);
            var unknown = enemyIds.Except(known).ToList();
            if (unknown.Count > 0)
                error = $"Unknown hero IDs: {string.Join(", ", unknown)}.";
        }
        if (error is not null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["against"] = [error] });

        var clampedLimit = Math.Clamp(limit, 1, 20);

        // 2. Every non-enemy hero's results against any of the enemies
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

        var baselines = await db.LoadBaselineWinRatesAsync(ct);
        var priorGames = options.Value.PriorGames;

        // 3. Sum each candidate's advantage over all enemies, so countering several picks ranks highest.
        //    An enemy without matchup data contributes 0: no evidence either way
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
                            ToPoints(CounterRanking.Advantage(m.Wins, m.GamesPlayed, baseline, priorGames)),
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
            .Take(clampedLimit)
            .ToList();

        return TypedResults.Ok(counters);
    }

    private static decimal ToPoints(double fraction) => Math.Round((decimal)fraction * 100m, 2);
}
