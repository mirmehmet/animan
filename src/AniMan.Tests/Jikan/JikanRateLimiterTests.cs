using AniMan.Infrastructure.Jikan;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniMan.Tests.Jikan;

public class JikanRateLimiterTests
{
    private static readonly TimeSpan RealTimeGuard = TimeSpan.FromSeconds(5);

    private static JikanRateLimiter Create(TimeProvider time) =>
        new(NullLogger<JikanRateLimiter>.Instance, time);

    [Fact]
    public async Task ThreeConcurrentCalls_CompleteImmediately()
    {
        var time = new FakeTimeProvider();
        using var limiter = Create(time);

        var tasks = Enumerable.Range(0, 3).Select(_ =>
            limiter.ExecuteAsync(() => Task.FromResult(1))).ToList();

        // All three per-second slots are free — no virtual time needs to pass.
        var all = Task.WhenAll(tasks);
        (await Task.WhenAny(all, Task.Delay(RealTimeGuard))).Should().Be(all);
    }

    [Fact]
    public async Task FourthCall_WaitsUntilSlotIsReleased()
    {
        var time = new FakeTimeProvider();
        using var limiter = Create(time);

        var first3 = Enumerable.Range(0, 3).Select(_ =>
            limiter.ExecuteAsync(() => Task.FromResult(1))).ToList();
        await Task.WhenAll(first3);

        var fourth = limiter.ExecuteAsync(() => Task.FromResult(1));
        fourth.IsCompleted.Should().BeFalse("all three per-second slots are taken");

        // One virtual second later the first slot is released.
        time.Advance(TimeSpan.FromSeconds(1));

        (await Task.WhenAny(fourth, Task.Delay(RealTimeGuard))).Should().Be(fourth);
        (await fourth).Should().Be(1);
    }

    [Fact]
    public async Task SustainedLoad_DrainsAsTimeAdvances()
    {
        var time = new FakeTimeProvider();
        using var limiter = Create(time);

        var tasks = Enumerable.Range(0, 10).Select(_ =>
            limiter.ExecuteAsync(() => Task.FromResult(true))).ToList();

        // 10 calls at 3/sec need ~3 virtual seconds to drain.
        var all = Task.WhenAll(tasks);
        var deadline = DateTime.UtcNow + RealTimeGuard;
        while (!all.IsCompleted && DateTime.UtcNow < deadline)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10); // let released waiters' continuations run
        }

        all.IsCompleted.Should().BeTrue("no call may deadlock under sustained load");
        (await all).Should().AllSatisfy(r => r.Should().BeTrue());
    }

    /// <summary>
    /// Minimal manual fake (the Microsoft.Extensions.TimeProvider.Testing package is
    /// not referenced): supports the one-shot timers Task.Delay creates.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
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
}
