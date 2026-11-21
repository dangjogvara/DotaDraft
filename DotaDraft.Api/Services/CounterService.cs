using DotaDraft.Api.Data;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Services;

// Each query checks its own hero IDs, so it can't run on IDs that weren't checked. The formula is in CounterRanking
public class CounterService(AppDbContext db, IOptions<CounterOptions> options)
{
    private readonly int _priorGames = options.Value.PriorGames;

    /// <summary>The best counter picks, never enemies or excluded heroes. Errors are keyed by query parameter.</summary>
    /// <param name="role">Only heroes with this role (case-insensitive); null or blank for any.</param>
    public async Task<CheckedResult<IReadOnlyList<HeroScoreDto>>> GetDraftCountersAsync(
        IReadOnlyList<int> enemyIds, IReadOnlyList<int> excludedIds, string? role, int limit, CancellationToken ct)
    {
        // Any hero can be a counter, so load them all (about 125): one load serves the checks, the candidates and
        // the enemies' baselines
        var heroesById = await db.Heroes.AsNoTracking().ToDictionaryAsync(h => h.Id, ct);

        var errors = CheckHeroes(enemyIds, excludedIds, heroesById,
            otherName: "exclude", inBothError: "Hero IDs can't be both enemies and excluded");
        var knownRole = FindRole(role, heroesById.Values, errors);
        if (errors.Count > 0)
            return CheckedResult<IReadOnlyList<HeroScoreDto>>.Invalid(errors);

        // The role filter comes before ranking and the limit, so a role still gets up to `limit` suggestions
        var unavailableIds = enemyIds.Concat(excludedIds).ToHashSet();
        var candidates = heroesById.Values
            .Where(h => !unavailableIds.Contains(h.Id) && (knownRole is null || h.Roles.Contains(knownRole)))
            .ToList();

        // Heroes without games against any of the enemies have nothing to rank them by, so they're left out
        var counters = (await ScoreAsync(candidates, enemyIds, heroesById, ct))
            .Where(c => c.Matchups.Any(m => m.GamesPlayed > 0))
            .OrderByDescending(c => c.TotalAdvantage)
            .ThenByDescending(c => c.Matchups.Sum(e => e.GamesPlayed))
            .Take(limit)
            .ToList();
        return CheckedResult<IReadOnlyList<HeroScoreDto>>.Valid(counters);
    }

    /// <summary>Your team's heroes scored against the enemy lineup, in pick order. Errors are keyed by query parameter.</summary>
    public async Task<CheckedResult<TeamMatchupDto>> GetTeamMatchupAsync(
        IReadOnlyList<int> teamIds, IReadOnlyList<int> enemyIds, CancellationToken ct)
    {
        var heroesById = await db.Heroes
            .AsNoTracking()
            .Where(h => teamIds.Contains(h.Id) || enemyIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, ct);

        var errors = CheckHeroes(enemyIds, teamIds, heroesById,
            otherName: "team", inBothError: "Hero IDs can't be on both teams");
        if (errors.Count > 0)
            return CheckedResult<TeamMatchupDto>.Invalid(errors);

        var heroes = await ScoreAsync(teamIds.Select(id => heroesById[id]).ToList(), enemyIds, heroesById, ct);
        return CheckedResult<TeamMatchupDto>.Valid(new TeamMatchupDto(heroes.Sum(h => h.TotalAdvantage), heroes));
    }

    // A hero in both lists is reported alone, under the other list's name. Otherwise unknown IDs are reported
    // under the list they came from
    private static Dictionary<string, string[]> CheckHeroes(
        IReadOnlyList<int> enemyIds, IReadOnlyList<int> otherIds, IReadOnlyDictionary<int, Hero> heroesById,
        string otherName, string inBothError)
    {
        var errors = new Dictionary<string, string[]>();

        var inBoth = enemyIds.Intersect(otherIds).ToList();
        if (inBoth.Count > 0)
        {
            errors[otherName] = [$"{inBothError}: {string.Join(", ", inBoth)}."];
            return errors;
        }

        AddUnknown(errors, "against", enemyIds, heroesById);
        AddUnknown(errors, otherName, otherIds, heroesById);
        return errors;
    }

    // The role as the heroes spell it ("support" -> "Support"), or null for no filter
    private static string? FindRole(string? role, IEnumerable<Hero> heroes, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

        var knownRoles = heroes.SelectMany(h => h.Roles).Distinct().Order(StringComparer.Ordinal).ToList();
        var match = knownRoles.FirstOrDefault(r => string.Equals(r, role.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null)
            errors["role"] = [$"Unknown role: {role}. Known roles: {string.Join(", ", knownRoles)}."];
        return match;
    }

    private static void AddUnknown(
        Dictionary<string, string[]> errors, string name, IReadOnlyList<int> ids, IReadOnlyDictionary<int, Hero> heroesById)
    {
        var unknown = ids.Where(id => !heroesById.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
            errors[name] = [$"Unknown hero IDs: {string.Join(", ", unknown)}."];
    }

    private async Task<List<HeroScoreDto>> ScoreAsync(
        IReadOnlyList<Hero> heroes, IReadOnlyList<int> enemyIds, IReadOnlyDictionary<int, Hero> heroesById, CancellationToken ct)
    {
        var enemyBaselines = heroesById.Values
            .Where(h => enemyIds.Contains(h.Id))
            .ToDictionary(h => h.Id, h => h.BaselineWinRate);
        var heroIds = heroes.Select(h => h.Id).ToList();
        var matchups = await db.HeroMatchups
            .AsNoTracking()
            .Where(m => enemyIds.Contains(m.AgainstHeroId) && heroIds.Contains(m.HeroId))
            .ToListAsync(ct);

        return CounterRanking.Score(heroes, enemyIds, enemyBaselines, matchups, _priorGames);
    }
}
