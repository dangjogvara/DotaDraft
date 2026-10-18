using DotaDraft.Api.Data;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Models;
using DotaDraft.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Tests.Services;

public class HeroIngestionServiceTests
{
    private readonly FakeOpenDotaClient _client = new();
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private HeroIngestionService CreateService(
        int matchupRequestDelayMs = 0, MatchupSource source = MatchupSource.Pro, int windowCount = 3, int? minAvgRankTier = null) => new(
        _client,
        _db,
        NullLogger<HeroIngestionService>.Instance,
        Options.Create(new OpenDotaOptions { SteamCdnBaseUrl = "https://cdn.test", MatchupRequestDelayMs = matchupRequestDelayMs }),
        Options.Create(new MatchupOptions
        {
            Source = source, WindowSize = 100, WindowCount = windowCount, MinAvgRankTier = minAvgRankTier
        }));

    private HeroIngestionService CreatePublicService(int windowCount = 3, int? minAvgRankTier = null) =>
        CreateService(source: MatchupSource.Public, windowCount: windowCount, minAvgRankTier: minAvgRankTier);

    private static OpenDotaHeroDto HeroDto(int id, string? img = null) =>
        new(id, $"npc_dota_hero_{id}", $"Hero {id}", "str", ["Carry"], img);

    private void AddHeroes(params int[] ids)
    {
        _db.Heroes.AddRange(ids.Select(id => new Hero
        {
            Id = id, Name = $"npc_dota_hero_{id}", LocalizedName = $"Hero {id}", PrimaryAttr = "str"
        }));
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task SeedHeroes_SavesHeroesAndPrefixesImageWithCdn()
    {
        // OpenDota sends image paths with a trailing empty query string
        _client.Heroes.AddRange([HeroDto(1, "/apps/dota2/images/1.png?"), HeroDto(2)]);

        var result = await CreateService().SeedHeroesAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Saved);
        var heroes = await _db.Heroes.OrderBy(h => h.Id).ToListAsync();
        Assert.Equal("https://cdn.test/apps/dota2/images/1.png", heroes[0].ImgUrl);
        Assert.Null(heroes[1].ImgUrl);
    }

