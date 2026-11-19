using System.Net;
using System.Net.Http.Json;
using DotaDraft.Api.Data;
using DotaDraft.Api.Dtos;
using DotaDraft.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace DotaDraft.Api.Tests.Endpoints;

public class DraftEndpointsTests : IAsyncLifetime
{
    private DotaDraftApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = await DotaDraftApiFactory.CreateAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The baselines a seed would store for the matchups below (all wins / all games); 50% without games
        var baselines = new Dictionary<int, double> { [2] = 100 / 200.0, [3] = 170 / 300.0, [4] = 100 / 200.0, [5] = 180 / 300.0 };
        db.Heroes.AddRange(Enumerable.Range(1, 6).Select(id => new Hero
        {
            Id = id, Name = $"npc_dota_hero_{id}", LocalizedName = $"Hero {id}", PrimaryAttr = "str",
            Roles = id == 4 ? ["Carry"] : ["Support"],
            BaselineWinRate = baselines.GetValueOrDefault(id, Hero.DefaultBaselineWinRate)
        }));

        // Enemy lineup in the tests: heroes 1 and 2
        // - hero 3: 70% against both enemies, 30% vs hero 6 (baseline 56.67%): counters both moderately
        // - hero 4: 75% against hero 1 only, no data vs hero 2 (baseline 50%): counters one strongly
        // - hero 5: 60% against everyone (baseline 60%): no real edge
        // - hero 2 beats hero 1 90% of the time, but is an enemy, so must not be suggested. It loses to hero 6, so its
        //   baseline is 50% and, like hero 1, it doesn't move what the others are expected to win (see CounterRankingTests)
        db.HeroMatchups.AddRange(
            Matchup(3, 1, 70, 100), Matchup(3, 2, 70, 100), Matchup(3, 6, 30, 100),
            Matchup(4, 1, 75, 100), Matchup(4, 6, 25, 100),
            Matchup(5, 1, 60, 100), Matchup(5, 2, 60, 100), Matchup(5, 6, 60, 100),
            Matchup(2, 1, 90, 100), Matchup(2, 6, 10, 100));
        db.SaveChanges();
    }

    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    private static HeroMatchup Matchup(int heroId, int againstHeroId, int wins, int games) => new()
    {
        HeroId = heroId,
        AgainstHeroId = againstHeroId,
        Wins = wins,
        GamesPlayed = games,
        WinRate = Math.Round((decimal)wins / games * 100m, 2)
    };

    private Task<HttpResponseMessage> Get(string query) => _factory.CreateClient().GetAsync($"/api/counters{query}");

    private async Task<List<HeroScoreDto>> GetCounters(string query)
    {
        var response = await Get(query);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<List<HeroScoreDto>>())!;
    }

    [Fact]
    public async Task RanksByAdvantageSummedOverTheLineup()
    {
        var counters = await GetCounters("?against=1&against=2");

        // Hero 3: 2 x (70 - 56.67) / 130 = 20.52 points beats hero 4's single (75 - 50) / 130 = 19.23
        Assert.Equal([3, 4, 5], counters.Select(c => c.Id));
        Assert.Equal(20.52m, counters[0].TotalAdvantage);
        Assert.Equal(19.23m, counters[1].TotalAdvantage);
    }

    [Fact]
    public async Task ExcludesHeroesInTheLineup()
    {
        var counters = await GetCounters("?against=1&against=2");

        Assert.DoesNotContain(counters, c => c.Id is 1 or 2);
    }

    [Fact]
    public async Task LeavesOutExcludedHeroes()
    {
        // Hero 3 would rank first, but is already on the user's team
        var counters = await GetCounters("?against=1&against=2&exclude=3");

        Assert.Equal([4, 5], counters.Select(c => c.Id));
    }

    [Fact]
    public async Task ReturnsPerEnemyBreakdown_InRequestOrder_WithZeroForMissingData()
    {
        var hero4 = (await GetCounters("?against=2&against=1")).Single(c => c.Id == 4);

        Assert.Equal([2, 1], hero4.Matchups.Select(m => m.EnemyId));
        Assert.Equal(new EnemyMatchupDto(2, 0m, null, null, 0), hero4.Matchups[0]);
        Assert.Equal(new EnemyMatchupDto(1, 19.23m, 75m, 50m, 100), hero4.Matchups[1]);
        Assert.Equal(50m, hero4.BaselineWinRate);
    }

    [Fact]
    public async Task ReturnsExpectedWinRatePerEnemy()
    {
        var hero3 = (await GetCounters("?against=1&against=2")).Single(c => c.Id == 3);

        // Hero 3's baseline is 170/300 = 56.67% against 50% enemies
        Assert.Equal([56.67m, 56.67m], hero3.Matchups.Select(m => m.ExpectedWinRate));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(20, 3)]
    public async Task AppliesLimit(int limit, int expectedCount)
    {
        var counters = await GetCounters($"?against=1&against=2&limit={limit}");

        Assert.Equal(expectedCount, counters.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task RejectsLimitOutOfRange(int limit)
    {
        var response = await Get($"?against=1&limit={limit}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains("between 1 and 20", Assert.Single(problem!.Errors["limit"]));
    }

    [Theory]
    [InlineData("", "Pass 1 to 5")]
    [InlineData("?against=1&against=2&against=3&against=4&against=5&against=6", "Pass 1 to 5")]
    [InlineData("?against=1&against=1", "distinct")]
    [InlineData("?against=1&against=999", "Unknown hero IDs: 999")]
    public async Task RejectsInvalidLineups(string query, string expectedError)
    {
        var response = await Get(query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(expectedError, Assert.Single(problem!.Errors["against"]));
    }

    [Theory]
    [InlineData("&exclude=3&exclude=4&exclude=5&exclude=6&exclude=7&exclude=8", "at most 5")]
    [InlineData("&exclude=3&exclude=3", "distinct")]
    [InlineData("&exclude=3&exclude=999", "Unknown hero IDs: 999")]
    [InlineData("&exclude=3&exclude=1", "both enemies and excluded: 1")]
    public async Task RejectsInvalidExclusions(string exclude, string expectedError)
    {
        var response = await Get($"?against=1&against=2{exclude}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(expectedError, Assert.Single(problem!.Errors["exclude"]));
        Assert.False(problem.Errors.ContainsKey("against"));
    }

    [Fact]
    public async Task FiltersByRole()
    {
        // Hero 4 (the carry) would rank second
        var counters = await GetCounters("?against=1&against=2&role=support");

        Assert.Equal([3, 5], counters.Select(c => c.Id));
    }

    [Fact]
    public async Task RejectsAnUnknownRole()
    {
        var response = await Get("?against=1&role=Jungler");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Equal("Unknown role: Jungler. Known roles: Carry, Support.", Assert.Single(problem!.Errors["role"]));
    }

    [Fact]
    public async Task ReportsUnknownIds_UnderTheParameterTheyCameFrom()
    {
        var response = await Get("?against=1&against=998&exclude=999");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Equal("Unknown hero IDs: 998.", Assert.Single(problem!.Errors["against"]));
        Assert.Equal("Unknown hero IDs: 999.", Assert.Single(problem.Errors["exclude"]));
    }

    private async Task<TeamMatchupDto> GetTeamMatchup(string query)
    {
        var response = await _factory.CreateClient().GetAsync($"/api/team-matchup{query}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TeamMatchupDto>())!;
    }

    [Fact]
    public async Task TeamMatchup_ScoresEachHeroInPickOrder_AndSumsThem()
    {
        var matchup = await GetTeamMatchup("?team=4&team=3&against=1&against=2");

        Assert.Equal([4, 3], matchup.Heroes.Select(h => h.Id));
        Assert.Equal(19.23m, matchup.Heroes[0].TotalAdvantage);
        Assert.Equal(20.52m, matchup.Heroes[1].TotalAdvantage);
        Assert.Equal(39.75m, matchup.TotalAdvantage);
    }

    [Fact]
    public async Task TeamMatchup_ScoresHeroesExactlyLikeCounterSuggestions()
    {
        var counters = await GetCounters("?against=1&against=2");
        var matchup = await GetTeamMatchup("?team=3&team=4&team=5&against=1&against=2");

        foreach (var hero in matchup.Heroes)
        {
            var counter = counters.Single(c => c.Id == hero.Id);
            Assert.Equal(counter with { Matchups = [] }, hero with { Matchups = [] });
            Assert.Equal(counter.Matchups, hero.Matchups);
        }
    }

    [Fact]
    public async Task TeamMatchup_IncludesHeroesWithoutData_AsZero()
    {
        // Hero 6 has no matchup data of its own, so it has no edge to show
        var hero = Assert.Single((await GetTeamMatchup("?team=6&against=1")).Heroes);

        Assert.Equal(6, hero.Id);
        Assert.Equal(0m, hero.TotalAdvantage);
        Assert.Equal([new EnemyMatchupDto(1, 0m, null, null, 0)], hero.Matchups);
    }

    [Theory]
    [InlineData("?against=1", "team", "Pass 1 to 5 hero IDs for your team")]
    [InlineData("?team=3&team=4&team=5&team=6&team=7&team=8&against=1", "team", "Pass 1 to 5 hero IDs for your team")]
    [InlineData("?team=3&team=3&against=1", "team", "distinct")]
    [InlineData("?team=3&team=999&against=1", "team", "Unknown hero IDs: 999")]
    [InlineData("?team=3&team=1&against=1", "team", "on both teams: 1")]
    [InlineData("?team=3", "against", "Pass 1 to 5 enemy hero IDs")]
    [InlineData("?team=3&against=998", "against", "Unknown hero IDs: 998")]
    public async Task TeamMatchup_RejectsInvalidTeams(string query, string parameter, string expectedError)
    {
        var response = await _factory.CreateClient().GetAsync($"/api/team-matchup{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.Contains(expectedError, Assert.Single(problem!.Errors[parameter]));
    }
}
