using DotaDraft.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace DotaDraft.Api.Endpoints;

public static class HeroEndpoints
{
    public static void RegisterHeroEndpoints(this WebApplication app)
    {
        var heroesApi = app.MapGroup("/api/heroes");

        heroesApi.MapGet("/", GetHeroes);
    }

    private static async Task<IResult> GetHeroes(AppDbContext db)
    {
        var heroes = await db.Heroes
            .OrderBy(h => h.LocalizedName)
            .ToListAsync();
        return TypedResults.Ok(heroes);
    }
}