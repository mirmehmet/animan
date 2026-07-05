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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetupAnime(int malId, string title)
    {
        _catalogServiceMock
            .Setup(c => c.GetAnimeAsync(malId, It.IsAny<CancellationToken>()))
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
