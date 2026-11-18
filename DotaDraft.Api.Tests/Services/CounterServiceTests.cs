using DotaDraft.Api.Data;
using DotaDraft.Api.Models;
using DotaDraft.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Tests.Services;

public class CounterServiceTests
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    public CounterServiceTests()
    {
        _db.Heroes.AddRange(Enumerable.Range(1, 4).Select(id => new Hero
        {
            Id = id, Name = $"npc_dota_hero_{id}", LocalizedName = $"Hero {id}", PrimaryAttr = "str"
        }));
        _db.SaveChanges();
    }

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

    [Fact]
    public async Task GetDraftCounters_ReturnsEmpty_ForHeroWithoutMatchups()
    {
        var counters = await CreateService().GetDraftCountersAsync([1], [], 5, CancellationToken.None);

        Assert.Empty(counters);
    }

    [Fact]
    public async Task GetDraftCounters_AppliesPriorGamesFromOptions()
    {
        // 6 of 10 against hero 1 and 4 of 10 against hero 4: a 50% baseline and a raw edge of 10 points
        AddMatchups((2, 1, 6, 10), (2, 4, 4, 10));

        var unsmoothed = await CreateService(priorGames: 0).GetDraftCountersAsync([1], [], 5, CancellationToken.None);
        var smoothed = await CreateService(priorGames: 10).GetDraftCountersAsync([1], [], 5, CancellationToken.None);

        Assert.Equal(10m, Assert.Single(unsmoothed).TotalAdvantage);
        Assert.Equal(5m, Assert.Single(smoothed).TotalAdvantage);
    }

    [Fact]
    public async Task CheckDraft_ReturnsNoErrors_ForKnownHeroesInOneList()
    {
        var errors = await CreateService().CheckDraftAsync([1, 2], [3, 4], CancellationToken.None);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task CheckDraft_ReportsUnknownIds_InRequestOrder_UnderTheListTheyCameFrom()
    {
        var errors = await CreateService().CheckDraftAsync([98, 1, 97], [2, 99], CancellationToken.None);

        Assert.Equal(2, errors.Count);
        Assert.Equal(["Unknown hero IDs: 98, 97."], errors["against"]);
        Assert.Equal(["Unknown hero IDs: 99."], errors["exclude"]);
    }

    [Fact]
    public async Task CheckDraft_ReportsHeroesInBothLists_UnderExclude_WithoutCheckingTheRest()
    {
        // 99 is unknown, but a hero in both lists is reported on its own
        var errors = await CreateService().CheckDraftAsync([1, 2, 99], [2, 1], CancellationToken.None);

        var error = Assert.Single(errors);
        Assert.Equal("exclude", error.Key);
        Assert.Equal(["Hero IDs can't be both enemies and excluded: 1, 2."], error.Value);
    }

    [Fact]
    public async Task CheckTeamMatchup_ReportsHeroesInBothLists_UnderTeam()
    {
        var errors = await CreateService().CheckTeamMatchupAsync([3, 1], [1], CancellationToken.None);

        var error = Assert.Single(errors);
        Assert.Equal("team", error.Key);
        Assert.Equal(["Hero IDs can't be on both teams: 1."], error.Value);
    }

    [Fact]
    public async Task CheckTeamMatchup_ReportsUnknownIds_UnderTheListTheyCameFrom()
    {
        var errors = await CreateService().CheckTeamMatchupAsync([3, 99], [98], CancellationToken.None);

        Assert.Equal(2, errors.Count);
        Assert.Equal(["Unknown hero IDs: 99."], errors["team"]);
        Assert.Equal(["Unknown hero IDs: 98."], errors["against"]);
    }

    [Fact]
    public async Task GetDraftCounters_BreaksAdvantageTies_ByTotalGames()
    {
        // Both have the same advantage over hero 1 with no smoothing; hero 3 has more games behind it
        AddMatchups((2, 1, 6, 10), (2, 4, 4, 10), (3, 1, 60, 100), (3, 4, 40, 100));

        var counters = await CreateService(priorGames: 0).GetDraftCountersAsync([1], [], 5, CancellationToken.None);

        Assert.Equal([3, 2], counters.Select(c => c.Id));
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

        var matchup = await CreateService(priorGames: 0).GetTeamMatchupAsync([3, 4], [1, 2], CancellationToken.None);

        Assert.Equal(0m, matchup.TotalAdvantage);
        Assert.All(matchup.Heroes, h => Assert.All(h.Matchups, m => Assert.Equal(0m, m.Advantage)));
    }

    [Fact]
    public async Task GetDraftCounters_UsesTheEnemysStoredBaseline()
    {
        // Hero 1 wins 55% in general, so hero 2 (50%) is expected to win 45% against it; 55 of 100 is a 10-point edge
        _db.Heroes.Single(h => h.Id == 1).BaselineWinRate = 0.55;
        AddMatchups((2, 1, 55, 100));

        var counter = Assert.Single(await CreateService(priorGames: 0).GetDraftCountersAsync([1], [], 5, CancellationToken.None));

        Assert.Equal(10m, counter.TotalAdvantage);
    }
}
