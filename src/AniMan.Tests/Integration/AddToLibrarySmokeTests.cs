using AniMan.Core.Common;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.MediaSource;
using AniMan.Infrastructure.MediaSource.Dtos;
using AniMan.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using AniMan.Tests.TestDoubles;

namespace AniMan.Tests.Integration;

/// <summary>
/// End-to-end "add to library" flow against real (in-memory) SQLite for both
/// databases, with only the media-source client mocked. Exercises
/// CatalogService → catalog cache → SnapshotService → library write.
/// </summary>
[Collection(CatalogStateCollection.Name)]
public class AddToLibrarySmokeTests : IDisposable
{
    private readonly SqliteContextFactory<LibraryDbContext> _libraryFactory = new(o => new LibraryDbContext(o));
    private readonly SqliteContextFactory<CatalogDbContext> _catalogFactory = new(o => new CatalogDbContext(o));
    private readonly BackgroundWork _backgroundWork = new(NullLogger<BackgroundWork>.Instance);
    private readonly TestStorage _storage = new();
    private readonly StubHttpHandler _http = new();

    [Fact]
    public async Task AddAnime_FetchesFromSource_CachesCatalog_AndWritesLibraryItem()
    {
        var source = new Mock<IMediaSourceClient>();
        source.Setup(j => j.GetAnimeFullAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<AnimeDto>>.Success(new SingleResult<AnimeDto>
            {
                Data = new AnimeDto
                {
                    MalId = 1, Title = "Cowboy Bebop", Episodes = 26,
                    Type = "TV", Score = 8.75, Genres = []
                }
            }));
        // Streaming fetch is fire-and-forget; return no data so it exits before touching the DB.
        source.Setup(j => j.GetAnimeStreamingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(
                new SingleResult<IReadOnlyList<StreamingDto>> { Data = null }));

        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetCacheRefreshDaysAsync()).ReturnsAsync(7);

        var catalogService = new CatalogService(
            _catalogFactory, source.Object, settings.Object,
            _backgroundWork, NullLogger<CatalogService>.Instance);

        // Cover URL is null in the mocked DTO, so no download is attempted (checked below).
        var snapshotService = new SnapshotService(
            _libraryFactory, _catalogFactory, catalogService,
            new CoverStore(_http.Factory(), _storage.Paths, NullLogger<CoverStore>.Instance),
            NullLogger<SnapshotService>.Instance);

        // ── Act: add anime 1 with status "Watching" (id 1) ───────────────────────
        var result = await snapshotService.SnapshotAsync(1, MediaType.Anime, statusId: 1);

        // ── Assert: library item + snapshot persisted ────────────────────────────
        result.IsSuccess.Should().BeTrue(result.Error);

        await using (var lib = _libraryFactory.CreateDbContext())
        {
            var item = await lib.LibraryItems.Include(i => i.Snapshot).SingleAsync();
            item.MalId.Should().Be(1);
            item.MediaType.Should().Be(MediaType.Anime);
            item.StatusId.Should().Be(1);
            item.Snapshot!.Title.Should().Be("Cowboy Bebop");
            item.Snapshot.TotalEpisodes.Should().Be(26);
        }

        // ── Assert: anime was cached in the catalog DB ───────────────────────────
        await using (var cat = _catalogFactory.CreateDbContext())
            (await cat.Anime.SingleAsync()).Title.Should().Be("Cowboy Bebop");

        source.Verify(j => j.GetAnimeFullAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AddAnime_AlreadyInLibrary_ReturnsFailure()
    {
        await using (var lib = _libraryFactory.CreateDbContext())
        {
            lib.LibraryItems.Add(new Core.Domain.Models.LibraryItem
            {
                MalId = 1, MediaType = MediaType.Anime, StatusId = 1,
                AddedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            await lib.SaveChangesAsync();
        }

        var source = new Mock<IMediaSourceClient>();
        var settings = new Mock<ISettingsService>();
        var catalogService = new CatalogService(
            _catalogFactory, source.Object, settings.Object,
            _backgroundWork, NullLogger<CatalogService>.Instance);
        var snapshotService = new SnapshotService(
            _libraryFactory, _catalogFactory, catalogService,
            new CoverStore(_http.Factory(), _storage.Paths, NullLogger<CoverStore>.Instance),
            NullLogger<SnapshotService>.Instance);

        var result = await snapshotService.SnapshotAsync(1, MediaType.Anime, 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already in your library");
        source.Verify(j => j.GetAnimeFullAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public void Dispose()
    {
        // Stop the fire-and-forget streaming refresh before its database goes away.
        _backgroundWork.StopAndWait(TimeSpan.FromSeconds(5));
        _backgroundWork.Dispose();
        _libraryFactory.Dispose();
        _catalogFactory.Dispose();
        _http.Dispose();
        _storage.Dispose();
    }
}
