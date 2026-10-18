using AniMan.Infrastructure.Http;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.Tenrai;

/// <summary>
/// Tenrai publishes no formal rate limits and sends no <c>X-RateLimit-*</c> headers, but it does answer
/// 429 under rapid bursts. These budgets are a deliberate safe choice (3/second, 60/minute)
/// to ensure stable requests.
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
