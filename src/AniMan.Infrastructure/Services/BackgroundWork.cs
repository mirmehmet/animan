using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.Services;

/// <summary>
/// Fire-and-forget work tied to the app's lifetime (cache refreshes, the snapshot backfill).
/// On exit the app calls <see cref="StopAndWait"/> before disposing services, so this work is
/// cancelled and given a moment to end instead of running on against disposed database
/// factories (audit AM-074).
/// </summary>
public sealed class BackgroundWork(ILogger<BackgroundWork> logger) : IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<Task, byte> _running = new();

    /// <summary>Cancelled when the app is shutting down.</summary>
    public CancellationToken Stopping => _stopping.Token;

    /// <summary>
    /// Runs <paramref name="work"/> on the thread pool with the shutdown token. Failures are
    /// logged here, never thrown; cancellation at shutdown is expected and stays quiet.
    /// </summary>
    public void Run(string name, Func<CancellationToken, Task> work)
    {
        if (_stopping.IsCancellationRequested) return;

        var token = _stopping.Token;
        var task = Task.Run(async () =>
        {
            try
            {
                await work(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (token.IsCancellationRequested && ex is ObjectDisposedException)
            {
                // A service it used was disposed during shutdown.
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Background work {Name} failed", name);
            }
        }, CancellationToken.None);

        _running.TryAdd(task, 0);
        _ = task.ContinueWith(t => _running.TryRemove(t, out _), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>
    /// Cancels all work and waits up to <paramref name="timeout"/> for it to end.
    /// Returns false when something was still running at the deadline.
    /// </summary>
    public bool StopAndWait(TimeSpan timeout)
    {
        _stopping.Cancel();
        var running = _running.Keys.ToArray();
        // The tasks never fault (Run catches everything), so Wait only reports the timeout.
        return running.Length == 0 || Task.WaitAll(running, timeout);
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
    }
}
