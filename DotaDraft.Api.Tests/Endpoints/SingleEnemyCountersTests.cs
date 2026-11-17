using System.Net.Http.Json;
using DotaDraft.Api.Data;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace DotaDraft.Api.Tests.Endpoints;

// The ranking against one enemy, with data where raw win rate would order the counters differently
public class SingleEnemyCountersTests : IAsyncLifetime
{
    private readonly DotaDraftApiFactory _factory = new();

    public Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The baselines a seed would store for the matchups below (all wins / all games); 50% without games
        var baselines = new Dictionary<int, double> { [2] = 52 / 100.0, [3] = 100 / 200.0, [4] = 120 / 200.0 };
        db.Heroes.AddRange(Enumerable.Range(1, 5).Select(id => new Hero
        {
            Id = id, Name = $"npc_dota_hero_{id}", LocalizedName = $"Hero {id}", PrimaryAttr = "str",
            BaselineWinRate = baselines.GetValueOrDefault(id, Hero.DefaultBaselineWinRate)
        }));

        // Counters for hero 1. Raw win rate would rank them 2, 3, 4:
        // - hero 2: lucky 5 of 6 against hero 1, about average overall
        // - hero 3: 70 of 100 against hero 1, 50% overall
        // - hero 4: 60% against everyone, so no real advantage
        db.HeroMatchups.AddRange(
            Matchup(2, 1, wins: 5, games: 6), Matchup(2, 5, wins: 47, games: 94),
            Matchup(3, 1, wins: 70, games: 100), Matchup(3, 5, wins: 30, games: 100),
            Matchup(4, 1, wins: 60, games: 100), Matchup(4, 5, wins: 60, games: 100),
            Matchup(5, 1, wins: 0, games: 0));
        db.SaveChanges();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private static HeroMatchup Matchup(int heroId, int againstHeroId, int wins, int games) => new()
    {
        HeroId = heroId,
        AgainstHeroId = againstHeroId,
        Wins = wins,
        GamesPlayed = games,
        WinRate = games > 0 ? Math.Round((decimal)wins / games * 100m, 2) : 0m
    };

    private async Task<List<HeroScoreDto>> GetCountersToHero1() =>
        (await _factory.CreateClient().GetFromJsonAsync<List<HeroScoreDto>>("/api/counters?against=1"))!;

    [Fact]
    public async Task RanksBySampleAdjustedAdvantage_NotRawWinRate()
    {
        var counters = await GetCountersToHero1();

        Assert.Equal([3, 2, 4], counters.Select(c => c.Id));
    }

    [Fact]
    public async Task ReturnsAdvantageWinRateAndBaseline()
    {
        var hero3 = (await GetCountersToHero1()).First(c => c.Id == 3);

        // (70 - 100 * 0.5) / (100 + 30) = 15.38 points
        Assert.Equal(15.38m, hero3.TotalAdvantage);
        Assert.Equal(50m, hero3.BaselineWinRate);
        Assert.Equal(new EnemyMatchupDto(1, 15.38m, 70m, 100), Assert.Single(hero3.Matchups));
    }

    [Fact]
    public async Task ExcludesMatchupsWithNoGames()
    {
        var counters = await GetCountersToHero1();

        Assert.DoesNotContain(counters, c => c.Id == 5);
    }
}
