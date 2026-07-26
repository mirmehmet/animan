using AniMan.Infrastructure.Http;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.Jikan;

/// <summary>
/// Jikan's published limits: 3 requests per second and 60 per minute.
/// </summary>
public sealed class JikanRateLimiter : IDisposable
{
    private readonly SlidingWindowRateLimiter _limiter;

    public JikanRateLimiter(ILogger<JikanRateLimiter> logger, TimeProvider? timeProvider = null) =>
        _limiter = new SlidingWindowRateLimiter("Jikan", logger, timeProvider,
            (60, TimeSpan.FromMinutes(1)),
            (3, TimeSpan.FromSeconds(1)));

    public Task<T> ExecuteAsync<T>(Func<Task<T>> request, CancellationToken ct = default) =>
        _limiter.ExecuteAsync(request, ct);

    public void Dispose() => _limiter.Dispose();
}
