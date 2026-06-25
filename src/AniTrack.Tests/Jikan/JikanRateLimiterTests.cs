using AniTrack.Infrastructure.Jikan;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniTrack.Tests.Jikan;

public class JikanRateLimiterTests
{
    private static JikanRateLimiter Create() => new(NullLogger<JikanRateLimiter>.Instance);

    [Fact]
    public async Task ThreeConcurrentCalls_CompleteImmediately()
    {
        var limiter = Create();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, 3).Select(_ =>
            limiter.ExecuteAsync(() => Task.FromResult(1)));

        await Task.WhenAll(tasks);

        // All three should complete well under the 1s release window
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(800));
    }

    [Fact]
    public async Task FourthCall_WaitsForSlot()
    {
        var limiter = Create();

        // Consume all 3 per-second slots
        var first3 = Enumerable.Range(0, 3).Select(_ =>
            limiter.ExecuteAsync(() => Task.FromResult(1))).ToList();
        await Task.WhenAll(first3);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 4th call must wait ~1s for a slot to open
        await limiter.ExecuteAsync(() => Task.FromResult(1));

        // Should have waited at least 900ms (the per-second release delay)
        sw.Elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public async Task NoDeadlock_UnderSustainedLoad()
    {
        var limiter = Create();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Fire 10 calls — no deadlock expected within timeout
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            limiter.ExecuteAsync(() => Task.FromResult(true), cts.Token));

        var results = await Task.WhenAll(tasks);
        results.Should().AllSatisfy(r => r.Should().BeTrue());
    }
}
