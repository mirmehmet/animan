using AniMan.Core.Common;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.Services;

public class SnapshotServiceTests : IDisposable
{
    private readonly LibraryDbContext _libraryDb;
    private readonly CatalogDbContext _catalogDb;
    private readonly IDbContextFactory<LibraryDbContext> _libraryFactory;
    private readonly IDbContextFactory<CatalogDbContext> _catalogFactory;
    private readonly Mock<ICatalogService> _catalogServiceMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly StoragePaths _paths;

    public SnapshotServiceTests()
    {
        var libraryOptions = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _libraryDb = new LibraryDbContext(libraryOptions);
        _libraryDb.Database.EnsureCreated();

        var catalogOptions = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _catalogDb = new CatalogDbContext(catalogOptions);
        _catalogDb.Database.EnsureCreated();

        var libFactory = new Mock<IDbContextFactory<LibraryDbContext>>();
        libFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new LibraryDbContext(libraryOptions));
        _libraryFactory = libFactory.Object;

        var catFactory = new Mock<IDbContextFactory<CatalogDbContext>>();
        catFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new CatalogDbContext(catalogOptions));
        _catalogFactory = catFactory.Object;

        _catalogServiceMock = new Mock<ICatalogService>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();

        _paths = new StoragePaths(
            Path.GetTempPath(),
            Path.Combine(Path.GetTempPath(), $"covers_{Guid.NewGuid()}"));
        Directory.CreateDirectory(_paths.CoversPath);

        // TrackingStatuses are already seeded by LibraryDbContext.HasData via EnsureCreated
    }

    private SnapshotService CreateService() => new(
        _libraryFactory, _catalogFactory,
        _catalogServiceMock.Object,
        new CoverStore(_httpClientFactoryMock.Object, _paths, NullLogger<CoverStore>.Instance),
        NullLogger<SnapshotService>.Instance);

    [Fact]
    public async Task SnapshotAsync_Success_CreatesLibraryItemAndSnapshot()
    {
        SetupAnime(5, "Steins;Gate");

        // Cover download fails (no real HTTP) — should succeed anyway
        _httpClientFactoryMock.Setup(f => f.CreateClient("covers"))
            .Returns(new System.Net.Http.HttpClient());

        var svc = CreateService();
        var result = await svc.SnapshotAsync(5, MediaType.Anime, 1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MalId.Should().Be(5);
        result.Value.StartedAt.Should().NotBeNull();
        result.Value.Snapshot!.Title.Should().Be("Steins;Gate");

        _libraryDb.LibraryItems.Should().HaveCount(1);
        _libraryDb.Snapshots.Should().HaveCount(1);
    }

    [Fact]
    public async Task SnapshotAsync_CoverDownloadFails_SnapshotWrittenWithNullPath()
    {
        SetupAnime(10, "Naruto");

        var failingClient = new System.Net.Http.HttpClient(new FailingHandler());
        _httpClientFactoryMock.Setup(f => f.CreateClient("covers")).Returns(failingClient);

        var svc = CreateService();
        var result = await svc.SnapshotAsync(10, MediaType.Anime, 1);

        result.IsSuccess.Should().BeTrue();
        var snapshot = _libraryDb.Snapshots.Single();
        snapshot.CoverLocalPath.Should().BeNull();
    }

    [Fact]
    public async Task SnapshotAsync_DuplicateAdd_ReturnsFailure()
    {
        SetupAnime(20, "FMA");
        _httpClientFactoryMock.Setup(f => f.CreateClient("covers"))
            .Returns(new System.Net.Http.HttpClient());

        var svc = CreateService();
        await svc.SnapshotAsync(20, MediaType.Anime, 1);

        var second = await svc.SnapshotAsync(20, MediaType.Anime, 1);
        second.IsSuccess.Should().BeFalse();
        second.Error.Should().Contain("already in your library");
    }

    [Fact]
    public async Task SnapshotAsync_SoftDeletedItem_RestoresAndPreservesProgressRatingAndNotes()
    {
        SetupAnime(25, "Cowboy Bebop");
        _httpClientFactoryMock.Setup(f => f.CreateClient("covers"))
            .Returns(new System.Net.Http.HttpClient());

        var svc = CreateService();
        var first = await svc.SnapshotAsync(25, MediaType.Anime, 1);
        first.IsSuccess.Should().BeTrue();

        // Add user progress, rating, and note, then soft-delete to Trash
        using (var db = await _libraryFactory.CreateDbContextAsync())
        {
            var item = await db.LibraryItems.SingleAsync(i => i.MalId == 25);
            item.UserRating = 9.5m;
            db.EpisodeProgress.Add(new EpisodeProgress
            {
                LibraryItemId = item.Id, EpisodeNumber = 1, IsWatched = true, WatchedAt = DateTime.UtcNow
            });
            db.Notes.Add(new Note
            {
                LibraryItemId = item.Id, Content = "Masterpiece", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            item.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        // Re-adding the soft-deleted item restores it with preserved user data
        var reAdded = await svc.SnapshotAsync(25, MediaType.Anime, 6);
        reAdded.IsSuccess.Should().BeTrue();
        reAdded.Value!.DeletedAt.Should().BeNull();
        reAdded.Value.StatusId.Should().Be(6);

        using (var verify = await _libraryFactory.CreateDbContextAsync())
        {
            var restored = await verify.LibraryItems
                .Include(i => i.EpisodeProgress)
                .Include(i => i.Notes)
                .SingleAsync(i => i.MalId == 25);
            restored.UserRating.Should().Be(9.5m);
            restored.EpisodeProgress.Should().ContainSingle(e => e.EpisodeNumber == 1 && e.IsWatched);
            restored.Notes.Should().ContainSingle(n => n.Content == "Masterpiece");
        }
    }

    [Fact]
    public async Task SnapshotAsync_SoftDeletedItem_CatalogFetchFails_PreservesTrashedItem()
    {
        SetupAnime(30, "Trigun");
        _httpClientFactoryMock.Setup(f => f.CreateClient("covers"))
            .Returns(new System.Net.Http.HttpClient());

        var svc = CreateService();
        await svc.SnapshotAsync(30, MediaType.Anime, 1);

        using (var db = await _libraryFactory.CreateDbContextAsync())
        {
            var item = await db.LibraryItems.SingleAsync(i => i.MalId == 30);
            item.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        _catalogServiceMock
            .Setup(c => c.GetAnimeAsync(30, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CachedAnime>.Failure("Offline"));

        var attempt = await svc.SnapshotAsync(30, MediaType.Anime, 1);
        attempt.IsSuccess.Should().BeFalse();

        using (var verify = await _libraryFactory.CreateDbContextAsync())
        {
            verify.LibraryItems.Should().ContainSingle(i => i.MalId == 30 && i.DeletedAt != null);
        }
    }

    [Fact]
    public async Task ReSnapshotAsync_UsesForceRefreshAndPreservesExistingCoverWhenDownloadFails()
    {
        var coverPath = Path.Combine(_paths.CoversPath, "50_anime.jpg");
        await File.WriteAllBytesAsync(coverPath, [1, 2, 3]);

        using (var db = await _libraryFactory.CreateDbContextAsync())
        {
            db.LibraryItems.Add(new LibraryItem
            {
                Id = 50,
                MalId = 50,
                MediaType = MediaType.Anime,
                StatusId = 1,
                AddedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Snapshot = new MediaSnapshot
                {
                    LibraryItemId = 50,
                    Title = "Old Title",
                    TotalEpisodes = 10,
                    CoverLocalPath = coverPath,
                    Genres = "[]",
                    SnapshotAt = DateTime.UtcNow.AddDays(-5)
                }
            });
            await db.SaveChangesAsync();
        }

        _catalogServiceMock
            .Setup(c => c.GetAnimeAsync(50, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CachedAnime>.Success(new CachedAnime
            {
                Id = 50,
                Title = "Updated Title",
                TotalEpisodes = 12,
                CoverLargeUrl = "https://example.com/50.jpg",
                FetchedAt = DateTime.UtcNow
            }));
        _httpClientFactoryMock.Setup(f => f.CreateClient("covers"))
            .Returns(new System.Net.Http.HttpClient(new FailingHandler()));

        var svc = CreateService();
        var refreshed = await svc.ReSnapshotAsync(50);

        refreshed.IsSuccess.Should().BeTrue();
        refreshed.Value!.Title.Should().Be("Updated Title");
        refreshed.Value.TotalEpisodes.Should().Be(12);
        refreshed.Value.CoverLocalPath.Should().Be(coverPath);
        _catalogServiceMock.Verify(c => c.GetAnimeAsync(50, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetupAnime(int malId, string title)
    {
        _catalogServiceMock
            .Setup(c => c.GetAnimeAsync(malId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CachedAnime>.Success(new CachedAnime
            {
                Id = malId, Title = title, FetchedAt = DateTime.UtcNow
            }));
    }

    public void Dispose()
    {
        _libraryDb.Dispose();
        _catalogDb.Dispose();
        if (Directory.Exists(_paths.CoversPath))
            Directory.Delete(_paths.CoversPath, recursive: true);
    }

    private sealed class FailingHandler : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("Simulated failure");
    }
}
