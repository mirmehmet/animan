using AniMan.Core.Domain;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Infrastructure;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Services;
using AniMan.Tests.Integration;
using AniMan.Tests.TestDoubles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.Services;

/// <summary>
/// The trash paths are the only places that permanently delete library rows and cover
/// files, so they are pinned here: what gets purged, what must survive, and which covers go.
/// </summary>
public sealed class TrackingServiceTrashTests : IDisposable
{
    private const int ActiveId = 1;        // never trashed
    private const int ExpiredId = 2;       // trashed 31 days ago
    private const int RecentTrashId = 3;   // trashed 5 days ago

    private readonly SqliteContextFactory<LibraryDbContext> _factory;
    private readonly TestStorage _storage = new();
    private readonly StoragePaths _paths;
    private readonly CoverStore _covers;
    private readonly TrackingService _service;

    public TrackingServiceTrashTests()
    {
        _factory = new SqliteContextFactory<LibraryDbContext>(opts => new LibraryDbContext(opts));
        _paths = _storage.Paths;

        _covers = new CoverStore(new StubHttpHandler().Factory(), _paths, NullLogger<CoverStore>.Instance);
        _service = new TrackingService(_factory, NullLogger<TrackingService>.Instance, _covers);

        Seed();
    }

    private void Seed()
    {
        var now = DateTime.UtcNow;
        using var db = _factory.CreateDbContext();

        db.LibraryItems.AddRange(
            NewItem(ActiveId, 100, MediaType.Anime, deletedAt: null),
            NewItem(ExpiredId, 200, MediaType.Anime, deletedAt: now.AddDays(-31)),
            NewItem(RecentTrashId, 300, MediaType.Manga, deletedAt: now.AddDays(-5)));

        db.EpisodeProgress.AddRange(
            new EpisodeProgress { LibraryItemId = ActiveId, EpisodeNumber = 1, IsWatched = true },
            new EpisodeProgress { LibraryItemId = ExpiredId, EpisodeNumber = 1, IsWatched = true });
        db.ChapterProgress.Add(
            new ChapterProgress { LibraryItemId = RecentTrashId, ChapterNumber = 1, IsRead = true });
        db.Notes.AddRange(
            new Note { LibraryItemId = ActiveId, Content = "keep", CreatedAt = now, UpdatedAt = now },
            new Note { LibraryItemId = ExpiredId, Content = "purge", CreatedAt = now, UpdatedAt = now });

        db.SaveChanges();

        foreach (var (malId, type) in new[] { (100, MediaType.Anime), (200, MediaType.Anime), (300, MediaType.Manga) })
            File.WriteAllBytes(_covers.GetLocalPath(malId, type), [1, 2, 3]);
    }

    private static LibraryItem NewItem(int id, int malId, MediaType type, DateTime? deletedAt) => new()
    {
        Id = id,
        MalId = malId,
        MediaType = type,
        StatusId = type == MediaType.Anime ? TrackingStatusIds.Watching : TrackingStatusIds.Reading,
        AddedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        DeletedAt = deletedAt,
        Snapshot = new MediaSnapshot
        {
            Title = $"Title {malId}",
            TotalEpisodes = type == MediaType.Anime ? 12 : null,
            TotalChapters = type == MediaType.Manga ? 10 : null,
            Genres = "[]",
            SnapshotAt = DateTime.UtcNow
        }
    };

    private async Task<int[]> RemainingItemIdsAsync()
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.LibraryItems.AsNoTracking().OrderBy(i => i.Id).Select(i => i.Id).ToArrayAsync();
    }

    private bool CoverExists(int malId, MediaType type) => File.Exists(_covers.GetLocalPath(malId, type));

    [Fact]
    public async Task PurgeExpiredTrash_RemovesOnlyItemsTrashedMoreThan30DaysAgo()
    {
        await _service.PurgeExpiredTrashAsync();

        (await RemainingItemIdsAsync()).Should().Equal(
            [ActiveId, RecentTrashId], "active items and recent trash must survive the startup purge");
    }

    [Fact]
    public async Task PurgeExpiredTrash_RemovesProgressNotesAndSnapshotOfPurgedItem()
    {
        await _service.PurgeExpiredTrashAsync();

        await using var db = await _factory.CreateDbContextAsync();
        (await db.Snapshots.AnyAsync(s => s.LibraryItemId == ExpiredId)).Should().BeFalse();
        (await db.EpisodeProgress.AnyAsync(e => e.LibraryItemId == ExpiredId)).Should().BeFalse();
        (await db.Notes.AnyAsync(n => n.LibraryItemId == ExpiredId)).Should().BeFalse();

        (await db.EpisodeProgress.AnyAsync(e => e.LibraryItemId == ActiveId)).Should().BeTrue();
        (await db.Notes.AnyAsync(n => n.LibraryItemId == ActiveId)).Should().BeTrue();
        (await db.ChapterProgress.AnyAsync(c => c.LibraryItemId == RecentTrashId)).Should().BeTrue();
    }

    [Fact]
    public async Task PurgeExpiredTrash_DeletesOnlyTheCoversOfPurgedItems()
    {
        await _service.PurgeExpiredTrashAsync();

        CoverExists(200, MediaType.Anime).Should().BeFalse();
        CoverExists(100, MediaType.Anime).Should().BeTrue();
        CoverExists(300, MediaType.Manga).Should().BeTrue("recently trashed items can still be restored with their cover");
    }

    [Fact]
    public async Task EmptyTrash_RemovesEveryTrashedItemAndKeepsActiveOnes()
    {
        var result = await _service.EmptyTrashAsync();

        result.IsSuccess.Should().BeTrue();
        (await RemainingItemIdsAsync()).Should().Equal([ActiveId]);

        await using var db = await _factory.CreateDbContextAsync();
        (await db.ChapterProgress.AnyAsync(c => c.LibraryItemId == RecentTrashId)).Should().BeFalse();
        (await db.Notes.AnyAsync(n => n.LibraryItemId == ExpiredId)).Should().BeFalse();
        (await db.Notes.AnyAsync(n => n.LibraryItemId == ActiveId)).Should().BeTrue();
    }

    [Fact]
    public async Task EmptyTrash_DeletesCoversOfTrashedItemsOnly()
    {
        await _service.EmptyTrashAsync();

        CoverExists(200, MediaType.Anime).Should().BeFalse();
        CoverExists(300, MediaType.Manga).Should().BeFalse();
        CoverExists(100, MediaType.Anime).Should().BeTrue();
    }

    [Fact]
    public async Task EmptyTrash_WhenTrashIsEmpty_SucceedsAndChangesNothing()
    {
        await _service.EmptyTrashAsync();

        var second = await _service.EmptyTrashAsync();

        second.IsSuccess.Should().BeTrue();
        (await RemainingItemIdsAsync()).Should().Equal([ActiveId]);
        CoverExists(100, MediaType.Anime).Should().BeTrue();
    }

    public void Dispose()
    {
        _factory.Dispose();
        _storage.Dispose();
    }
}
