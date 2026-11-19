using DotaDraft.Api.Data;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Models;
using DotaDraft.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DotaDraft.Api.Tests.Services;

public class SeedingWorkerTests : IAsyncLifetime
{
    private readonly FakeOpenDotaClient _client = new();
    private readonly RecordingTimeProvider _time = new();
    private ServiceProvider _services = null!;
    private SeedingWorker _worker = null!;

    public async Task InitializeAsync()
    {
        var connectionString = await TestDatabase.CreateAsync();
        _services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString))
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

    // Heroes, plus optionally an older matchup, and optionally a recorded seed that finished seededAgo ago
    private void AddExistingData(bool withMatchup, TimeSpan? seededAgo = null)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Heroes.AddRange(
            new Hero { Id = 1, Name = "npc_dota_hero_1", LocalizedName = "Hero 1", PrimaryAttr = "str" },
            new Hero { Id = 2, Name = "npc_dota_hero_2", LocalizedName = "Hero 2", PrimaryAttr = "agi" });
        if (withMatchup)
            db.HeroMatchups.Add(new HeroMatchup { HeroId = 1, AgainstHeroId = 2, GamesPlayed = 1, Wins = 1, WinRate = 100m });
        if (seededAgo is { } ago)
            db.SeedingStates.Add(new SeedingState { MatchupsSeededAt = _time.GetUtcNow() - ago });
        db.SaveChanges();
    }

    private DateTimeOffset? RecordedSeed()
    {
        using var scope = _services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().SeedingStates.SingleOrDefault()?.MatchupsSeededAt;
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
    public async Task SkipsTheStartupSeed_WhenTheLastSeedIsRecent_ThenSeedsAfterTheInterval()
    {
        AddExistingData(withMatchup: true, seededAgo: TimeSpan.Zero);
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
    public async Task RecordsWhenTheSeedFinished()
    {
        await _worker.StartAsync(CancellationToken.None);

        await WaitUntil(() => RecordedSeed() is not null);
        Assert.Equal(_time.GetUtcNow(), RecordedSeed());
    }

    [Fact]
    public async Task SeedsAtStartup_WhenTheLastSeedIsOlderThanTheInterval()
    {
        AddExistingData(withMatchup: true, seededAgo: TimeSpan.FromHours(25));

        await _worker.StartAsync(CancellationToken.None);

        await WaitUntil(() => _client.HeroStatsRequests == 1 && MatchupCount() == 2);
    }

    [Fact]
    public async Task SeedsAtStartup_WhenMatchupsExistButNoSeedWasRecorded()
    {
        AddExistingData(withMatchup: true);

        await _worker.StartAsync(CancellationToken.None);

        await WaitUntil(() => _client.HeroStatsRequests == 1 && MatchupCount() == 2);
    }

    [Fact]
    public async Task SeedsAtStartup_WhenTheMatchupTableIsEmpty_EvenAfterARecentSeed()
    {
        // As after truncating HeroMatchups to force a fresh seed
        AddExistingData(withMatchup: false, seededAgo: TimeSpan.FromHours(1));

        await _worker.StartAsync(CancellationToken.None);

        await WaitUntil(() => MatchupCount() == 2);
    }

    [Fact]
    public async Task CountsTheIntervalFromTheLastSeed_NotFromStartup()
    {
        // The app restarts 20 hours after the last seed, so the next one is due in 4 hours, not 24
        AddExistingData(withMatchup: true, seededAgo: TimeSpan.FromHours(20));
        await _worker.StartAsync(CancellationToken.None);

        // Time stands still until the worker is waiting, so its wait is exact
        await WaitUntil(() => _time.DueTimes.Count > 0);
        Assert.Equal(TimeSpan.FromHours(4), _time.DueTimes.Single());

        _time.Advance(TimeSpan.FromHours(4));
        await WaitUntil(() => _client.HeroStatsRequests > 0);
    }

    [Fact]
    public async Task RecordsNothing_AndRetriesWithExponentialBackoff_WhenTheSeedFails()
    {
        _client.FailLatestPublicMatchId = true;
        await _worker.StartAsync(CancellationToken.None);

        await WaitUntil(() => _time.DueTimes.Count == 1);
        Assert.Null(RecordedSeed());
        Assert.Equal(TimeSpan.FromMinutes(5), _time.DueTimes.ElementAt(0));

        _time.Advance(TimeSpan.FromMinutes(5));
        await WaitUntil(() => _time.DueTimes.Count == 2);
        Assert.Equal(TimeSpan.FromMinutes(10), _time.DueTimes.ElementAt(1));

        _time.Advance(TimeSpan.FromMinutes(10));
        await WaitUntil(() => _time.DueTimes.Count == 3);
        Assert.Equal(TimeSpan.FromMinutes(20), _time.DueTimes.ElementAt(2));
        Assert.Equal(3, _client.HeroStatsRequests);
    }

    [Fact]
    public async Task CapsFailureBackoff_AtTheInterval()
    {
        _client.FailLatestPublicMatchId = true;
        var worker = new SeedingWorker(
            _services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SeedingOptions { IntervalHours = 1, RetryDelayMinutes = 20 }),
            _time,
            NullLogger<SeedingWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntil(() => _time.DueTimes.Count == 1);
            Assert.Equal(TimeSpan.FromMinutes(20), _time.DueTimes.ElementAt(0));

            _time.Advance(TimeSpan.FromMinutes(20));
            await WaitUntil(() => _time.DueTimes.Count == 2);
            Assert.Equal(TimeSpan.FromMinutes(40), _time.DueTimes.ElementAt(1));

            _time.Advance(TimeSpan.FromMinutes(40));
            await WaitUntil(() => _time.DueTimes.Count == 3);
            Assert.Equal(TimeSpan.FromHours(1), _time.DueTimes.ElementAt(2));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ResetsFailureBackoff_AfterASeedSucceeds()
    {
        _client.FailLatestPublicMatchId = true;
        await _worker.StartAsync(CancellationToken.None);

        // Two failures: 5 min, then 10 min
        await WaitUntil(() => _time.DueTimes.Count == 1);
        _time.Advance(TimeSpan.FromMinutes(5));
        await WaitUntil(() => _time.DueTimes.Count == 2);
        Assert.Equal(TimeSpan.FromMinutes(10), _time.DueTimes.ElementAt(1));

        // Next try succeeds: waits the full interval
        _client.FailLatestPublicMatchId = false;
        _time.Advance(TimeSpan.FromMinutes(10));
        await WaitUntil(() => _time.DueTimes.Count == 3);
        Assert.Equal(TimeSpan.FromHours(24), _time.DueTimes.ElementAt(2));

        // A later failure starts from the base retry delay again, not 20 min
        _client.FailLatestPublicMatchId = true;
        _time.Advance(TimeSpan.FromHours(24));
        await WaitUntil(() => _time.DueTimes.Count == 4);
        Assert.Equal(TimeSpan.FromMinutes(5), _time.DueTimes.ElementAt(3));
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
