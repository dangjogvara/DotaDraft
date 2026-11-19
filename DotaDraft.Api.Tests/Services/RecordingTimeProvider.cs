using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;

namespace DotaDraft.Api.Tests.Services;

// A FakeTimeProvider that records the due time of every timer, such as the one behind Task.Delay(wait, time).
// Tests can then wait until the worker is waiting, and see how long it means to wait, before moving time on
public class RecordingTimeProvider : FakeTimeProvider
{
    private readonly ConcurrentQueue<TimeSpan> _dueTimes = new();

    public IReadOnlyCollection<TimeSpan> DueTimes => _dueTimes;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        _dueTimes.Enqueue(dueTime);
        return base.CreateTimer(callback, state, dueTime, period);
    }
}
