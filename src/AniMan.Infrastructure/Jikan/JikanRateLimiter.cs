using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.Jikan;

public sealed class JikanRateLimiter(
    ILogger<JikanRateLimiter> logger,
    TimeProvider? timeProvider = null) : IDisposable
{
    private readonly SemaphoreSlim _perSecond = new(3, 3);
    private readonly SemaphoreSlim _perMinute = new(60, 60);
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private volatile bool _disposed;

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> request, CancellationToken ct = default)
    {
        await _perMinute.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _perSecond.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Cancelled while waiting for the per-second slot — release the
            // per-minute permit we already hold so it isn't leaked.
            _perMinute.Release();
            throw;
        }

        logger.LogDebug("Jikan slot acquired — per-second: {S}, per-minute: {M}",
            _perSecond.CurrentCount, _perMinute.CurrentCount);

        try
        {
            return await request().ConfigureAwait(false);
        }
        finally
        {
            _ = ReleaseAfterAsync(_perSecond, TimeSpan.FromSeconds(1));
            _ = ReleaseAfterAsync(_perMinute, TimeSpan.FromSeconds(60));
        }
    }

    private async Task ReleaseAfterAsync(SemaphoreSlim semaphore, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _time).ConfigureAwait(false);
            if (!_disposed)
                semaphore.Release();
        }
        catch (ObjectDisposedException)
        {
            // Shutdown disposed the semaphore while a release was pending — benign.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _perSecond.Dispose();
        _perMinute.Dispose();
    }
}
