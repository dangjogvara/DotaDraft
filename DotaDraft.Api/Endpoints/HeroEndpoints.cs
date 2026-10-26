using DotaDraft.Api.Data;
using DotaDraft.Api.Dtos;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace DotaDraft.Api.Endpoints;

public static class HeroEndpoints
{
    public static void RegisterHeroEndpoints(this WebApplication app)
    {
        var heroesApi = app.MapGroup("/api/heroes");

        heroesApi.MapGet("/", GetHeroes)
            .WithName(nameof(GetHeroes))
            .WithTags("Heroes");
    }

    // Projects to HeroDto so the public contract doesn't follow the DB schema (e.g. the Matchups navigation)
    /// <summary>List all heroes.</summary>
    /// <remarks>Sorted by name. Empty until heroes are seeded.</remarks>
    internal static async Task<Ok<List<HeroDto>>> GetHeroes(AppDbContext db, CancellationToken ct)
    {
        var heroes = await db.Heroes
            .AsNoTracking()
            .OrderBy(h => h.LocalizedName)
            .Select(h => new HeroDto(h.Id, h.Name, h.LocalizedName, h.PrimaryAttr, h.Roles, h.ImgUrl))
            .ToListAsync(ct);
        return TypedResults.Ok(heroes);
    }
}
