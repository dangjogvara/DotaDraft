using System.ComponentModel.DataAnnotations;
using DotaDraft.Api.Data;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace DotaDraft.Api.Endpoints;

public static class HeroEndpoints
{
    public static void RegisterHeroEndpoints(this WebApplication app)
    {
        var heroesApi = app.MapGroup("/api/heroes");

        heroesApi.MapGet("/", GetHeroes);
        heroesApi.MapGet("/{id:int}/counters", GetHeroCounters);
    }

    // Projects to HeroDto so the public contract doesn't follow the DB schema (e.g. the Matchups navigation)
    private static async Task<IResult> GetHeroes(AppDbContext db, CancellationToken ct)
    {
        var heroes = await db.Heroes
            .AsNoTracking()
            .OrderBy(h => h.LocalizedName)
            .Select(h => new HeroDto(h.Id, h.Name, h.LocalizedName, h.PrimaryAttr, h.Roles, h.ImgUrl))
            .ToListAsync(ct);
        return TypedResults.Ok(heroes);
    }

    private static async Task<IResult> GetHeroCounters(
        int id,
        CounterService counters,
        [Range(1, DraftEndpoints.MaxLimit)] int limit = 5,
        CancellationToken ct = default)
    {
        var result = await counters.GetHeroCountersAsync(id, limit, ct);
        if (result is null)
        {
            return TypedResults.Problem(
                title: "Hero not found",
                detail: $"Hero with ID {id} not found.",
                statusCode: StatusCodes.Status404NotFound);
        }

        return TypedResults.Ok(result);
    }
}
