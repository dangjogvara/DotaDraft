using DotaDraft.Api.Data;
using DotaDraft.Api.Models;
using DotaDraft.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Tests.Services;

public class CounterServiceTests : IAsyncLifetime
{
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDatabase.CreateContextAsync();
        _db.Heroes.AddRange(Enumerable.Range(1, 4).Select(id => new Hero
        {
            Id = id, Name = $"npc_dota_hero_{id}", LocalizedName = $"Hero {id}", PrimaryAttr = "str"
        }));
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private CounterService CreateService(int priorGames = 30) =>
        new(_db, Options.Create(new CounterOptions { PriorGames = priorGames }));

    private void AddMatchups(params (int HeroId, int AgainstHeroId, int Wins, int Games)[] matchups)
    {
        _db.HeroMatchups.AddRange(matchups.Select(m => new HeroMatchup
        {
            HeroId = m.HeroId,
            AgainstHeroId = m.AgainstHeroId,
            Wins = m.Wins,
            GamesPlayed = m.Games,
            WinRate = Math.Round((decimal)m.Wins / m.Games * 100m, 2)
        }));
        _db.SaveChanges();
    }

    // The value of a result the test expects to be valid; fails with the errors otherwise
    private static T ValueOf<T>(CheckedResult<T> result)
    {
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.Key}: {string.Join(" ", e.Value)}")));
        return result.Value;
    }

    [Fact]
    public async Task GetDraftCounters_ReturnsEmpty_ForHeroWithoutMatchups()
    {
        var counters = ValueOf(await CreateService().GetDraftCountersAsync([1], [], null, 5, CancellationToken.None));

        Assert.Empty(counters);
    }

    [Fact]
    public async Task GetDraftCounters_AppliesPriorGamesFromOptions()
    {
        // 6 of 10 against hero 1 and 4 of 10 against hero 4: a 50% baseline and a raw edge of 10 points
        AddMatchups((2, 1, 6, 10), (2, 4, 4, 10));

        var unsmoothed = ValueOf(await CreateService(priorGames: 0).GetDraftCountersAsync([1], [], null, 5, CancellationToken.None));
        var smoothed = ValueOf(await CreateService(priorGames: 10).GetDraftCountersAsync([1], [], null, 5, CancellationToken.None));

        Assert.Equal(10m, Assert.Single(unsmoothed).TotalAdvantage);
        Assert.Equal(5m, Assert.Single(smoothed).TotalAdvantage);
    }

    [Fact]
    public async Task GetDraftCounters_ReportsUnknownIds_InRequestOrder_UnderTheListTheyCameFrom()
    {
        var result = await CreateService().GetDraftCountersAsync([98, 1, 97], [2, 99], null, 5, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Null(result.Value);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(["Unknown hero IDs: 98, 97."], result.Errors["against"]);
        Assert.Equal(["Unknown hero IDs: 99."], result.Errors["exclude"]);
    }

    [Fact]
    public async Task GetDraftCounters_ReportsHeroesInBothLists_UnderExclude_AlongWithOtherErrors()
    {
        GiveRoles();

        var result = await CreateService().GetDraftCountersAsync([1, 2, 99], [2, 1, 97], "Jungler", 5, CancellationToken.None);

        Assert.Equal(["against", "exclude", "role"], result.Errors.Keys.Order());
        Assert.Equal(["Unknown hero IDs: 99."], result.Errors["against"]);
        Assert.Equal(["Hero IDs can't be both enemies and excluded: 1, 2.", "Unknown hero IDs: 97."], result.Errors["exclude"]);
        Assert.Equal(["Unknown role: Jungler. Known roles: Carry, Disabler, Support."], result.Errors["role"]);
    }

    [Fact]
    public async Task GetTeamMatchup_ReportsHeroesInBothLists_UnderTeam()
    {
        var result = await CreateService().GetTeamMatchupAsync([3, 1], [1], CancellationToken.None);

        var error = Assert.Single(result.Errors);
        Assert.Equal("team", error.Key);
        Assert.Equal(["Hero IDs can't be on both teams: 1."], error.Value);
    }

    [Fact]
    public async Task GetTeamMatchup_ReportsUnknownIds_UnderTheListTheyCameFrom_InsteadOfThrowing()
    {
        var result = await CreateService().GetTeamMatchupAsync([3, 99], [98], CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(["Unknown hero IDs: 99."], result.Errors["team"]);
        Assert.Equal(["Unknown hero IDs: 98."], result.Errors["against"]);
    }

    [Fact]
    public async Task GetDraftCounters_BreaksAdvantageTies_ByTotalGames()
    {
        // Both have the same advantage over hero 1 with no smoothing; hero 3 has more games behind it
        AddMatchups((2, 1, 6, 10), (2, 4, 4, 10), (3, 1, 60, 100), (3, 4, 40, 100));

        var counters = ValueOf(await CreateService(priorGames: 0).GetDraftCountersAsync([1], [], null, 5, CancellationToken.None));

        Assert.Equal([3, 2], counters.Select(c => c.Id));
        Assert.Equal(counters[0].TotalAdvantage, counters[1].TotalAdvantage);
    }

    [Fact]
    public async Task GetDraftCounters_BreaksFullTies_ByHeroId()
    {
        // An update moves hero 2's row in Postgres, so an unsorted query returns it after hero 3
        _db.Heroes.Single(h => h.Id == 2).LocalizedName = "Hero 2, renamed";
        await _db.SaveChangesAsync();
        AddMatchups((2, 1, 6, 10), (3, 1, 6, 10));

        var counters = ValueOf(await CreateService().GetDraftCountersAsync([1], [], null, 5, CancellationToken.None));

        Assert.Equal([2, 3], counters.Select(c => c.Id));
        Assert.Equal(counters[0].TotalAdvantage, counters[1].TotalAdvantage);
    }

    [Fact]
    public async Task GetTeamMatchup_IsNotInflated_ByWeakEnemies()
    {
        // Heroes 1 and 2 win 42% in general, so a 50% hero is expected to beat them 58% of the time: no counter
        // effect. Before the enemies' baselines were used this team scored +32 (8 points per hero per enemy)
        foreach (var weak in _db.Heroes.Where(h => h.Id == 1 || h.Id == 2))
            weak.BaselineWinRate = 0.42;
        AddMatchups((3, 1, 58, 100), (3, 2, 58, 100), (4, 1, 58, 100), (4, 2, 58, 100));

        var matchup = ValueOf(await CreateService(priorGames: 0).GetTeamMatchupAsync([3, 4], [1, 2], CancellationToken.None));

        Assert.Equal(0m, matchup.TotalAdvantage);
        Assert.All(matchup.Heroes, h => Assert.All(h.Matchups, m => Assert.Equal(0m, m.Advantage)));
    }

    [Fact]
    public async Task GetDraftCounters_UsesTheEnemysStoredBaseline()
    {
        // Hero 1 wins 55% in general, so hero 2 (50%) is expected to win 45% against it; 55 of 100 is a 10-point edge
        _db.Heroes.Single(h => h.Id == 1).BaselineWinRate = 0.55;
        AddMatchups((2, 1, 55, 100));

        var counter = Assert.Single(ValueOf(await CreateService(priorGames: 0).GetDraftCountersAsync([1], [], null, 5, CancellationToken.None)));

        Assert.Equal(10m, counter.TotalAdvantage);
    }

    // Roles for the role-filter tests: heroes 2 and 3 are supports, hero 4 a carry
    private void GiveRoles()
    {
        foreach (var hero in _db.Heroes)
            hero.Roles = hero.Id switch { 2 or 3 => ["Support", "Disabler"], 4 => ["Carry"], _ => [] };
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetDraftCounters_OnlySuggestsHeroesWithTheRole_BeforeApplyingTheLimit()
    {
        // Carry hero 4 is the best counter to hero 1, so without the filter it would take the only slot
        GiveRoles();
        AddMatchups((4, 1, 80, 100), (2, 1, 60, 100), (3, 1, 70, 100));

        var counters = ValueOf(await CreateService(priorGames: 0).GetDraftCountersAsync([1], [], "Support", 1, CancellationToken.None));

        Assert.Equal([3], counters.Select(c => c.Id));
    }

    [Fact]
    public async Task GetDraftCounters_MatchesTheRole_IgnoringCase()
    {
        GiveRoles();
        AddMatchups((4, 1, 80, 100), (2, 1, 60, 100));

        var counters = ValueOf(await CreateService().GetDraftCountersAsync([1], [], " support ", 5, CancellationToken.None));

        Assert.Equal([2], counters.Select(c => c.Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task GetDraftCounters_SuggestsAnyRole_WithoutOne(string? role)
    {
        GiveRoles();
        AddMatchups((4, 1, 80, 100), (2, 1, 60, 100));

        var counters = ValueOf(await CreateService().GetDraftCountersAsync([1], [], role, 5, CancellationToken.None));

        Assert.Equal([4, 2], counters.Select(c => c.Id));
    }

    [Fact]
    public async Task GetDraftCounters_ReportsAnUnknownRole_ListingTheKnownOnes_AlongWithOtherErrors()
    {
        GiveRoles();

        var result = await CreateService().GetDraftCountersAsync([1, 99], [], "Jungler", 5, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal("Unknown role: Jungler. Known roles: Carry, Disabler, Support.", Assert.Single(result.Errors["role"]));
        Assert.Equal("Unknown hero IDs: 99.", Assert.Single(result.Errors["against"]));
    }
}
