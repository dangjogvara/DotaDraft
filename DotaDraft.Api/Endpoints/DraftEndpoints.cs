using System.ComponentModel.DataAnnotations;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DotaDraft.Api.Endpoints;

public static class DraftEndpoints
{
    public const int MaxHeroesPerTeam = 5;
    public const int MaxLimit = 20;

    private const string AgainstError = "Pass 1 to 5 enemy hero IDs, e.g. ?against=1&against=14.";
    private const string ExcludeError = "Pass at most 5 hero IDs to exclude, e.g. &exclude=5&exclude=8.";
    private const string TeamError = "Pass 1 to 5 hero IDs for your team, e.g. ?team=5&team=8.";

    public static void RegisterDraftEndpoints(this WebApplication app)
    {
        app.MapGet("/api/counters", GetDraftCounters)
            .WithName(nameof(GetDraftCounters))
            .WithTags("Counters");

        app.MapGet("/api/team-matchup", GetTeamMatchup)
            .WithName(nameof(GetTeamMatchup))
            .WithTags("Counters");
    }

    // ASP.NET fills each parameter from the query string; a repeated key becomes an array (against=1&against=14).
    // The attributes are checked by the framework's validation before the method runs; a missing required list
    // arrives as an empty array and fails [Length]. The service then does the checks that need both lists or the
    // database, and the handler maps its errors to a ValidationProblem.
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
        [Length(1, MaxHeroesPerTeam, ErrorMessage = AgainstError), Distinct(ErrorMessage = "Enemy hero IDs must be distinct.")]
        int[] against,
        [Length(0, MaxHeroesPerTeam, ErrorMessage = ExcludeError), Distinct(ErrorMessage = "Excluded hero IDs must be distinct.")]
        int[]? exclude,
        CounterService counters,
        [Range(1, MaxLimit)] int limit = 10,
        CancellationToken ct = default)
    {
        int[] excluded = exclude ?? [];  // exclude is optional: leaving it out excludes no one

        var errors = await counters.CheckDraftAsync(against, excluded, ct);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var counterPicks = await counters.GetDraftCountersAsync(against, excluded, limit, ct);
        return TypedResults.Ok(counterPicks);
    }

    /// <summary>How your team's heroes do against an enemy lineup.</summary>
    /// <remarks>
    /// Each of your heroes is scored exactly like a counter suggestion: its advantage against each enemy, summed.
    /// The total adds up your whole team. It's based on 1-vs-1 matchups only, so it isn't a win prediction.
    /// </remarks>
    /// <param name="team">1 to 5 distinct hero IDs on your team, in pick order. None may also be in against.</param>
    /// <param name="against">1 to 5 distinct enemy hero IDs.</param>
    internal static async Task<Results<Ok<TeamMatchupDto>, ValidationProblem>> GetTeamMatchup(
        [Length(1, MaxHeroesPerTeam, ErrorMessage = TeamError), Distinct(ErrorMessage = "Your team's hero IDs must be distinct.")]
        int[] team,
        [Length(1, MaxHeroesPerTeam, ErrorMessage = AgainstError), Distinct(ErrorMessage = "Enemy hero IDs must be distinct.")]
        int[] against,
        CounterService counters,
        CancellationToken ct = default)
    {
        var errors = await counters.CheckTeamMatchupAsync(team, against, ct);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var matchup = await counters.GetTeamMatchupAsync(team, against, ct);
        return TypedResults.Ok(matchup);
    }
}
