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

    /// <summary>
    /// Problems with the hero IDs of a counters request, keyed by query parameter (against, exclude); empty when there
    /// are none. Run before <see cref="GetDraftCountersAsync"/>.
    /// </summary>
    public Task<IReadOnlyDictionary<string, string[]>> CheckDraftAsync(
        IReadOnlyList<int> enemyIds, IReadOnlyList<int> excludedIds, CancellationToken ct) =>
        CheckHeroesAsync(enemyIds, excludedIds, otherName: "exclude", inBothError: "Hero IDs can't be both enemies and excluded", ct);

    /// <summary>
    /// Problems with the hero IDs of a team matchup request, keyed by query parameter (team, against); empty when
    /// there are none. Run before <see cref="GetTeamMatchupAsync"/>, which expects every ID to be a known hero.
    /// </summary>
    public Task<IReadOnlyDictionary<string, string[]>> CheckTeamMatchupAsync(
        IReadOnlyList<int> teamIds, IReadOnlyList<int> enemyIds, CancellationToken ct) =>
        CheckHeroesAsync(enemyIds, teamIds, otherName: "team", inBothError: "Hero IDs can't be on both teams", ct);

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

    // Checks the enemy IDs together with one other list of hero IDs (excluded heroes, or your team):
    // 1. No hero may be in both lists. Reported under the other list's name, and nothing else is checked.
    // 2. Every ID must be a known hero. Each unknown ID is reported under the list it came from.
    private async Task<IReadOnlyDictionary<string, string[]>> CheckHeroesAsync(
        IReadOnlyList<int> enemyIds, IReadOnlyList<int> otherIds, string otherName, string inBothError, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        var inBoth = enemyIds.Intersect(otherIds).ToList();
        if (inBoth.Count > 0)
        {
            errors[otherName] = [$"{inBothError}: {string.Join(", ", inBoth)}."];
            return errors;
        }

        var unknownIds = await FindUnknownHeroIdsAsync([.. enemyIds, .. otherIds], ct);
        AddUnknown(errors, "against", enemyIds, unknownIds);
        AddUnknown(errors, otherName, otherIds, unknownIds);
        return errors;
    }

    // The IDs that aren't known heroes
    private async Task<HashSet<int>> FindUnknownHeroIdsAsync(IReadOnlyList<int> heroIds, CancellationToken ct)
    {
        var known = await db.Heroes.Where(h => heroIds.Contains(h.Id)).Select(h => h.Id).ToListAsync(ct);
        return heroIds.Except(known).ToHashSet();
    }

    private static void AddUnknown(Dictionary<string, string[]> errors, string name, IReadOnlyList<int> ids, HashSet<int> unknownIds)
    {
        var unknown = ids.Where(unknownIds.Contains).ToList();
        if (unknown.Count > 0)
            errors[name] = [$"Unknown hero IDs: {string.Join(", ", unknown)}."];
    }

    // Loads the heroes' matchups against the enemies and the enemies' baselines, then scores in CounterRanking.
    // Baseline win rates are stored on the heroes by the matchup seed
    private async Task<List<HeroScoreDto>> ScoreAsync(IReadOnlyList<Hero> heroes, IReadOnlyList<int> enemyIds, CancellationToken ct)
    {
        var enemyBaselines = await db.Heroes
            .AsNoTracking()
            .Where(h => enemyIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.BaselineWinRate, ct);
        var heroIds = heroes.Select(h => h.Id).ToList();
        var matchups = await db.HeroMatchups
            .AsNoTracking()
            .Where(m => enemyIds.Contains(m.AgainstHeroId) && heroIds.Contains(m.HeroId))
            .ToListAsync(ct);

        return CounterRanking.Score(heroes, enemyIds, enemyBaselines, matchups, _priorGames);
    }
}
