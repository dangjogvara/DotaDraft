using System.ComponentModel.DataAnnotations;
using DotaDraft.Api.Data;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Services;
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

        heroesApi.MapGet("/{id:int}/counters", GetHeroCounters)
            .WithName(nameof(GetHeroCounters))
            .WithTags("Counters")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
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

    /// <summary>Best counter picks against one hero.</summary>
    /// <remarks>Ranked by sample-adjusted advantage over each counter's own baseline win rate.</remarks>
    /// <param name="id">The enemy hero's ID.</param>
    /// <param name="limit">How many counters to return, 1 to 20.</param>
    internal static async Task<Results<Ok<IReadOnlyList<HeroCounterDto>>, ProblemHttpResult>> GetHeroCounters(
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
