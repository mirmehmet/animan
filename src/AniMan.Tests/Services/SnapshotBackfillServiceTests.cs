using AniMan.Core.Common;
using AniMan.Core.Domain;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Services;
using AniMan.Tests.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.Services;

public class SnapshotBackfillServiceTests : IDisposable
{
    private readonly SqliteContextFactory<LibraryDbContext> _factory = new(opts => new LibraryDbContext(opts));
    private readonly Mock<ISnapshotService> _snapshots = new();
    private readonly List<int> _calls = [];

    private SnapshotBackfillService Create() => new(_factory, _snapshots.Object,
        NullLogger<SnapshotBackfillService>.Instance)
    {
        StartupDelay = TimeSpan.Zero,
        DelayBetweenItems = TimeSpan.Zero
    };

    private void Respond(bool success) =>
        _snapshots
            .Setup(s => s.FillExtendedMetadataAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, CancellationToken>((id, _) => _calls.Add(id))
            .ReturnsAsync(success ? Result.Success() : Result.Failure("offline"));

    /// <param name="fallbackFilled">Leaves themes/demographics null, as only the fallback source does;
    /// primary-source data always carries a (possibly empty) list.</param>
    private int Seed(int malId, int dataVersion, DateTime updatedAt, bool trashed = false, bool fallbackFilled = false)
    {
        var lists = dataVersion == MediaDataVersion.Current && !fallbackFilled ? "[]" : null;
        using var db = _factory.CreateDbContext();
        var item = new LibraryItem
        {
            MalId = malId, MediaType = MediaType.Anime, StatusId = 1,
            AddedAt = updatedAt, UpdatedAt = updatedAt, DeletedAt = trashed ? updatedAt : null,
            Snapshot = new MediaSnapshot
            {
                Title = $"#{malId}", Genres = "[]", SnapshotAt = updatedAt, DataVersion = dataVersion,
                Themes = lists, Demographics = lists
            }
        };
        db.LibraryItems.Add(item);
        db.SaveChanges();
        return item.Id;
    }

    [Fact]
    public async Task RunAsync_FillsOnlyLegacySnapshots_ActiveMostRecentFirst_TrashLast()
    {
        var t = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var trashed = Seed(1, MediaDataVersion.Legacy, t.AddDays(5), trashed: true);
        var older = Seed(2, MediaDataVersion.Legacy, t.AddDays(1));
        var newer = Seed(3, MediaDataVersion.Legacy, t.AddDays(3));
        Seed(4, MediaDataVersion.Current, t.AddDays(4));
        Respond(success: true);

        await Create().RunAsync(CancellationToken.None);

        _calls.Should().Equal(newer, older, trashed);
    }

    [Fact]
    public async Task RunAsync_NothingLegacy_DoesNothing()
    {
        Seed(1, MediaDataVersion.Current, DateTime.UtcNow);
        Respond(success: true);

        await Create().RunAsync(CancellationToken.None);

        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_SourceUnreachable_StopsAfterFiveFailuresInARow()
    {
        for (var i = 1; i <= 8; i++)
            Seed(i, MediaDataVersion.Legacy, DateTime.UtcNow.AddMinutes(i));
        Respond(success: false);

        await Create().RunAsync(CancellationToken.None);

        _calls.Should().HaveCount(5, "offline should not burn through the whole library");
    }

    [Fact]
    public async Task RunAsync_ASuccessBetweenFailures_ResetsTheFailureCount()
    {
        // AM-133: only failures *in a row* stop the run; a few broken titles among working
        // ones must not cut the backfill short.
        for (var i = 1; i <= 12; i++)
            Seed(i, MediaDataVersion.Legacy, DateTime.UtcNow.AddMinutes(i));
        var answers = new Queue<bool>([false, false, false, false, true, false, false, false, false, false, true, true]);
        _snapshots
            .Setup(s => s.FillExtendedMetadataAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, CancellationToken>((id, _) => _calls.Add(id))
            .ReturnsAsync(() => answers.Dequeue() ? Result.Success() : Result.Failure("broken title"));

        await Create().RunAsync(CancellationToken.None);

        _calls.Should().HaveCount(10, "4 failures, a success that resets the count, then 5 failures in a row");
    }

    [Fact]
    public async Task RunAsync_UnexpectedException_EndsTheRunQuietly()
    {
        // AM-133: the backfill runs unattended in the background; an exception must end it
        // (it resumes next launch), never reach the app.
        Seed(1, MediaDataVersion.Legacy, DateTime.UtcNow.AddMinutes(2));
        Seed(2, MediaDataVersion.Legacy, DateTime.UtcNow.AddMinutes(1));
        _snapshots
            .Setup(s => s.FillExtendedMetadataAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, CancellationToken>((id, _) => _calls.Add(id))
            .ThrowsAsync(new InvalidOperationException("database is locked"));

        var act = () => Create().RunAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        _calls.Should().HaveCount(1, "the run stops at the exception");
    }

    [Fact]
    public async Task RunAsync_Cancelled_ReturnsQuietly()
    {
        Seed(1, MediaDataVersion.Legacy, DateTime.UtcNow);
        Respond(success: true);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => Create().RunAsync(cts.Token);

        await act.Should().NotThrowAsync();
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_CompleteLookingFallbackSnapshot_IsRepairedAndFilled()
    {
        var real = Seed(1, MediaDataVersion.Current, DateTime.UtcNow);
        var misstamped = Seed(2, MediaDataVersion.Current, DateTime.UtcNow, fallbackFilled: true);
        Respond(success: true);

        await Create().RunAsync(CancellationToken.None);

        _calls.Should().Equal([misstamped], "only the fallback-filled snapshot is sent back for completion");
        _calls.Should().NotContain(real);
    }

    [Fact]
    public async Task Repair_ChangesOnlyTheVersionMarker_AndRunsOnce()
    {
        var id = Seed(1, MediaDataVersion.Current, DateTime.UtcNow, fallbackFilled: true);
        var service = Create();

        await service.RepairMisstampedFallbackDataAsync(CancellationToken.None);

        await using (var db = await _factory.CreateDbContextAsync())
        {
            var snap = await db.Snapshots.SingleAsync(s => s.LibraryItemId == id);
            snap.DataVersion.Should().Be(MediaDataVersion.Partial);
            snap.Title.Should().Be("#1", "the repair never touches the data itself");

            // Pretend the backfill completed it; a second launch must not mark it again.
            snap.DataVersion = MediaDataVersion.Current;
            await db.SaveChangesAsync();
        }

        await service.RepairMisstampedFallbackDataAsync(CancellationToken.None);

        await using var verify = await _factory.CreateDbContextAsync();
        (await verify.Snapshots.SingleAsync(s => s.LibraryItemId == id)).DataVersion
            .Should().Be(MediaDataVersion.Current, "the repair is one-time");
    }

    public void Dispose() => _factory.Dispose();
}
