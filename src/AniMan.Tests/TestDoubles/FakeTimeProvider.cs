namespace AniMan.Tests.TestDoubles;

/// <summary>
/// Minimal manual <see cref="TimeProvider"/> (the Microsoft.Extensions.TimeProvider.Testing
/// package is not referenced) for draining rate-limiter windows in virtual time. It follows
/// real timer semantics where tests could notice (AM-057): <see cref="Advance"/> fires due
/// timers one by one in due order with the clock set to each timer's due time, periodic
/// timers re-arm, and a zero due time fires on the thread pool rather than inside the call
/// that set it.
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

    /// <summary>
    /// Moves the clock forward by <paramref name="by"/>, firing every timer that falls due on
    /// the way — including ones a callback sets — at its own due time, earliest first.
    /// </summary>
    public void Advance(TimeSpan by)
    {
        DateTimeOffset target;
        lock (_gate) target = _now + by;

        while (true)
        {
            FakeTimer? next;
            lock (_gate)
            {
                // OrderBy is stable: timers due at the same moment fire in creation order.
                next = _timers.Where(t => t.DueAt <= target).OrderBy(t => t.DueAt).FirstOrDefault();
                if (next is null)
                {
                    _now = target;
                    return;
                }

                _now = next.DueAt!.Value;
                next.DueAt = next.Period > TimeSpan.Zero ? _now + next.Period : null;
            }
            next.Fire();
        }
    }

    private sealed class FakeTimer(FakeTimeProvider provider, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? DueAt;
        public TimeSpan Period;

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            var fireNow = false;
            lock (provider._gate)
            {
                Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
                if (!provider._timers.Contains(this))
                    provider._timers.Add(this);

                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    DueAt = null;
                }
                else if (dueTime <= TimeSpan.Zero)
                {
                    // A real timer with no delay still fires on a pool thread, not in Change.
                    fireNow = true;
                    DueAt = Period > TimeSpan.Zero ? provider._now + Period : null;
                }
                else
                {
                    DueAt = provider._now + dueTime;
                }
            }

            if (fireNow) ThreadPool.QueueUserWorkItem(_ => Fire());
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
