using AniMan.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniMan.Tests.Services;

public class BackgroundWorkTests
{
    private static BackgroundWork Create() => new(NullLogger<BackgroundWork>.Instance);

    [Fact]
    public async Task StopAndWait_CancelsRunningWorkAndWaitsForItToEnd()
    {
        using var work = Create();
        // Continuations run asynchronously: otherwise SetResult would run the rest of this test
        // on the job's own thread, and StopAndWait would wait there for that very job.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = false;
        work.Run("long job", async ct =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            finally { ended = true; }
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        work.StopAndWait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        ended.Should().BeTrue("the job saw the shutdown token and finished before services are disposed");
    }

    [Fact]
    public void StopAndWait_ReportsWorkThatIgnoresCancellation()
    {
        using var work = Create();
        using var release = new ManualResetEventSlim();
        work.Run("stubborn job", _ => Task.Run(() => release.Wait(TimeSpan.FromSeconds(10))));

        work.StopAndWait(TimeSpan.FromMilliseconds(100)).Should().BeFalse();
        release.Set();
    }

    [Fact]
    public void Run_FailingWork_IsLoggedNotThrown()
    {
        using var work = Create();

        work.Run("failing job", _ => throw new InvalidOperationException("boom"));

        work.StopAndWait(TimeSpan.FromSeconds(5)).Should().BeTrue();
    }

    [Fact]
    public void Run_AfterStop_DoesNotStartTheWork()
    {
        using var work = Create();
        work.StopAndWait(TimeSpan.FromSeconds(1));
        var ran = false;

        work.Run("late job", _ => { ran = true; return Task.CompletedTask; });
        work.StopAndWait(TimeSpan.FromSeconds(1));

        ran.Should().BeFalse();
    }
}
