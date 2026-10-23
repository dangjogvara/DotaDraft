using DotaDraft.Api.Services;
using Microsoft.Extensions.Time.Testing;

namespace DotaDraft.Api.Tests.Services;

public class MatchupSeedJobTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);
    private readonly MatchupSeedJob _job;

    public MatchupSeedJobTests() => _job = new MatchupSeedJob(_time);

    [Fact]
    public void TryEnqueue_QueuesRequest()
    {
        Assert.True(_job.TryEnqueue());

        Assert.Equal(SeedJobState.Queued, _job.Status.State);
        Assert.Equal(Start, _job.Status.QueuedAt);
        Assert.True(_job.Requests.TryRead(out _));
    }

    [Fact]
    public void TryEnqueue_RefusesWhileQueuedOrRunning()
    {
        _job.TryEnqueue();
        Assert.False(_job.TryEnqueue());

        _job.MarkRunning();
        Assert.False(_job.TryEnqueue());
    }

    [Fact]
    public void TryEnqueue_AllowsNewRunAfterFinish()
    {
        _job.TryEnqueue();
        _job.Requests.TryRead(out _);
        _job.MarkRunning();
        _job.MarkFinished(SeedResult.Success(10));

        Assert.True(_job.TryEnqueue());
        // A new run starts with a fresh status
        Assert.Null(_job.Status.Result);
    }

    [Fact]
    public void MarkFinished_WithFailedResult_SetsFailedStateAndError()
    {
        _job.TryEnqueue();
        _time.Advance(TimeSpan.FromSeconds(1));
        _job.MarkRunning();
        _time.Advance(TimeSpan.FromSeconds(90));
        _job.MarkFinished(SeedResult.Failed(SeedFailure.Upstream, "boom", saved: 3));

        var status = _job.Status;
        Assert.Equal(SeedJobState.Failed, status.State);
        Assert.Equal("boom", status.Error);
        Assert.Equal(3, status.Result?.Saved);
        Assert.Equal(Start, status.QueuedAt);
        Assert.Equal(Start.AddSeconds(1), status.StartedAt);
        Assert.Equal(Start.AddSeconds(91), status.FinishedAt);
    }

    [Fact]
    public void MarkFailed_RecordsFinishTime()
    {
        _job.TryEnqueue();
        _job.MarkRunning();
        _time.Advance(TimeSpan.FromMinutes(2));
        _job.MarkFailed("Cancelled");

        Assert.Equal(SeedJobState.Failed, _job.Status.State);
        Assert.Equal(Start.AddMinutes(2), _job.Status.FinishedAt);
    }
}
