using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.Http;

/// <summary>
/// Enforces one or more independent "N requests per window" budgets. A permit is taken
/// from every window before the request runs and returned one window-length later, so
/// several budgets (e.g. 3/second and 60/minute) can be combined.
/// <para>
/// Windows are acquired in the order supplied — widest first, so a caller never holds a
/// short-lived permit while queuing for a long-lived one.
/// </para>
/// </summary>
public sealed class SlidingWindowRateLimiter : IDisposable
{
    private readonly Window[] _windows;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly string _name;
    private volatile bool _disposed;

    private sealed record Window(SemaphoreSlim Gate, TimeSpan Duration);

    public SlidingWindowRateLimiter(
        string name,
        ILogger logger,
        TimeProvider? timeProvider,
        params (int Permits, TimeSpan Duration)[] windows)
    {
        ArgumentOutOfRangeException.ThrowIfZero(windows.Length);

        _name = name;
        _logger = logger;
        _time = timeProvider ?? TimeProvider.System;
        _windows = [.. windows.Select(w =>
            new Window(new SemaphoreSlim(w.Permits, w.Permits), w.Duration))];
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> request, CancellationToken ct = default)
    {
        var held = 0;
        try
        {
            for (; held < _windows.Length; held++)
                await _windows[held].Gate.WaitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Cancelled part-way through acquisition — hand back the permits already
            // taken, otherwise they stay lost for the lifetime of the process.
            for (var i = 0; i < held; i++)
                _windows[i].Gate.Release();
            throw;
        }

        _logger.LogDebug("{Name} slot acquired — remaining per window: {Remaining}",
            _name, string.Join(", ", _windows.Select(w => w.Gate.CurrentCount)));

        try
        {
            return await request().ConfigureAwait(false);
        }
        finally
        {
            foreach (var window in _windows)
                _ = ReleaseAfterAsync(window);
        }
    }

    private async Task ReleaseAfterAsync(Window window)
    {
        try
        {
            await Task.Delay(window.Duration, _time).ConfigureAwait(false);
            if (!_disposed)
                window.Gate.Release();
        }
        catch (ObjectDisposedException)
        {
            // Shutdown disposed the semaphore while a release was pending — benign.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var window in _windows)
            window.Gate.Dispose();
    }
}
