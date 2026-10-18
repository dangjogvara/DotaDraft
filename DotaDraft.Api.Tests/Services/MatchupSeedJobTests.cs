using DotaDraft.Api.Services;

namespace DotaDraft.Api.Tests.Services;

public class MatchupSeedJobTests
{
    private readonly MatchupSeedJob _job = new();

    [Fact]
    public void TryEnqueue_QueuesRequest()
    {
        Assert.True(_job.TryEnqueue());

        Assert.Equal(SeedJobState.Queued, _job.Status.State);
        Assert.NotNull(_job.Status.QueuedAt);
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
        _job.MarkRunning();
        _job.MarkFinished(SeedResult.Failed(SeedFailure.Upstream, "boom", saved: 3));

        var status = _job.Status;
        Assert.Equal(SeedJobState.Failed, status.State);
        Assert.Equal("boom", status.Error);
        Assert.Equal(3, status.Result?.Saved);
        Assert.NotNull(status.StartedAt);
        Assert.NotNull(status.FinishedAt);
    }
}
