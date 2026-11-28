using AniMan.Infrastructure.Tenrai;
using AniMan.Tests.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniMan.Tests.Tenrai;

public class TenraiRateLimiterTests
{
    private static readonly TimeSpan RealTimeGuard = TimeSpan.FromSeconds(5);

    private static TenraiRateLimiter Create(TimeProvider time) =>
        new(NullLogger<TenraiRateLimiter>.Instance, time);

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
    public async Task SustainedLoad_ReleasesThreeCallsPerVirtualSecond()
    {
        // AM-058: pins the per-second budget, not only "no deadlock".
        var time = new FakeTimeProvider();
        using var limiter = Create(time);

        var tasks = Enumerable.Range(0, 10).Select(_ =>
            limiter.ExecuteAsync(() => Task.FromResult(true))).ToList();

        foreach (var expected in new[] { 3, 6, 9, 10 })
        {
            await CompletedCountReachesAsync(tasks, expected);
            await Task.Delay(50); // a fourth call in the same second would complete by now
            tasks.Count(t => t.IsCompleted).Should().Be(expected, "Tenrai allows 3 calls per second");
            time.Advance(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task SixtyFirstCall_WaitsUntilTheFirstCallsLeaveTheMinuteWindow()
    {
        // AM-058: pins the 60-per-minute budget.
        var time = new FakeTimeProvider();
        var start = time.GetUtcNow();
        using var limiter = Create(time);

        var first60 = Enumerable.Range(0, 60).Select(_ =>
            limiter.ExecuteAsync(() => Task.FromResult(true))).ToList();
        var deadline = DateTime.UtcNow + RealTimeGuard;
        while (!first60.All(t => t.IsCompleted) && DateTime.UtcNow < deadline)
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10); // let released waiters' continuations run
        }
        first60.Should().OnlyContain(t => t.IsCompleted, "60 calls fit in one minute");
        (time.GetUtcNow() - start).Should().BeLessThan(TimeSpan.FromSeconds(59));

        var call61 = limiter.ExecuteAsync(() => Task.FromResult(true));
        while (time.GetUtcNow() - start < TimeSpan.FromSeconds(59))
        {
            time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(5);
        }
        await Task.Delay(50);
        call61.IsCompleted.Should().BeFalse("the first calls (at 0 s) still fill the minute window at 59 s");

        time.Advance(TimeSpan.FromSeconds(2));
        (await Task.WhenAny(call61, Task.Delay(RealTimeGuard))).Should().Be(call61);
    }

    private static async Task CompletedCountReachesAsync(IReadOnlyCollection<Task> tasks, int count)
    {
        var deadline = DateTime.UtcNow + RealTimeGuard;
        while (tasks.Count(t => t.IsCompleted) < count && DateTime.UtcNow < deadline)
            await Task.Delay(5);
        tasks.Count(t => t.IsCompleted).Should().BeGreaterThanOrEqualTo(count);
    }
}
