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

    /// <summary>The IDs that aren't known heroes, in the order given. Callers check this before the methods below.</summary>
    public async Task<IReadOnlyList<int>> FindUnknownHeroIdsAsync(IReadOnlyList<int> heroIds, CancellationToken ct)
    {
        var known = await db.Heroes.Where(h => heroIds.Contains(h.Id)).Select(h => h.Id).ToListAsync(ct);
        return heroIds.Except(known).ToList();
    }

    /// <summary>The best counter picks against the enemies, never suggesting enemies or excluded heroes.</summary>
    public async Task<IReadOnlyList<HeroScoreDto>> GetDraftCountersAsync(
        IReadOnlyList<int> enemyIds, IReadOnlyList<int> excludedIds, int limit, CancellationToken ct)
    {
        var unavailableIds = enemyIds.Concat(excludedIds).ToList();
        var candidates = await db.Heroes.AsNoTracking().Where(h => !unavailableIds.Contains(h.Id)).ToListAsync(ct);

        // Heroes without games against any of the enemies have nothing to rank them by, so they're left out
        return (await ScoreAsync(candidates, enemyIds, ct))
            .Where(c => c.Matchups.Any(m => m.GamesPlayed > 0))
            .OrderByDescending(c => c.TotalAdvantage)
            .ThenByDescending(c => c.Matchups.Sum(e => e.GamesPlayed))
            .Take(limit)
            .ToList();
    }

    /// <summary>How each of your team's heroes does against the enemy lineup, in pick order.</summary>
    public async Task<TeamMatchupDto> GetTeamMatchupAsync(
        IReadOnlyList<int> teamIds, IReadOnlyList<int> enemyIds, CancellationToken ct)
    {
        var heroesById = await db.Heroes.AsNoTracking().Where(h => teamIds.Contains(h.Id)).ToDictionaryAsync(h => h.Id, ct);
        var heroes = await ScoreAsync(teamIds.Select(id => heroesById[id]).ToList(), enemyIds, ct);

        return new TeamMatchupDto(heroes.Sum(h => h.TotalAdvantage), heroes);
    }

    // Loads what the scoring needs, then scores in CounterRanking
    private async Task<List<HeroScoreDto>> ScoreAsync(IReadOnlyList<Hero> heroes, IReadOnlyList<int> enemyIds, CancellationToken ct)
    {
        var heroIds = heroes.Select(h => h.Id).ToList();
        var matchups = await db.HeroMatchups
            .AsNoTracking()
            .Where(m => enemyIds.Contains(m.AgainstHeroId) && heroIds.Contains(m.HeroId))
            .ToListAsync(ct);

        var baselines = await LoadBaselineWinRatesAsync(ct);

        return CounterRanking.Score(heroes, enemyIds, matchups, baselines, _priorGames);
    }

    // Each hero's overall win rate across all its matchups, as a fraction, so general strength
    // isn't mistaken for countering. Heroes without games get CounterRanking's default
    private Task<Dictionary<int, double>> LoadBaselineWinRatesAsync(CancellationToken ct) =>
        db.HeroMatchups
            .AsNoTracking()
            .GroupBy(m => m.HeroId)
            .Select(g => new { HeroId = g.Key, Wins = g.Sum(m => m.Wins), Games = g.Sum(m => m.GamesPlayed) })
            .Where(b => b.Games > 0)
            .ToDictionaryAsync(b => b.HeroId, b => (double)b.Wins / b.Games, ct);
}
