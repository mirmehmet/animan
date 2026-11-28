using FluentAssertions;

namespace AniMan.Tests.TestDoubles;

/// <summary>The fake clock behaves like real timers where the rate-limiter tests rely on it (AM-057).</summary>
public class FakeTimeProviderTests
{
    [Fact]
    public void Advance_FiresDueTimersInDueOrder_WithTheClockAtEachDueTime()
    {
        var time = new FakeTimeProvider();
        var start = time.GetUtcNow();
        var fired = new List<(string Name, TimeSpan At)>();

        using var late = time.CreateTimer(_ => fired.Add(("late", time.GetUtcNow() - start)), null, TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
        using var early = time.CreateTimer(_ => fired.Add(("early", time.GetUtcNow() - start)), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        using var never = time.CreateTimer(_ => fired.Add(("never", time.GetUtcNow() - start)), null, TimeSpan.FromSeconds(9), Timeout.InfiniteTimeSpan);

        time.Advance(TimeSpan.FromSeconds(5));

        fired.Should().Equal(("early", TimeSpan.FromSeconds(1)), ("late", TimeSpan.FromSeconds(3)));
        (time.GetUtcNow() - start).Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void PeriodicTimer_FiresOncePerPeriod()
    {
        var time = new FakeTimeProvider();
        var count = 0;
        using var timer = time.CreateTimer(_ => count++, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        time.Advance(TimeSpan.FromSeconds(3.5));

        count.Should().Be(3);
    }

    [Fact]
    public void TimerSetByACallback_FiresInTheSameAdvance_WhenItFallsDue()
    {
        var time = new FakeTimeProvider();
        var second = false;
        ITimer? chained = null;
        using var first = time.CreateTimer(
            _ => chained = time.CreateTimer(_ => second = true, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan),
            null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        time.Advance(TimeSpan.FromSeconds(2));

        second.Should().BeTrue();
        chained?.Dispose();
    }

    [Fact]
    public async Task ZeroDueTime_FiresOnThePool_NotInsideCreateTimer()
    {
        var time = new FakeTimeProvider();
        using var created = new SemaphoreSlim(0);
        var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Fired inside CreateTimer, the callback would wait for a release that comes only
        // after CreateTimer returns, and report false.
        using var timer = time.CreateTimer(_ => fired.TrySetResult(created.Wait(TimeSpan.FromSeconds(2))),
            null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        created.Release();

        (await fired.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
    }
}
