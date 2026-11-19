using DotaDraft.Api.Data;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Models;
using DotaDraft.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Tests.Services;

public class HeroIngestionServiceTests : IAsyncLifetime
{
    private readonly FakeOpenDotaClient _client = new();
    private AppDbContext _db = null!;

    public async Task InitializeAsync() => _db = await TestDatabase.CreateContextAsync();

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private HeroIngestionService CreateService(int windowCount = 3, int? minAvgRankTier = null) =>
        new(_client, _db, NullLogger<HeroIngestionService>.Instance,
            Options.Create(new OpenDotaOptions { SteamCdnBaseUrl = "https://cdn.test" }),
            Options.Create(new MatchupOptions { WindowSize = 100, WindowCount = windowCount, MinAvgRankTier = minAvgRankTier }));

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

        var saved = await CreateService().SeedHeroesAsync();

        Assert.Equal(2, saved);
        var heroes = await _db.Heroes.OrderBy(h => h.Id).ToListAsync();
        Assert.Equal("https://cdn.test/apps/dota2/images/1.png", heroes[0].ImgUrl);
        Assert.Null(heroes[1].ImgUrl);
    }

    [Fact]
    public async Task SeedHeroes_UpdatesExistingHeroes()
    {
        AddHeroes(1);
        _client.Heroes.AddRange([HeroDto(1, "/new.png") with { LocalizedName = "Renamed" }, HeroDto(2)]);

        var saved = await CreateService().SeedHeroesAsync();

        Assert.Equal(2, saved);
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

        var saved = await CreateService().SeedHeroesAsync();

        Assert.Equal(0, saved);
    }

    [Fact]
    public async Task SeedHeroes_Throws_WhenClientFails()
    {
        _client.FailHeroStats = true;

        await Assert.ThrowsAsync<OpenDotaException>(() => CreateService().SeedHeroesAsync());
    }

    [Fact]
    public async Task SeedHeroes_Throws_WhenNoHeroesReturned()
    {
        var ex = await Assert.ThrowsAsync<OpenDotaException>(() => CreateService().SeedHeroesAsync());

        Assert.Equal("OpenDota returned no heroes.", ex.Message);
    }

    [Fact]
    public async Task SeedMatchups_Throws_WhenNoHeroes()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateService().SeedMatchupsAsync());

        Assert.Contains("Heroes table is empty", ex.Message);
        Assert.Empty(_client.PublicMatchupRequests);
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
    public async Task SeedMatchups_SumsWindowsBackFromLatestMatch_AndSkipsUnknownHeroes()
    {
        AddHeroes(1, 2);
        _client.LatestPublicMatchId = 1000;
        _client.PublicMatchups[1000] = [Pair(1, 2, 100, 60), Pair(2, 1, 100, 40), Pair(1, 99, 50, 25)];
        _client.PublicMatchups[900] = [Pair(1, 2, 100, 50), Pair(2, 1, 100, 50)];

        var saved = await CreateService().SeedMatchupsAsync();

        Assert.Equal(2, saved);
        Assert.Equal([(901L, 1000L, (int?)null), (801, 900, null), (701, 800, null)], _client.PublicMatchupRequests);
        var matchup = await _db.HeroMatchups.SingleAsync(m => m.HeroId == 1);
        Assert.Equal(2, matchup.AgainstHeroId);
        Assert.Equal(200, matchup.GamesPlayed);
        Assert.Equal(110, matchup.Wins);
        Assert.Equal(55.00m, matchup.WinRate);
        Assert.Equal(2, await _db.HeroMatchups.CountAsync());
    }

    [Fact]
    public async Task SeedMatchups_PassesRankFilter()
    {
        AddHeroes(1, 2);
        _client.PublicMatchups[_client.LatestPublicMatchId] = [Pair(1, 2, 10, 5)];

        await CreateService(windowCount: 1, minAvgRankTier: 70).SeedMatchupsAsync();

        Assert.Equal(70, Assert.Single(_client.PublicMatchupRequests).MinAvgRankTier);
    }

    [Fact]
    public async Task SeedMatchups_ReplacesExistingRows_AndRemovesPairsNotSeen()
    {
        AddHeroes(1, 2, 3);
        AddMatchup(1, 2, 10, 5);
        AddMatchup(1, 3, 10, 5);
        _client.PublicMatchups[_client.LatestPublicMatchId] = [Pair(1, 2, 1000, 600)];

        var saved = await CreateService(windowCount: 1).SeedMatchupsAsync();

        // One row updated, one removed
        Assert.Equal(2, saved);
        var matchup = await _db.HeroMatchups.AsNoTracking().SingleAsync();
        Assert.Equal((1, 2, 1000, 600, 60.00m),
            (matchup.HeroId, matchup.AgainstHeroId, matchup.GamesPlayed, matchup.Wins, matchup.WinRate));
    }

    [Fact]
    public async Task SeedMatchups_ReseedWithUnchangedData_WritesNothing()
    {
        AddHeroes(1, 2);
        _client.PublicMatchups[_client.LatestPublicMatchId] = [Pair(1, 2, 10, 5), Pair(2, 1, 10, 5)];
        await CreateService(windowCount: 1).SeedMatchupsAsync();

        var saved = await CreateService(windowCount: 1).SeedMatchupsAsync();

        Assert.Equal(0, saved);
        Assert.Equal(2, await _db.HeroMatchups.CountAsync());
    }

    [Fact]
    public async Task SeedMatchups_SavesTheRest_WhenSomeWindowsFail()
    {
        AddHeroes(1, 2);
        _client.LatestPublicMatchId = 1000;
        _client.FailingWindows.Add(900);
        _client.PublicMatchups[1000] = [Pair(1, 2, 100, 60)];

        var saved = await CreateService().SeedMatchupsAsync();

        Assert.Equal(1, saved);
        Assert.Equal(100, (await _db.HeroMatchups.SingleAsync()).GamesPlayed);
    }

    [Fact]
    public async Task SeedMatchups_KeepsExistingData_WhenMostWindowsFail()
    {
        AddHeroes(1, 2);
        AddMatchup(1, 2, 10, 5);
        _client.LatestPublicMatchId = 1000;
        _client.FailingWindows.UnionWith([1000, 900]);
        _client.PublicMatchups[800] = [Pair(1, 2, 100, 60)];

        var ex = await Assert.ThrowsAsync<OpenDotaException>(() => CreateService().SeedMatchupsAsync());

        Assert.Equal("Only 1 of 3 match windows could be fetched; existing matchups were kept.", ex.Message);
        Assert.Equal(10, (await _db.HeroMatchups.AsNoTracking().SingleAsync()).GamesPlayed);
    }

    private void SetBaseline(int heroId, double baselineWinRate)
    {
        _db.Heroes.Single(h => h.Id == heroId).BaselineWinRate = baselineWinRate;
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private async Task<Dictionary<int, double>> StoredBaselines() =>
        await _db.Heroes.AsNoTracking().ToDictionaryAsync(h => h.Id, h => h.BaselineWinRate);

    [Fact]
    public async Task SeedMatchups_StoresEachHerosBaseline_FromAllItsPairsInAllWindows()
    {
        AddHeroes(1, 2, 3, 4);
        SetBaseline(4, 0.7); // from an older seed; hero 4 has no games in this one
        _client.LatestPublicMatchId = 1000;
        _client.PublicMatchups[1000] = [Pair(1, 2, 100, 60), Pair(2, 1, 100, 40), Pair(1, 3, 50, 10)];
        _client.PublicMatchups[900] = [Pair(1, 2, 50, 20), Pair(2, 3, 50, 35)];

        await CreateService().SeedMatchupsAsync();

        var baselines = await StoredBaselines();
        Assert.Equal(90 / 200.0, baselines[1], precision: 10);   // 60 + 10 + 20 wins in 100 + 50 + 50 games
        Assert.Equal(75 / 150.0, baselines[2], precision: 10);   // 40 + 35 wins in 100 + 50 games
        Assert.Equal(Hero.DefaultBaselineWinRate, baselines[3]); // only ever an enemy, so no games of its own
        Assert.Equal(Hero.DefaultBaselineWinRate, baselines[4]); // the old baseline doesn't linger
    }

    [Fact]
    public async Task SeedMatchups_KeepsExistingBaselines_WhenMostWindowsFail()
    {
        AddHeroes(1, 2);
        AddMatchup(1, 2, 10, 7);
        SetBaseline(1, 0.7);
        _client.LatestPublicMatchId = 1000;
        _client.FailingWindows.UnionWith([1000, 900]);
        _client.PublicMatchups[800] = [Pair(1, 2, 100, 20)];

        await Assert.ThrowsAsync<OpenDotaException>(() => CreateService().SeedMatchupsAsync());

        Assert.Equal(0.7, (await StoredBaselines())[1]);
    }

    [Fact]
    public async Task SeedMatchups_CountsOnlyMatchupRows_NotHeroesWhoseBaselineChanged()
    {
        AddHeroes(1, 2);
        _client.PublicMatchups[_client.LatestPublicMatchId] = [Pair(1, 2, 10, 8), Pair(2, 1, 10, 2)];

        var saved = await CreateService(windowCount: 1).SeedMatchupsAsync();

        // Two matchups added; both heroes' baselines changed too, but aren't counted
        Assert.Equal(2, saved);
        Assert.Equal([0.8, 0.2], (await StoredBaselines()).OrderBy(b => b.Key).Select(b => b.Value));
    }

    [Fact]
    public async Task SeedMatchups_Throws_WhenLatestMatchCannotBeFound()
    {
        AddHeroes(1, 2);
        _client.FailLatestPublicMatchId = true;

        await Assert.ThrowsAsync<OpenDotaException>(() => CreateService().SeedMatchupsAsync());

        Assert.Empty(_client.PublicMatchupRequests);
    }
}
