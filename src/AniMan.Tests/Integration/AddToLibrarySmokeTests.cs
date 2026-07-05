using AniMan.Core.Common;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Jikan;
using AniMan.Infrastructure.Jikan.Dtos;
using AniMan.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.Integration;

/// <summary>
/// End-to-end "add to library" flow against real (in-memory) SQLite for both
/// databases, with only the Jikan HTTP client mocked. Exercises
/// CatalogService → catalog cache → SnapshotService → library write.
/// </summary>
public class AddToLibrarySmokeTests : IDisposable
{
    private readonly SqliteContextFactory<LibraryDbContext> _libraryFactory = new(o => new LibraryDbContext(o));
    private readonly SqliteContextFactory<CatalogDbContext> _catalogFactory = new(o => new CatalogDbContext(o));

    [Fact]
    public async Task AddAnime_FetchesFromJikan_CachesCatalog_AndWritesLibraryItem()
    {
        var jikan = new Mock<IJikanClient>();
        jikan.Setup(j => j.GetAnimeFullAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanSingleResult<JikanAnimeDto>>.Success(new JikanSingleResult<JikanAnimeDto>
            {
                Data = new JikanAnimeDto
                {
                    MalId = 1, Title = "Cowboy Bebop", Episodes = 26,
                    Type = "TV", Score = 8.75, Genres = []
                }
            }));
        // Streaming fetch is fire-and-forget; return no data so it exits before touching the DB.
        jikan.Setup(j => j.GetAnimeStreamingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanSingleResult<IReadOnlyList<JikanStreamingDto>>>.Success(
                new JikanSingleResult<IReadOnlyList<JikanStreamingDto>> { Data = null }));

        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetCacheRefreshDaysAsync()).ReturnsAsync(7);

        var catalogService = new CatalogService(
            _catalogFactory, jikan.Object, settings.Object, NullLogger<CatalogService>.Instance);

        // Cover URL is null in the mocked DTO, so the HTTP factory is never used.
        var httpFactory = new Mock<IHttpClientFactory>();
        var storagePaths = new StoragePaths(Path.GetTempPath(), Path.GetTempPath());

        var snapshotService = new SnapshotService(
            _libraryFactory, _catalogFactory, catalogService,
            new CoverStore(httpFactory.Object, storagePaths, NullLogger<CoverStore>.Instance),
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

        jikan.Verify(j => j.GetAnimeFullAsync(1, It.IsAny<CancellationToken>()), Times.Once);
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

        var jikan = new Mock<IJikanClient>();
        var settings = new Mock<ISettingsService>();
        var catalogService = new CatalogService(
            _catalogFactory, jikan.Object, settings.Object, NullLogger<CatalogService>.Instance);
        var snapshotService = new SnapshotService(
            _libraryFactory, _catalogFactory, catalogService,
            new CoverStore(
                new Mock<IHttpClientFactory>().Object,
                new StoragePaths(Path.GetTempPath(), Path.GetTempPath()),
                NullLogger<CoverStore>.Instance),
            NullLogger<SnapshotService>.Instance);

        var result = await snapshotService.SnapshotAsync(1, MediaType.Anime, 1);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already in your library");
        jikan.Verify(j => j.GetAnimeFullAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public void Dispose()
    {
        _libraryFactory.Dispose();
        _catalogFactory.Dispose();
    }
}
