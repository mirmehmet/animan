using Microsoft.Extensions.Logging;

namespace AniTrack.Infrastructure.Jikan;

public sealed class JikanRateLimiter(ILogger<JikanRateLimiter> logger)
{
    private readonly SemaphoreSlim _perSecond = new(3, 3);
    private readonly SemaphoreSlim _perMinute = new(60, 60);

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> request, CancellationToken ct = default)
    {
        await _perMinute.WaitAsync(ct);
        await _perSecond.WaitAsync(ct);

        logger.LogDebug("Jikan slot acquired — per-second: {S}, per-minute: {M}",
            _perSecond.CurrentCount, _perMinute.CurrentCount);

        try
        {
            return await request();
        }
        finally
        {
            _ = Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None)
                .ContinueWith(_ => _perSecond.Release(), TaskScheduler.Default);

            _ = Task.Delay(TimeSpan.FromSeconds(60), CancellationToken.None)
                .ContinueWith(_ => _perMinute.Release(), TaskScheduler.Default);
        }
    }
}
