using AniTrack.Core.Domain.Models;
using AniTrack.Infrastructure.Data;
using AniTrack.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniTrack.Tests.Services;

public class TrackingServiceTests : IDisposable
{
    private readonly LibraryDbContext _db;
    private readonly IDbContextFactory<LibraryDbContext> _factory;

    public TrackingServiceTests()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new LibraryDbContext(options);
        _db.Database.EnsureCreated();

        var factoryMock = new Mock<IDbContextFactory<LibraryDbContext>>();
        factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new LibraryDbContext(options));
        _factory = factoryMock.Object;

        SeedData();
    }

    private void SeedData()
    {
        // TrackingStatuses are already seeded by LibraryDbContext.HasData via EnsureCreated
        _db.LibraryItems.Add(new LibraryItem
        {
            Id = 1, MalId = 100,
            MediaType = AniTrack.Core.Domain.Enums.MediaType.Anime,
            StatusId = 1, AddedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        _db.Snapshots.Add(new MediaSnapshot
        {
            LibraryItemId = 1, Title = "Test Anime",
            TotalEpisodes = 12, Genres = "[]",
            SnapshotAt = DateTime.UtcNow
        });

        _db.LibraryItems.Add(new LibraryItem
        {
            Id = 2, MalId = 200,
            MediaType = AniTrack.Core.Domain.Enums.MediaType.Manga,
            StatusId = 2, AddedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        _db.Snapshots.Add(new MediaSnapshot
        {
            LibraryItemId = 2, Title = "Test Manga",
            TotalChapters = 10, Genres = "[]",
            SnapshotAt = DateTime.UtcNow
        });
        _db.SaveChanges();
    }

    private TrackingService Create() => new(_factory, NullLogger<TrackingService>.Instance);

    [Fact]
    public async Task ToggleEpisode_UnwatchedToWatched_CreatesRow()
    {
        var svc = Create();
        var result = await svc.ToggleEpisodeAsync(1, 1);

        result.IsSuccess.Should().BeTrue();
        var ep = _db.EpisodeProgress.Single(e => e.LibraryItemId == 1 && e.EpisodeNumber == 1);
        ep.IsWatched.Should().BeTrue();
    }

    [Fact]
    public async Task ToggleEpisode_WatchedToUnwatched_ClearsWatchedAt()
    {
        _db.EpisodeProgress.Add(new EpisodeProgress
        {
            LibraryItemId = 1, EpisodeNumber = 2,
            IsWatched = true, WatchedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var svc = Create();
        var result = await svc.ToggleEpisodeAsync(1, 2);

        result.IsSuccess.Should().BeTrue();
        var ep = _db.EpisodeProgress.AsNoTracking().Single(e => e.LibraryItemId == 1 && e.EpisodeNumber == 2);
        ep.IsWatched.Should().BeFalse();
        ep.WatchedAt.Should().BeNull();
    }

    [Fact]
    public async Task MarkUpToHere_Episode7Of12_Creates7Rows()
    {
        var svc = Create();
        var result = await svc.MarkUpToHereAsync(1, 7);

        result.IsSuccess.Should().BeTrue();
        var watched = _db.EpisodeProgress.Count(e => e.LibraryItemId == 1 && e.IsWatched);
        watched.Should().Be(7);
        result.Value!.AutoCompleteNeeded.Should().BeFalse();
    }

    [Fact]
    public async Task MarkUpToHere_AllEpisodes_AutoCompleteNeeded()
    {
        var svc = Create();
        var result = await svc.MarkUpToHereAsync(1, 12);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AutoCompleteNeeded.Should().BeTrue();
    }

    [Fact]
    public async Task SaveNote_Create_ThenUpdate()
    {
        var svc = Create();

        await svc.SaveNoteAsync(1, null, null, "First note");
        var created = _db.Notes.AsNoTracking().Single(n => n.LibraryItemId == 1);
        created.Content.Should().Be("First note");

        await svc.SaveNoteAsync(1, null, null, "Updated note");
        var updated = _db.Notes.AsNoTracking().Single(n => n.LibraryItemId == 1);
        updated.Content.Should().Be("Updated note");
        _db.Notes.AsNoTracking().Count(n => n.LibraryItemId == 1).Should().Be(1);
    }

    [Fact]
    public async Task SetEpisodeWatched_True_ThenFalse_SetsExactState()
    {
        var svc = Create();

        await svc.SetEpisodeWatchedAsync(1, 3, true);
        var ep = _db.EpisodeProgress.AsNoTracking().Single(e => e.LibraryItemId == 1 && e.EpisodeNumber == 3);
        ep.IsWatched.Should().BeTrue();
        ep.WatchedAt.Should().NotBeNull();

        await svc.SetEpisodeWatchedAsync(1, 3, false);
        ep = _db.EpisodeProgress.AsNoTracking().Single(e => e.LibraryItemId == 1 && e.EpisodeNumber == 3);
        ep.IsWatched.Should().BeFalse();
        ep.WatchedAt.Should().BeNull();
    }

    [Fact]
    public async Task SetEpisodeWatched_SameValueTwice_NoDuplicateRow()
    {
        var svc = Create();

        await svc.SetEpisodeWatchedAsync(1, 4, true);
        await svc.SetEpisodeWatchedAsync(1, 4, true);

        _db.EpisodeProgress.AsNoTracking()
            .Count(e => e.LibraryItemId == 1 && e.EpisodeNumber == 4)
            .Should().Be(1);
    }

    [Fact]
    public async Task SetChapterRead_True_ThenFalse_SetsExactState()
    {
        var svc = Create();

        await svc.SetChapterReadAsync(2, 5, true);
        var ch = _db.ChapterProgress.AsNoTracking().Single(c => c.LibraryItemId == 2 && c.ChapterNumber == 5);
        ch.IsRead.Should().BeTrue();
        ch.ReadAt.Should().NotBeNull();

        await svc.SetChapterReadAsync(2, 5, false);
        ch = _db.ChapterProgress.AsNoTracking().Single(c => c.LibraryItemId == 2 && c.ChapterNumber == 5);
        ch.IsRead.Should().BeFalse();
        ch.ReadAt.Should().BeNull();
    }

    [Fact]
    public async Task GetProgress_Anime_CountsEpisodes()
    {
        var svc = Create();
        await svc.MarkUpToHereAsync(1, 5);

        var result = await svc.GetProgressAsync(1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Watched.Should().Be(5);
        result.Value.Total.Should().Be(12);
    }

    [Fact]
    public async Task GetProgress_Manga_CountsChapters()
    {
        var svc = Create();
        await svc.MarkChaptersUpToAsync(2, 3);

        var result = await svc.GetProgressAsync(2);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Watched.Should().Be(3);
        result.Value.Total.Should().Be(10);
    }

    [Fact]
    public async Task GetLibrary_FiltersByMediaType()
    {
        var svc = Create();

        var anime = await svc.GetLibraryAsync("anime");
        anime.IsSuccess.Should().BeTrue();
        anime.Value!.Should().ContainSingle(i => i.MalId == 100);

        var manga = await svc.GetLibraryAsync("manga");
        manga.IsSuccess.Should().BeTrue();
        manga.Value!.Should().ContainSingle(i => i.MalId == 200);
    }

    public void Dispose() => _db.Dispose();
}
