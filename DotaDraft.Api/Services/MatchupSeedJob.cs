using System.Threading.Channels;

namespace DotaDraft.Api.Services;

// Singleton handing matchup seed requests from the admin endpoint to MatchupSeedWorker,
// and tracking the latest run so clients can poll its status
public class MatchupSeedJob(TimeProvider time)
{
    // Capacity 1 is enough: TryEnqueue refuses while a run is queued or in progress
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(1);
    private readonly Lock _lock = new();
    private SeedJobStatus _status = new(SeedJobState.Idle);

    public SeedJobStatus Status
    {
        get { lock (_lock) return _status; }
    }

    public ChannelReader<bool> Requests => _requests.Reader;

    /// <returns>False if a run is already queued or running.</returns>
    public bool TryEnqueue()
    {
        lock (_lock)
        {
            if (_status.State is SeedJobState.Queued or SeedJobState.Running)
                return false;

            _status = new SeedJobStatus(SeedJobState.Queued, QueuedAt: time.GetUtcNow());
            _requests.Writer.TryWrite(true);
            return true;
        }
    }

    public void MarkRunning()
    {
        lock (_lock)
            _status = _status with { State = SeedJobState.Running, StartedAt = time.GetUtcNow() };
    }

    public void MarkFinished(SeedResult result)
    {
        lock (_lock)
            _status = _status with
            {
                State = result.Succeeded ? SeedJobState.Succeeded : SeedJobState.Failed,
                FinishedAt = time.GetUtcNow(),
                Result = result,
                Error = result.Error
            };
    }

    public void MarkFailed(string error, SeedResult? result = null)
    {
        lock (_lock)
            _status = _status with
            {
                State = SeedJobState.Failed,
                FinishedAt = time.GetUtcNow(),
                Result = result,
                Error = error
            };
    }
}