    [Fact]
    public async Task SeedHeroes_UpdatesExistingHeroes()
    {
        AddHeroes(1);
        _client.Heroes.AddRange([HeroDto(1, "/new.png") with { LocalizedName = "Renamed" }, HeroDto(2)]);

        var result = await CreateService().SeedHeroesAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Saved);
        var hero = await _db.Heroes.AsNoTracking().SingleAsync(h => h.Id == 1);
        Assert.Equal("Renamed", hero.LocalizedName);
        Assert.Equal("https://cdn.test/new.png", hero.ImgUrl);
        Assert.Equal(2, await _db.Heroes.CountAsync());
    }

    [Fact]
    public async Task SeedHeroes_ReseedWithUnchangedData_WritesNothing()
    {
        _client.Heroes.AddRange([HeroDto(1, "/1.png"), HeroDto(2)]);
        await CreateService().SeedHeroesAsync();
        _db.ChangeTracker.Clear();

        var result = await CreateService().SeedHeroesAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Saved);
    }

    [Fact]
    public async Task SeedHeroes_ReturnsUpstreamFailure_WhenClientFails()
    {
        _client.FailHeroStats = true;

        var result = await CreateService().SeedHeroesAsync();

        Assert.Equal(SeedFailure.Upstream, result.Failure);
    }

    [Fact]
    public async Task SeedHeroes_ReturnsUpstreamFailure_WhenNoHeroesReturned()
    {
        var result = await CreateService().SeedHeroesAsync();

        Assert.Equal(SeedFailure.Upstream, result.Failure);
    }

    [Fact]
    public async Task SeedMatchups_ReturnsMissingPrerequisite_WhenNoHeroes()
    {
        var result = await CreateService().SeedMatchupsAsync();

        Assert.Equal(SeedFailure.MissingPrerequisite, result.Failure);
    }

    [Fact]
    public async Task SeedProMatchups_SavesWinRate_AndSkipsUnknownOpponents()
    {
        AddHeroes(1, 2);
        _client.Matchups[1] = [new(2, 200, 110), new(99, 50, 25)];
        _client.Matchups[2] = [new(1, 0, 0)];

        var result = await CreateService().SeedMatchupsAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Saved);
        var matchup = await _db.HeroMatchups.SingleAsync(m => m.HeroId == 1);
        Assert.Equal(2, matchup.AgainstHeroId);
        Assert.Equal(55.00m, matchup.WinRate);
        Assert.Equal(0m, (await _db.HeroMatchups.SingleAsync(m => m.HeroId == 2)).WinRate);
    }

    [Fact]
    public async Task SeedProMatchups_UpdatesExistingPairs()
    {
        AddHeroes(1, 2);
        _db.HeroMatchups.Add(new HeroMatchup { HeroId = 1, AgainstHeroId = 2, GamesPlayed = 10, Wins = 5, WinRate = 50m });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _client.Matchups[1] = [new(2, 200, 110)];

        var result = await CreateService().SeedMatchupsAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.Saved);
        var matchup = await _db.HeroMatchups.AsNoTracking().SingleAsync();
        Assert.Equal(200, matchup.GamesPlayed);
        Assert.Equal(110, matchup.Wins);
        Assert.Equal(55.00m, matchup.WinRate);
    }

    [Fact]
    public async Task SeedProMatchups_ReseedWithUnchangedData_WritesNothing()
    {
        AddHeroes(1, 2);
        _client.Matchups[1] = [new(2, 200, 110)];
        _client.Matchups[2] = [new(1, 200, 90)];
        await CreateService().SeedMatchupsAsync();

        var result = await CreateService().SeedMatchupsAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Saved);
        Assert.Equal(2, await _db.HeroMatchups.CountAsync());
    }

    [Fact]
    public async Task SeedProMatchups_ContinuesPastUpstreamFailures_AndReportsFailedHeroes()
    {
        AddHeroes(1, 2, 3);
        _client.FailingHeroIds.Add(2);
        _client.Matchups[1] = [new(3, 10, 5)];
        _client.Matchups[3] = [new(1, 10, 5)];

        var result = await CreateService().SeedMatchupsAsync();

        Assert.Equal(SeedFailure.Upstream, result.Failure);
        Assert.Equal(2, result.Saved);
        Assert.Equal([2], result.FailedHeroIds);
    }

    [Fact]
    public async Task SeedProMatchups_KeepsRateLimitDelay_AfterFailedAndEmptyResponses()
    {
        // Hero 1 fails, hero 2 returns nothing: both used to skip the delay before the next request
        AddHeroes(1, 2, 3);
        _client.FailingHeroIds.Add(1);
        const int delayMs = 100;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await CreateService(delayMs).SeedMatchupsAsync();
        stopwatch.Stop();

        // One delay between each of the three requests. Allow some slack, since timers can fire
        // a little early; without the delays the run takes ~1 ms
        Assert.True(stopwatch.ElapsedMilliseconds >= 2 * delayMs - 20, $"Elapsed {stopwatch.ElapsedMilliseconds} ms");
    }

    private static OpenDotaPublicMatchupDto Pair(int heroId, int againstHeroId, int games, int wins) =>
        new(heroId, againstHeroId, games, wins);

    private void AddMatchup(int heroId, int againstHeroId, int games, int wins)
    {
        _db.HeroMatchups.Add(new HeroMatchup
        {
            HeroId = heroId, AgainstHeroId = againstHeroId, GamesPlayed = games, Wins = wins, WinRate = 50m
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task SeedPublicMatchups_SumsWindowsBackFromLatestMatch_AndSkipsUnknownHeroes()
    {
        AddHeroes(1, 2);
        _client.LatestPublicMatchId = 1000;
        _client.PublicMatchups[1000] = [Pair(1, 2, 100, 60), Pair(2, 1, 100, 40), Pair(1, 99, 50, 25)];
        _client.PublicMatchups[900] = [Pair(1, 2, 100, 50), Pair(2, 1, 100, 50)];

        var result = await CreatePublicService().SeedMatchupsAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Saved);
        Assert.Equal([(901L, 1000L, (int?)null), (801, 900, null), (701, 800, null)], _client.PublicMatchupRequests);
        var matchup = await _db.HeroMatchups.SingleAsync(m => m.HeroId == 1);
        Assert.Equal(2, matchup.AgainstHeroId);
        Assert.Equal(200, matchup.GamesPlayed);
        Assert.Equal(110, matchup.Wins);
        Assert.Equal(55.00m, matchup.WinRate);
        Assert.Equal(2, await _db.HeroMatchups.CountAsync());
    }

    [Fact]
    public async Task SeedPublicMatchups_PassesRankFilter()
    {
        AddHeroes(1, 2);
        _client.PublicMatchups[_client.LatestPublicMatchId] = [Pair(1, 2, 10, 5)];

        await CreatePublicService(windowCount: 1, minAvgRankTier: 70).SeedMatchupsAsync();

        Assert.Equal(70, Assert.Single(_client.PublicMatchupRequests).MinAvgRankTier);
    }

    [Fact]
    public async Task SeedPublicMatchups_ReplacesExistingRows_AndRemovesPairsNotSeen()
    {
        AddHeroes(1, 2, 3);
        AddMatchup(1, 2, 10, 5);
        AddMatchup(1, 3, 10, 5);
        _client.PublicMatchups[_client.LatestPublicMatchId] = [Pair(1, 2, 1000, 600)];

        var result = await CreatePublicService(windowCount: 1).SeedMatchupsAsync();

        Assert.True(result.Succeeded);
        var matchup = await _db.HeroMatchups.AsNoTracking().SingleAsync();
        Assert.Equal((1, 2, 1000, 600, 60.00m),
            (matchup.HeroId, matchup.AgainstHeroId, matchup.GamesPlayed, matchup.Wins, matchup.WinRate));
    }

    [Fact]
    public async Task SeedPublicMatchups_ReseedWithUnchangedData_WritesNothing()
    {
        AddHeroes(1, 2);
        _client.PublicMatchups[_client.LatestPublicMatchId] = [Pair(1, 2, 10, 5), Pair(2, 1, 10, 5)];
        await CreatePublicService(windowCount: 1).SeedMatchupsAsync();

        var result = await CreatePublicService(windowCount: 1).SeedMatchupsAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Saved);
        Assert.Equal(2, await _db.HeroMatchups.CountAsync());
    }

    [Fact]
    public async Task SeedPublicMatchups_SavesAndReportsFailure_WhenSomeWindowsFail()
    {
        AddHeroes(1, 2);
        _client.LatestPublicMatchId = 1000;
        _client.FailingWindows.Add(900);
        _client.PublicMatchups[1000] = [Pair(1, 2, 100, 60)];

        var result = await CreatePublicService().SeedMatchupsAsync();

        Assert.Equal(SeedFailure.Upstream, result.Failure);
        Assert.Equal(1, result.FailedWindows);
        Assert.Equal(1, result.Saved);
        Assert.Equal(100, (await _db.HeroMatchups.SingleAsync()).GamesPlayed);
    }

    [Fact]
    public async Task SeedPublicMatchups_KeepsExistingData_WhenMostWindowsFail()
    {
        AddHeroes(1, 2);
        AddMatchup(1, 2, 10, 5);
        _client.LatestPublicMatchId = 1000;
        _client.FailingWindows.UnionWith([1000, 900]);
        _client.PublicMatchups[800] = [Pair(1, 2, 100, 60)];

        var result = await CreatePublicService().SeedMatchupsAsync();

        Assert.Equal(SeedFailure.Upstream, result.Failure);
        Assert.Equal(2, result.FailedWindows);
        Assert.Equal(0, result.Saved);
        Assert.Equal(10, (await _db.HeroMatchups.AsNoTracking().SingleAsync()).GamesPlayed);
    }

    [Fact]
    public async Task SeedPublicMatchups_ReturnsUpstreamFailure_WhenLatestMatchCannotBeFound()
    {
        AddHeroes(1, 2);
        _client.FailLatestPublicMatchId = true;

        var result = await CreatePublicService().SeedMatchupsAsync();

        Assert.Equal(SeedFailure.Upstream, result.Failure);
        Assert.Empty(_client.PublicMatchupRequests);
    }
}
