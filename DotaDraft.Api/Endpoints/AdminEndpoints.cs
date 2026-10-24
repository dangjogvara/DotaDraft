using DotaDraft.Api.Authentication;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DotaDraft.Api.Endpoints;

public static class AdminEndpoints
{
    public const string AdminKeyConfigKey = "Admin:ApiKey";

    public static void RegisterAdminEndpoints(this WebApplication app)
    {
        // Fail closed: without a configured key the endpoints don't exist, rather than being open
        if (string.IsNullOrWhiteSpace(app.Configuration[AdminKeyConfigKey]))
        {
            app.Logger.LogWarning("{ConfigKey} is not configured; admin endpoints are disabled.", AdminKeyConfigKey);
            return;
        }

        var adminApi = app.MapGroup("/api/admin")
            .RequireAuthorization(AdminKeyDefaults.PolicyName)
            .WithTags("Admin")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        adminApi.MapPost("/seed-heroes", SeedHeroes)
            .WithName(nameof(SeedHeroes))
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        adminApi.MapPost("/seed-matchups", SeedMatchups)
            .WithName(nameof(SeedMatchups))
            .ProducesProblem(StatusCodes.Status409Conflict);

        adminApi.MapGet("/seed-matchups/status", GetMatchupSeedStatus)
            .WithName(nameof(GetMatchupSeedStatus));
    }

    /// <summary>Fetch heroes from OpenDota and insert or update them.</summary>
    /// <remarks>Run this before seeding matchups. Safe to repeat, e.g. after a patch.</remarks>
    internal static async Task<Results<Ok<SeedHeroesResponse>, ProblemHttpResult>> SeedHeroes(
        HeroIngestionService ingestion, CancellationToken ct)
    {
        var result = await ingestion.SeedHeroesAsync(ct);
        if (result.Succeeded)
            return TypedResults.Ok(new SeedHeroesResponse($"Seeded {result.Saved} heroes."));

        return ToProblem(result);
    }

    // Matchup seeding takes minutes, so it is queued for MatchupSeedWorker instead of run in the request
    /// <summary>Queue a matchup seeding job.</summary>
    /// <remarks>
    /// Returns at once; poll the status endpoint in the Location header. The source is set by Matchups:Source.
    /// Returns 409 while a job is queued or running.
    /// </remarks>
    internal static Results<Accepted<MatchupSeedStatusDto>, ProblemHttpResult> SeedMatchups(MatchupSeedJob job)
    {
        if (!job.TryEnqueue())
        {
            return TypedResults.Problem(
                detail: "Matchup seeding is already queued or running.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Accepted("/api/admin/seed-matchups/status", ToStatusResponse(job.Status));
    }

    /// <summary>State of the latest matchup seeding job.</summary>
    internal static Ok<MatchupSeedStatusDto> GetMatchupSeedStatus(MatchupSeedJob job) =>
        TypedResults.Ok(ToStatusResponse(job.Status));

    private static MatchupSeedStatusDto ToStatusResponse(SeedJobStatus status) => new(
        status.State.ToString(),
        status.QueuedAt,
        status.StartedAt,
        status.FinishedAt,
        status.Result?.Saved,
        status.Result?.FailedHeroIds,
        status.Result?.FailedWindows,
        status.Error);

    private static ProblemHttpResult ToProblem(SeedResult result)
    {
        var statusCode = result.Failure switch
        {
            SeedFailure.Upstream => StatusCodes.Status502BadGateway,
            SeedFailure.MissingPrerequisite => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        // Report rows that were still saved, since a matchup run continues past per-hero failures
        var extensions = new Dictionary<string, object?> { ["saved"] = result.Saved };
        if (result.FailedHeroIds.Count > 0)
            extensions["failedHeroIds"] = result.FailedHeroIds;
        if (result.FailedWindows > 0)
            extensions["failedWindows"] = result.FailedWindows;

        return TypedResults.Problem(detail: result.Error, statusCode: statusCode, extensions: extensions);
    }
}
