using AniMan.Infrastructure.AniList;
using AniMan.Infrastructure.Http;
using AniMan.Tests.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniMan.Tests.Http;

/// <summary>
/// The multi-window case is covered by <c>JikanRateLimiterTests</c>; these pin the
/// single-window configuration AniList uses (30 requests per minute).
/// </summary>
public class SlidingWindowRateLimiterTests
{
    private static readonly TimeSpan RealTimeGuard = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task SingleWindow_AllowsExactlyItsPermitsBeforeBlocking()
    {
        var time = new FakeTimeProvider();
        using var limiter = new SlidingWindowRateLimiter(
            "Test", NullLogger.Instance, time, (3, TimeSpan.FromMinutes(1)));

        var firstThree = Enumerable.Range(0, 3)
            .Select(_ => limiter.ExecuteAsync(() => Task.FromResult(1))).ToList();
        await Task.WhenAll(firstThree);

        var fourth = limiter.ExecuteAsync(() => Task.FromResult(1));
        fourth.IsCompleted.Should().BeFalse("the window's three permits are all held");

        // Part-way through the window nothing is released yet.
        time.Advance(TimeSpan.FromSeconds(30));
        fourth.IsCompleted.Should().BeFalse();

        time.Advance(TimeSpan.FromSeconds(30));
        (await Task.WhenAny(fourth, Task.Delay(RealTimeGuard))).Should().Be(fourth);
        (await fourth).Should().Be(1);
    }

    [Fact]
    public async Task AniListRateLimiter_AllowsThirtyCallsPerMinute()
    {
        var time = new FakeTimeProvider();
        using var limiter = new AniListRateLimiter(
            NullLogger<AniListRateLimiter>.Instance, time);

        var thirty = Enumerable.Range(0, 30)
            .Select(_ => limiter.ExecuteAsync(() => Task.FromResult(true))).ToList();

        var all = Task.WhenAll(thirty);
        (await Task.WhenAny(all, Task.Delay(RealTimeGuard))).Should().Be(all,
            "AniList's advertised budget is 30/minute and none of these should queue");

        limiter.ExecuteAsync(() => Task.FromResult(true))
            .IsCompleted.Should().BeFalse("the thirty-first call exceeds the budget");
    }

    [Fact]
    public async Task CancellationWhileQueued_DoesNotLeakThePermitsAlreadyHeld()
    {
        var time = new FakeTimeProvider();
        using var limiter = new SlidingWindowRateLimiter(
            "Test", NullLogger.Instance, time,
            (2, TimeSpan.FromMinutes(1)),
            (1, TimeSpan.FromSeconds(1)));

        // Takes one permit from each window.
        await limiter.ExecuteAsync(() => Task.FromResult(1));

        // Queues on the inner window, then gives up — the outer permit it already took
        // must be handed back or it is lost for the process's lifetime.
        using var cts = new CancellationTokenSource();
        var queued = limiter.ExecuteAsync(() => Task.FromResult(1), cts.Token);
        await cts.CancelAsync();
        await FluentActions.Awaiting(() => queued).Should().ThrowAsync<OperationCanceledException>();

        // One second on, the inner permit returns and a fresh call must get through —
        // which it only can if the outer window still has its second permit.
        time.Advance(TimeSpan.FromSeconds(1));
        var next = limiter.ExecuteAsync(() => Task.FromResult(42));
        (await Task.WhenAny(next, Task.Delay(RealTimeGuard))).Should().Be(next);
        (await next).Should().Be(42);
    }

    [Fact]
    public async Task SustainedLoad_DrainsAsTimeAdvances()
    {
        var time = new FakeTimeProvider();
        using var limiter = new SlidingWindowRateLimiter(
            "Test", NullLogger.Instance, time, (5, TimeSpan.FromMinutes(1)));

        var tasks = Enumerable.Range(0, 12)
            .Select(_ => limiter.ExecuteAsync(() => Task.FromResult(true))).ToList();

        var all = Task.WhenAll(tasks);
        var deadline = DateTime.UtcNow + RealTimeGuard;
        while (!all.IsCompleted && DateTime.UtcNow < deadline)
        {
            time.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(10); // let released waiters' continuations run
        }

        all.IsCompleted.Should().BeTrue("no call may deadlock under sustained load");
    }
}
