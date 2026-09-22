using DotaDraft.Api.Services;

namespace DotaDraft.Api.Endpoints;

public static class AdminEndpoints
{
    public static void RegisterAdminEndpoints(this WebApplication app)
    {
        var adminApi = app.MapGroup("/api/admin");

        adminApi.MapPost("/seed-heroes", SeedHeroes);
    }

    private static async Task<IResult> SeedHeroes(OpenDotaService service, CancellationToken ct)
    {
        var count = await service.SeedHeroesAsync(ct);
        return TypedResults.Ok(new { message = $"Seeded {count} heroes." });
    }
}