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

    // A missing list arrives as an empty array, so [Length] also rejects a missing `against`.
    // Checks that need the database (unknown IDs, a hero on both lists, the role) are done by the service
    /// <summary>Best counter picks against an enemy lineup.</summary>
    /// <remarks>
    /// Ranked by each hero's advantage summed over the enemies, so heroes that counter several picks rank highest.
    /// Enemy heroes and excluded heroes are never suggested. With a role, only heroes with that role are, still up to
    /// the limit.
    /// </remarks>
    /// <param name="against">1 to 5 distinct enemy hero IDs. Repeat the key for each hero: against=1, then against=14.</param>
    /// <param name="exclude">
    /// Up to 5 distinct hero IDs that can't be picked, such as your own team's picks. None may also be in against.
    /// </param>
    /// <param name="role">
    /// Only suggest heroes with this role, such as Carry or Support (case-insensitive). Leave it out for any role.
    /// </param>
    /// <param name="limit">How many counters to return, 1 to 20.</param>
    internal static async Task<Results<Ok<IReadOnlyList<HeroScoreDto>>, ValidationProblem>> GetDraftCounters(
        [Length(1, MaxHeroesPerTeam, ErrorMessage = AgainstError), Distinct(ErrorMessage = "Enemy hero IDs must be distinct.")]
        int[] against,
        [Length(0, MaxHeroesPerTeam, ErrorMessage = ExcludeError), Distinct(ErrorMessage = "Excluded hero IDs must be distinct.")]
        int[]? exclude,
        string? role,
        CounterService counters,
        [Range(1, MaxLimit)] int limit = 10,
        CancellationToken ct = default)
    {
        var result = await counters.GetDraftCountersAsync(against, exclude ?? [], role, limit, ct);
        return result.IsValid ? TypedResults.Ok(result.Value) : TypedResults.ValidationProblem(result.Errors);
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
        var result = await counters.GetTeamMatchupAsync(team, against, ct);
        return result.IsValid ? TypedResults.Ok(result.Value) : TypedResults.ValidationProblem(result.Errors);
    }
}
