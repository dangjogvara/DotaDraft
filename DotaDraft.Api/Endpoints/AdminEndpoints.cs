using DotaDraft.Api.Services;

namespace DotaDraft.Api.Endpoints;

public static class AdminEndpoints
{
    public static void RegisterAdminEndpoints(this WebApplication app)
    {
        var adminApi = app.MapGroup("/api/admin");

        adminApi.MapPost("/seed-heroes", SeedHeroes);
        adminApi.MapPost("/seed-matchups", SeedMatchups);
    }

    private static async Task<IResult> SeedHeroes(OpenDotaService service, CancellationToken ct)
    {
        var result = await service.SeedHeroesAsync(ct);
        return ToResult(result, $"Seeded {result.Saved} heroes.");
    }

    private static async Task<IResult> SeedMatchups(OpenDotaService seeder, CancellationToken ct)
    {
        var result = await seeder.SeedMatchupsAsync(ct);
        return ToResult(result, $"Seeded {result.Saved} matchup records.");
    }

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

        return TypedResults.Problem(detail: result.Error, statusCode: statusCode, extensions: extensions);
    }
}
