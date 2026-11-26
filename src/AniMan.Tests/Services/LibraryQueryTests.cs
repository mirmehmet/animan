using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Services;
using AniMan.Tests.Integration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniMan.Tests.Services;

/// <summary>
/// The light library reads (audit AM-019) and the write counter that lets pages skip
/// reloads (AM-017).
/// </summary>
public class LibraryQueryTests : IDisposable
{
    private readonly LibraryWriteInterceptor _writes = new();
    private readonly SqliteContextFactory<LibraryDbContext> _factory;

    public LibraryQueryTests()
    {
        _factory = new SqliteContextFactory<LibraryDbContext>(opts => new LibraryDbContext(opts), _writes);
        Seed();
    }

    private void Seed()
    {
        using var db = _factory.CreateDbContext();
        var now = DateTime.UtcNow;
        db.LibraryItems.AddRange(
            new LibraryItem
            {
                Id = 1, MalId = 100, MediaType = MediaType.Anime, StatusId = 1, AddedAt = now, UpdatedAt = now,
                Snapshot = new MediaSnapshot { Title = "Watched three", TotalEpisodes = 12, Genres = "[]", SnapshotAt = now },
                EpisodeProgress =
                {
                    new EpisodeProgress { EpisodeNumber = 1, IsWatched = true, WatchedAt = now },
                    new EpisodeProgress { EpisodeNumber = 2, IsWatched = true, WatchedAt = now },
                    new EpisodeProgress { EpisodeNumber = 3, IsWatched = true, WatchedAt = now },
                    new EpisodeProgress { EpisodeNumber = 4, IsWatched = false }
                }
            },
            new LibraryItem
            {
                Id = 2, MalId = 200, MediaType = MediaType.Manga, StatusId = 2, AddedAt = now, UpdatedAt = now,
                Snapshot = new MediaSnapshot { Title = "Read one", TotalChapters = 10, Genres = "[]", SnapshotAt = now },
                ChapterProgress = { new ChapterProgress { ChapterNumber = 1, IsRead = true, ReadAt = now } }
            },
            new LibraryItem
            {
                Id = 3, MalId = 300, MediaType = MediaType.Anime, StatusId = 1, AddedAt = now, UpdatedAt = now,
                DeletedAt = now,
                Snapshot = new MediaSnapshot { Title = "In the trash", Genres = "[]", SnapshotAt = now }
            });
        db.SaveChanges();
    }

    private TrackingService Tracking() => new(_factory, NullLogger<TrackingService>.Instance);

    [Fact]
    public async Task Overview_CountsDoneProgress_WithoutLoadingTheRows()
    {
        var anime = await Tracking().GetLibraryOverviewAsync(MediaType.Anime);
        var manga = await Tracking().GetLibraryOverviewAsync(MediaType.Manga);

        var a = anime.Value!.Should().ContainSingle("the trashed anime is left out").Subject;
        a.ProgressDone.Should().Be(3, "the unwatched row does not count");
        a.Item.Snapshot!.Title.Should().Be("Watched three");
        a.Item.Status.Should().NotBeNull();
        a.Item.EpisodeProgress.Should().BeEmpty("progress rows stay in the database");

        manga.Value!.Single().ProgressDone.Should().Be(1);
    }

    [Fact]
    public async Task Keys_ListActiveTitlesOnly()
    {
        var keys = await Tracking().GetLibraryKeysAsync();

        keys.Value.Should().BeEquivalentTo(new[] { (100, MediaType.Anime), (200, MediaType.Manga) });
    }

    [Fact]
    public async Task FindItemId_MatchesMalIdAndType_AndSkipsTheTrash()
    {
        (await Tracking().FindItemIdAsync(100, MediaType.Anime)).Value.Should().Be(1);
        (await Tracking().FindItemIdAsync(100, MediaType.Manga)).Value.Should().BeNull("same MAL id, other type");
        (await Tracking().FindItemIdAsync(300, MediaType.Anime)).Value.Should().BeNull("it is in the trash");
    }

    [Fact]
    public async Task CountAllItems_IncludesTheTrash()
    {
        (await Tracking().CountAllItemsAsync()).Value.Should().Be(3);
    }

    [Fact]
    public async Task LibraryWrite_BumpsTheVersion_ReadsDoNot()
    {
        var before = _writes.Current;
        await Tracking().GetLibraryOverviewAsync(MediaType.Anime);
        _writes.Current.Should().Be(before, "reading changes nothing");

        await Tracking().SetEpisodeWatchedAsync(1, 5, watched: true);

        _writes.Current.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task SettingsWrite_LeavesTheVersionAlone()
    {
        using var settings = new SettingsService(_factory, NullLogger<SettingsService>.Instance);
        var before = _writes.Current;

        await settings.SetAsync("LibraryCardSize", "3");
        await settings.SetAsync("LibraryCardSize", "4");

        _writes.Current.Should().Be(before, "a slider or sort change must not reload the library pages");
    }

    [Fact]
    public async Task BulkDelete_BumpsTheVersion()
    {
        var before = _writes.Current;

        await Tracking().EmptyTrashAsync();

        _writes.Current.Should().BeGreaterThan(before, "ExecuteDelete bypasses SaveChanges but not the command interceptor");
    }

    [Theory]
    [InlineData("""INSERT INTO "EpisodeProgress" ("EpisodeNumber") VALUES (@p0) RETURNING "Id";""", true)]
    [InlineData("""UPDATE "LibraryItems" SET "StatusId" = @p0 WHERE "Id" = @p1 RETURNING 1;""", true)]
    [InlineData("""DELETE FROM "Notes" WHERE "Id" = @p0;""", true)]
    [InlineData("""INSERT OR REPLACE INTO "Snapshots" VALUES (1);""", true)]
    [InlineData("""UPDATE "Settings" SET "Value" = @p0 WHERE "Key" = @p1;""", false)]
    [InlineData("""INSERT INTO "Settings" ("Key", "Value") VALUES (@p0, @p1);""", false)]
    [InlineData("""UPDATE "Settings" SET "Value" = @p0; UPDATE "LibraryItems" SET "IsFavorite" = 1;""", true)]
    [InlineData("""SELECT "l"."Id" FROM "LibraryItems" AS "l" WHERE "l"."DeletedAt" IS NULL""", false)]
    [InlineData("PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;", false)]
    [InlineData("", false)]
    public void WritesLibraryData_RecognisesWritesOutsideSettings(string sql, bool expected)
    {
        LibraryWriteInterceptor.WritesLibraryData(sql).Should().Be(expected);
    }

    public void Dispose() => _factory.Dispose();
}
