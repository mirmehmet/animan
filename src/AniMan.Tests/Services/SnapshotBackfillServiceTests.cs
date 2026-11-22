using AniMan.Core.Common;
using AniMan.Core.Domain;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Services;
using AniMan.Tests.Integration;
using FluentAssertions;
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

    private int Seed(int malId, int dataVersion, DateTime updatedAt, bool trashed = false)
    {
        using var db = _factory.CreateDbContext();
        var item = new LibraryItem
        {
            MalId = malId, MediaType = MediaType.Anime, StatusId = 1,
            AddedAt = updatedAt, UpdatedAt = updatedAt, DeletedAt = trashed ? updatedAt : null,
            Snapshot = new MediaSnapshot
            {
                Title = $"#{malId}", Genres = "[]", SnapshotAt = updatedAt, DataVersion = dataVersion
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

    public void Dispose() => _factory.Dispose();
}
