using AniMan.Infrastructure.Jikan;
using AniMan.Tests.TestDoubles;
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
}
