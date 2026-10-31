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
    public async Task FindUnknownHeroIds_ReportsEveryUnknownId_InRequestOrder()
    {
        var unknown = await CreateService().FindUnknownHeroIdsAsync([98, 1, 99], CancellationToken.None);

        Assert.Equal([98, 99], unknown);
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
}
