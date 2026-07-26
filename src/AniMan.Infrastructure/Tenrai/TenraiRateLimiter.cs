using AniMan.Infrastructure.Http;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.Tenrai;

/// <summary>
/// Tenrai publishes no rate limits and sends no <c>X-RateLimit-*</c> headers, but it does answer
/// 429 under bursts — one appeared in a 12-request burst during testing, while a paced 1/second
/// run stayed clean. These budgets are therefore a deliberate guess on the safe side, carried
/// over from the limits Jikan documented (3/second, 60/minute).
/// </summary>
public sealed class TenraiRateLimiter : IDisposable
{
    private readonly SlidingWindowRateLimiter _limiter;

    public TenraiRateLimiter(ILogger<TenraiRateLimiter> logger, TimeProvider? timeProvider = null) =>
        _limiter = new SlidingWindowRateLimiter("Tenrai", logger, timeProvider,
            (60, TimeSpan.FromMinutes(1)),
            (3, TimeSpan.FromSeconds(1)));

    public Task<T> ExecuteAsync<T>(Func<Task<T>> request, CancellationToken ct = default) =>
        _limiter.ExecuteAsync(request, ct);

    public void Dispose() => _limiter.Dispose();
}
