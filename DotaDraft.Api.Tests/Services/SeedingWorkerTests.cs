using DotaDraft.Api.Data;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Models;
using DotaDraft.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace DotaDraft.Api.Tests.Services;

public class SeedingWorkerTests : IAsyncLifetime
{
    private readonly FakeOpenDotaClient _client = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ServiceProvider _services;
    private readonly SeedingWorker _worker;

    public SeedingWorkerTests()
    {
        var dbName = Guid.NewGuid().ToString();
        _services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName))
            .Configure<OpenDotaOptions>(o => o.MatchupRequestDelayMs = 0)
            .Configure<MatchupOptions>(o => o.WindowCount = 1)
            .Configure<SeedingOptions>(o => o.IntervalHours = 24)
            .AddSingleton<IOpenDotaClient>(_client)
            .AddSingleton<TimeProvider>(_time)
            .AddScoped<HeroIngestionService>()
            .AddSingleton<SeedingWorker>()
            .BuildServiceProvider();

        _worker = _services.GetRequiredService<SeedingWorker>();

        _client.Heroes.AddRange([
            new OpenDotaHeroDto(1, "npc_dota_hero_1", "Hero 1", "str", [], null),
            new OpenDotaHeroDto(2, "npc_dota_hero_2", "Hero 2", "agi", [], null)
        ]);
        _client.PublicMatchups[_client.LatestPublicMatchId] = [new(1, 2, 10, 6), new(2, 1, 10, 4)];
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _worker.StopAsync(CancellationToken.None);
        await _services.DisposeAsync();
    }

    private int MatchupCount()
    {
        using var scope = _services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().HeroMatchups.Count();
    }

    private void AddExistingData(bool withMatchup)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Heroes.AddRange(
            new Hero { Id = 1, Name = "npc_dota_hero_1", LocalizedName = "Hero 1", PrimaryAttr = "str" },
            new Hero { Id = 2, Name = "npc_dota_hero_2", LocalizedName = "Hero 2", PrimaryAttr = "agi" });
        if (withMatchup)
            db.HeroMatchups.Add(new HeroMatchup { HeroId = 1, AgainstHeroId = 2, GamesPlayed = 1, Wins = 1, WinRate = 100m });
        db.SaveChanges();
    }

    // Waits in real time for the worker, which runs on another thread
    private static async Task WaitUntil(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < timeout, "The worker didn't get there in time.");
            await Task.Delay(10);
        }
    }

    // Moves fake time forward an hour at a time until the condition holds. Stepping (rather than jumping
    // a whole interval) also covers the moment before the worker has created its timer
    private async Task<TimeSpan> AdvanceUntil(Func<bool> condition)
    {
        var start = _time.GetUtcNow();
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < timeout, "The worker didn't run in time.");
            _time.Advance(TimeSpan.FromHours(1));
            await Task.Delay(10);
        }
        return _time.GetUtcNow() - start;
    }

    [Fact]
    public async Task SeedsHeroesAndMatchups_AtStartup_WhenThereAreNoMatchups()
    {
        await _worker.StartAsync(CancellationToken.None);

        await WaitUntil(() => MatchupCount() == 2);
        Assert.Equal(1, _client.HeroStatsRequests);
    }

    [Fact]
    public async Task SkipsTheStartupSeed_WhenMatchupsExist_ThenSeedsAfterTheInterval()
    {
        AddExistingData(withMatchup: true);
        await _worker.StartAsync(CancellationToken.None);

        var elapsed = await AdvanceUntil(() => _client.HeroStatsRequests > 0);

        // Only the scheduled run fetched heroes, and not before the interval had passed
        Assert.Equal(1, _client.HeroStatsRequests);
        Assert.True(elapsed >= TimeSpan.FromHours(24), $"Seeded after {elapsed}");
        await WaitUntil(() => MatchupCount() == 2);
    }

    [Fact]
    public async Task SeedsMatchups_WithTheExistingHeroes_WhenTheHeroRefreshFails()
    {
        AddExistingData(withMatchup: false);
        _client.FailHeroStats = true;

        await _worker.StartAsync(CancellationToken.None);

        await WaitUntil(() => MatchupCount() == 2);
        Assert.Equal(1, _client.HeroStatsRequests);
    }

    [Fact]
    public async Task KeepsRunning_AfterARunThrows()
    {
        _client.HeroStatsBug = new InvalidOperationException("Simulated bug");
        await _worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => _client.HeroStatsRequests == 1);

        _client.HeroStatsBug = null;
        await AdvanceUntil(() => _client.HeroStatsRequests == 2);

        await WaitUntil(() => MatchupCount() == 2);
    }
}
