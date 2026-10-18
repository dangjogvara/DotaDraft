using DotaDraft.Api.Data;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
        AppDbContext db,
        IOptions<CounterOptions> options,
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

        // 3. Load every hero's results against this hero (at most one row per hero)
        var matchups = await db.HeroMatchups
            .AsNoTracking()
            .Where(m => m.AgainstHeroId == id && m.GamesPlayed > 0)
            .Select(m => new
            {
                m.Hero.Id,
                m.Hero.LocalizedName,
                m.Hero.PrimaryAttr,
                m.Hero.ImgUrl,
                m.Wins,
                m.GamesPlayed,
                m.WinRate
            })
            .ToListAsync(ct);

        // 4. Each hero's overall win rate, so general strength isn't mistaken for countering
        var baselines = await db.LoadBaselineWinRatesAsync(ct);

        // 5. Rank by sample-adjusted advantage over the baseline
        var priorGames = options.Value.PriorGames;
        var counters = matchups
            .Select(m =>
            {
                var baseline = baselines.GetValueOrDefault(m.Id, 0.5);
                var advantage = CounterRanking.Advantage(m.Wins, m.GamesPlayed, baseline, priorGames);
                return new HeroCounterDto(
                    m.Id,
                    m.LocalizedName,
                    m.PrimaryAttr,
                    m.ImgUrl,
                    Math.Round((decimal)advantage * 100m, 2),
                    m.WinRate,
                    Math.Round((decimal)baseline * 100m, 2),
                    m.GamesPlayed);
            })
            .OrderByDescending(c => c.Advantage)
            .ThenByDescending(c => c.GamesPlayed)
            .Take(clampedLimit)
            .ToList();

        return TypedResults.Ok(counters);
    }
}