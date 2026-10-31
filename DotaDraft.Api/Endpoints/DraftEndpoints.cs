using System.ComponentModel.DataAnnotations;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DotaDraft.Api.Endpoints;

public static class DraftEndpoints
{
    public const int MaxEnemies = 5;
    public const int MaxExcluded = 5;
    public const int MaxLimit = 20;

    private const string LineupSizeError = "Pass 1 to 5 enemy hero IDs, e.g. ?against=1&against=14.";
    private const string ExcludeSizeError = "Pass at most 5 hero IDs to exclude, e.g. &exclude=5&exclude=8.";
    private const string TeamSizeError = "Pass 1 to 5 hero IDs for your team, e.g. ?team=5&team=8.";

    public static void RegisterDraftEndpoints(this WebApplication app)
    {
        // Repeated keys bind to the array: /api/counters?against=1&against=14
        app.MapGet("/api/counters", GetDraftCounters)
            .WithName(nameof(GetDraftCounters))
            .WithTags("Counters");

        // /api/team-matchup?team=5&team=8&against=1&against=14
        app.MapGet("/api/team-matchup", GetTeamMatchup)
            .WithName(nameof(GetTeamMatchup))
            .WithTags("Counters");
    }

    // The attributes are checked by the framework's validation (AddValidation) before the handler runs,
    // returning a 400 ValidationProblem. The overlap check spans two parameters, so the handler does it;
    // only the unknown-ID check needs the database, so CounterService does that one
    /// <summary>Best counter picks against an enemy lineup.</summary>
    /// <remarks>
    /// Ranked by each hero's advantage summed over the enemies, so heroes that counter several picks rank highest.
    /// Enemy heroes and excluded heroes are never suggested.
    /// </remarks>
    /// <param name="against">1 to 5 distinct enemy hero IDs. Repeat the key for each hero: against=1, then against=14.</param>
    /// <param name="exclude">
    /// Up to 5 distinct hero IDs that can't be picked, such as your own team's picks. None may also be in against.
    /// </param>
    /// <param name="limit">How many counters to return, 1 to 20.</param>
    internal static async Task<Results<Ok<IReadOnlyList<DraftCounterDto>>, ValidationProblem>> GetDraftCounters(
        [Required(ErrorMessage = LineupSizeError)]
        [Length(1, MaxEnemies, ErrorMessage = LineupSizeError)]
        [Distinct(ErrorMessage = "Enemy hero IDs must be distinct.")]
        int[]? against,
        [Length(0, MaxExcluded, ErrorMessage = ExcludeSizeError)]
        [Distinct(ErrorMessage = "Excluded hero IDs must be distinct.")]
        int[]? exclude,
        CounterService counters,
        [Range(1, MaxLimit)] int limit = 10,
        CancellationToken ct = default)
    {
        exclude ??= [];
        var inBoth = against!.Intersect(exclude).ToList();
        if (inBoth.Count > 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["exclude"] = [$"Hero IDs can't be both enemies and excluded: {string.Join(", ", inBoth)}."]
            });
        }

        var result = await counters.GetDraftCountersAsync(against, exclude, limit, ct);
        if (result.UnknownHeroIds.Count > 0)
            return UnknownHeroes(result.UnknownHeroIds, ("against", against), ("exclude", exclude));

        return TypedResults.Ok(result.Counters);
    }

    /// <summary>How your team's heroes do against an enemy lineup.</summary>
    /// <remarks>
    /// Each of your heroes is scored exactly like a counter suggestion: its advantage against each enemy, summed.
    /// The total adds up your whole team. It's based on 1-vs-1 matchups only, so it isn't a win prediction.
    /// </remarks>
    /// <param name="team">1 to 5 distinct hero IDs on your team, in pick order. None may also be in against.</param>
    /// <param name="against">1 to 5 distinct enemy hero IDs.</param>
    internal static async Task<Results<Ok<TeamMatchupDto>, ValidationProblem>> GetTeamMatchup(
        [Required(ErrorMessage = TeamSizeError)]
        [Length(1, MaxEnemies, ErrorMessage = TeamSizeError)]
        [Distinct(ErrorMessage = "Your team's hero IDs must be distinct.")]
        int[]? team,
        [Required(ErrorMessage = LineupSizeError)]
        [Length(1, MaxEnemies, ErrorMessage = LineupSizeError)]
        [Distinct(ErrorMessage = "Enemy hero IDs must be distinct.")]
        int[]? against,
        CounterService counters,
        CancellationToken ct = default)
    {
        var inBoth = team!.Intersect(against!).ToList();
        if (inBoth.Count > 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["team"] = [$"Hero IDs can't be on both teams: {string.Join(", ", inBoth)}."]
            });
        }

        var result = await counters.GetTeamMatchupAsync(team, against, ct);
        if (result.Matchup is null)
            return UnknownHeroes(result.UnknownHeroIds, ("team", team), ("against", against));

        return TypedResults.Ok(result.Matchup);
    }

    // Reports unknown IDs under the parameter each one came from
    private static ValidationProblem UnknownHeroes(IReadOnlyList<int> unknownIds, params (string Name, int[] Ids)[] parameters)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var (name, ids) in parameters)
        {
            var unknown = ids.Where(unknownIds.Contains).ToList();
            if (unknown.Count > 0)
                errors[name] = [$"Unknown hero IDs: {string.Join(", ", unknown)}."];
        }
        return TypedResults.ValidationProblem(errors);
    }
}
