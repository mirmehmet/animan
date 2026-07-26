namespace AniMan.Tests.TestDoubles;

/// <summary>
/// Minimal manual <see cref="TimeProvider"/> (the Microsoft.Extensions.TimeProvider.Testing
/// package is not referenced): supports the one-shot timers <see cref="Task.Delay(TimeSpan, TimeProvider)"/>
/// creates, so rate-limiter windows can be drained in virtual time.
/// </summary>
internal sealed class FakeTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<FakeTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_gate) return _now.UtcTicks;
    }

    public override ITimer CreateTimer(
        TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        List<FakeTimer> due;
        lock (_gate)
        {
            _now += by;
            due = _timers.Where(t => t.DueAt is { } d && d <= _now).ToList();
            foreach (var t in due) t.DueAt = null; // one-shot
        }
        foreach (var t in due) t.Fire();
    }

    private sealed class FakeTimer(FakeTimeProvider provider, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? DueAt;

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            var fireNow = false;
            lock (provider._gate)
            {
                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    DueAt = null;
                }
                else if (dueTime <= TimeSpan.Zero)
                {
                    DueAt = null;
                    fireNow = true;
                }
                else
                {
                    DueAt = provider._now + dueTime;
                    if (!provider._timers.Contains(this))
                        provider._timers.Add(this);
                }
            }

            if (fireNow) Fire();
            return true;
        }

        public void Dispose()
        {
            lock (provider._gate) provider._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
