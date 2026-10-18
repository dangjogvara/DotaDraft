using DotaDraft.Api.Services;

namespace DotaDraft.Api.Endpoints;

public static class AdminEndpoints
{
    public const string AdminKeyConfigKey = "Admin:ApiKey";

    public static void RegisterAdminEndpoints(this WebApplication app)
    {
        // Fail closed: without a configured key the endpoints don't exist, rather than being open
        var adminKey = app.Configuration[AdminKeyConfigKey];
        if (string.IsNullOrWhiteSpace(adminKey))
        {
            app.Logger.LogWarning("{ConfigKey} is not configured; admin endpoints are disabled.", AdminKeyConfigKey);
            return;
        }

        var adminApi = app.MapGroup("/api/admin")
            .AddEndpointFilter(new AdminKeyFilter(adminKey));

        adminApi.MapPost("/seed-heroes", SeedHeroes);
        adminApi.MapPost("/seed-matchups", SeedMatchups);
        adminApi.MapGet("/seed-matchups/status", GetMatchupSeedStatus);
    }

    private static async Task<IResult> SeedHeroes(HeroIngestionService ingestion, CancellationToken ct)
    {
        var result = await ingestion.SeedHeroesAsync(ct);
        return ToResult(result, $"Seeded {result.Saved} heroes.");
    }

    // Matchup seeding takes minutes, so it is queued for MatchupSeedWorker instead of run in the request
    private static IResult SeedMatchups(MatchupSeedJob job)
    {
        if (!job.TryEnqueue())
        {
            return TypedResults.Problem(
                detail: "Matchup seeding is already queued or running.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Accepted("/api/admin/seed-matchups/status", ToStatusResponse(job.Status));
    }

    private static IResult GetMatchupSeedStatus(MatchupSeedJob job) =>
        TypedResults.Ok(ToStatusResponse(job.Status));

    private static object ToStatusResponse(SeedJobStatus status) => new
    {
        state = status.State.ToString(),
        status.QueuedAt,
        status.StartedAt,
        status.FinishedAt,
        saved = status.Result?.Saved,
        failedHeroIds = status.Result?.FailedHeroIds,
        failedWindows = status.Result?.FailedWindows,
        error = status.Error
    };

    private static IResult ToResult(SeedResult result, string successMessage)
    {
        if (result.Succeeded)
            return TypedResults.Ok(new { message = successMessage });

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
