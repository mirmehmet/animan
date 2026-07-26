using AniMan.Infrastructure.Http;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.AniList;

/// <summary>
/// AniList advertises its budget in the <c>X-RateLimit-Limit</c> response header. It has
/// been serving a degraded <b>30 requests/minute</b> (down from the documented 90), so the
/// lower figure is what we hold ourselves to. AniList is only reached when Jikan fails, so
/// this ceiling is generous in practice.
/// </summary>
public sealed class AniListRateLimiter : IDisposable
{
    private readonly SlidingWindowRateLimiter _limiter;

    public AniListRateLimiter(ILogger<AniListRateLimiter> logger, TimeProvider? timeProvider = null) =>
        _limiter = new SlidingWindowRateLimiter("AniList", logger, timeProvider,
            (30, TimeSpan.FromMinutes(1)));

    public Task<T> ExecuteAsync<T>(Func<Task<T>> request, CancellationToken ct = default) =>
        _limiter.ExecuteAsync(request, ct);

    public void Dispose() => _limiter.Dispose();
}
