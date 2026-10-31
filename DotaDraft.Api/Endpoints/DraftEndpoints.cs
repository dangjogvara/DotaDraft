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
    // returning a 400 ValidationProblem. ValidateHeroIdsAsync then does the checks that span two parameters
    // or need the database
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
    internal static async Task<Results<Ok<IReadOnlyList<HeroScoreDto>>, ValidationProblem>> GetDraftCounters(
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
        if (await ValidateHeroIdsAsync(counters, against!, ("exclude", exclude), "can't be both enemies and excluded", ct)
            is { } problem)
            return problem;

        return TypedResults.Ok(await counters.GetDraftCountersAsync(against!, exclude, limit, ct));
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
        if (await ValidateHeroIdsAsync(counters, against!, ("team", team!), "can't be on both teams", ct) is { } problem)
            return problem;

        return TypedResults.Ok(await counters.GetTeamMatchupAsync(team!, against!, ct));
    }

    // Checks the enemy IDs together with one other hero-ID parameter: no hero may be in both (reported under
    // the other parameter), and every ID must be a known hero (reported under the parameter it came from).
    // Returns null when they're valid
    private static async Task<ValidationProblem?> ValidateHeroIdsAsync(
        CounterService counters, int[] against, (string Name, int[] Ids) other, string inBothError, CancellationToken ct)
    {
        var inBoth = against.Intersect(other.Ids).ToList();
        if (inBoth.Count > 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [other.Name] = [$"Hero IDs {inBothError}: {string.Join(", ", inBoth)}."]
            });
        }

        var unknownIds = await counters.FindUnknownHeroIdsAsync([.. against, .. other.Ids], ct);
        if (unknownIds.Count == 0)
            return null;

        var errors = new Dictionary<string, string[]>();
        foreach (var (name, ids) in new[] { ("against", against), other })
        {
            var unknown = ids.Where(unknownIds.Contains).ToList();
            if (unknown.Count > 0)
                errors[name] = [$"Unknown hero IDs: {string.Join(", ", unknown)}."];
        }
        return TypedResults.ValidationProblem(errors);
    }
}
