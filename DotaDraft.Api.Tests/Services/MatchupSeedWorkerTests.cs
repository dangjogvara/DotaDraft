using DotaDraft.Api.Data;
using DotaDraft.Api.Integrations.OpenDota;
using DotaDraft.Api.Models;
using DotaDraft.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DotaDraft.Api.Tests.Services;

public class MatchupSeedWorkerTests : IAsyncLifetime
{
    private readonly FakeOpenDotaClient _client = new();
    private readonly ServiceProvider _services;
    private readonly MatchupSeedJob _job;
    private readonly MatchupSeedWorker _worker;

    public MatchupSeedWorkerTests()
    {
        var dbName = Guid.NewGuid().ToString();
        _services = new ServiceCollection()
            .AddLogging()
            .AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName))
            .Configure<OpenDotaOptions>(o => o.MatchupRequestDelayMs = 0)
            .Configure<MatchupOptions>(o => o.Source = MatchupSource.Pro)
            .AddSingleton<IOpenDotaClient>(_client)
            .AddSingleton(TimeProvider.System)
            .AddHeroIngestion()
            .AddSingleton<MatchupSeedJob>()
            .AddSingleton<MatchupSeedWorker>()
            .BuildServiceProvider();

        _job = _services.GetRequiredService<MatchupSeedJob>();
        _worker = _services.GetRequiredService<MatchupSeedWorker>();
    }

    public Task InitializeAsync() => _worker.StartAsync(CancellationToken.None);

    public async Task DisposeAsync()
    {
        await _worker.StopAsync(CancellationToken.None);
        await _services.DisposeAsync();
    }

    private void AddHeroes(params int[] ids)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Heroes.AddRange(ids.Select(id => new Hero
        {
            Id = id, Name = $"npc_dota_hero_{id}", LocalizedName = $"Hero {id}", PrimaryAttr = "str"
        }));
        db.SaveChanges();
    }

    private async Task<SeedJobStatus> WaitForFinishAsync()
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (_job.Status.State is SeedJobState.Queued or SeedJobState.Running)
        {
            Assert.True(DateTime.UtcNow < timeout, "Seed job did not finish in time.");
            await Task.Delay(10);
        }
        return _job.Status;
    }

    [Fact]
    public async Task QueuedRequest_RunsSeedingAndRecordsResult()
    {
        AddHeroes(1, 2);
        _client.Matchups[1] = [new(2, 10, 5)];
        _client.Matchups[2] = [new(1, 10, 5)];

        Assert.True(_job.TryEnqueue());
        var status = await WaitForFinishAsync();

        Assert.Equal(SeedJobState.Succeeded, status.State);
        Assert.Equal(2, status.Result?.Saved);
        Assert.Null(status.Error);
    }

    [Fact]
    public async Task FailedSeed_IsReportedAsFailed_AndWorkerAcceptsNextRequest()
    {
        // No heroes yet: the first run fails on the missing prerequisite
        Assert.True(_job.TryEnqueue());
        var failed = await WaitForFinishAsync();
        Assert.Equal(SeedJobState.Failed, failed.State);
        Assert.Equal(SeedFailure.MissingPrerequisite, failed.Result?.Failure);

        AddHeroes(1, 2);
        _client.Matchups[1] = [new(2, 10, 5)];
        Assert.True(_job.TryEnqueue());
        var succeeded = await WaitForFinishAsync();
        Assert.Equal(SeedJobState.Succeeded, succeeded.State);
    }
}
