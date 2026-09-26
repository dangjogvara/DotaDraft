using DotaDraft.Api.Data;
using DotaDraft.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace DotaDraft.Api.Endpoints;

public static class HeroEndpoints
{
    public static void RegisterHeroEndpoints(this WebApplication app)
    {
        var heroesApi = app.MapGroup("/api/heroes");

        heroesApi.MapGet("/", GetHeroes);
        heroesApi.MapGet("/{id}/counters", GetHeroCounters);
    }

    private static async Task<IResult> GetHeroes(AppDbContext db)
    {
        var heroes = await db.Heroes
            .AsNoTracking()
            .OrderBy(h => h.LocalizedName)
            .ToListAsync();
        return TypedResults.Ok(heroes);
    }

    private static async Task<IResult> GetHeroCounters(
        int id,
        AppDbContext db,
        int limit = 5,
        CancellationToken ct = default)
    {
        // 1. Verify the subject hero exists
        var heroExists = await db.Heroes.AnyAsync(h => h.Id == id, ct);
        if (!heroExists)
        {
            return TypedResults.NotFound(new { message = $"Hero with ID {id} not found." });
        }

        // 2. Clamp limit to avoid negative numbers or excessively large queries
        var clampedLimit = Math.Clamp(limit, 1, 20);

        // 3. Query heroes who face this hero and have the highest win rate
        var counters = await db.HeroMatchups
            .AsNoTracking()
            .Where(m => m.AgainstHeroId == id && m.GamesPlayed > 0)
            .OrderByDescending(m => m.Advantage)
            .Take(clampedLimit)
            .Select(m => new HeroCounterDto(
                m.Hero.Id,
                m.Hero.LocalizedName,
                m.Hero.PrimaryAttr,
                m.Hero.ImgUrl,
                m.Advantage,
                m.GamesPlayed
            ))
            .ToListAsync(ct);

        return TypedResults.Ok(counters);
    }
}