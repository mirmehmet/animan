using AniMan.Core.Common;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Tenrai;
using AniMan.Infrastructure.MediaSource.Dtos;
using AniMan.Infrastructure.Services;
using AniMan.Tests.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.Services;

public class CatalogServiceTests : IDisposable
{
    private readonly SqliteContextFactory<CatalogDbContext> _factory;
    private readonly CatalogDbContext _db;
    private readonly Mock<IMediaSourceClient> _sourceMock;
    private readonly Mock<ISettingsService> _settingsMock;

    public CatalogServiceTests()
    {
        CatalogService.ClearCaches();

        _factory = new SqliteContextFactory<CatalogDbContext>(opts => new CatalogDbContext(opts));
        _db = _factory.CreateDbContext();

        _sourceMock = new Mock<IMediaSourceClient>(MockBehavior.Strict);

        _settingsMock = new Mock<ISettingsService>();
        _settingsMock.Setup(s => s.GetCacheRefreshDaysAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);
    }

    private CatalogService CreateService() => new(
        _factory, _sourceMock.Object, _settingsMock.Object,
        NullLogger<CatalogService>.Instance);

    [Fact]
    public async Task GetAnimeAsync_CacheHit_DoesNotCallTheSource()
    {
        var anime = MakeCachedAnime(1, daysOld: 0);
        _db.Anime.Add(anime);
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var result = await svc.GetAnimeAsync(1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(1);
        _sourceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAnimeAsync_ForceTrue_BypassesFreshCacheAndUpdatesIt()
    {
        _db.Anime.Add(MakeCachedAnime(7, daysOld: 0, totalEpisodes: 10));
        await _db.SaveChangesAsync();

        var updatedDto = new AnimeDto { MalId = 7, Title = "Refreshed Anime 7", Episodes = 12 };
        _sourceMock
            .Setup(j => j.GetAnimeFullAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<AnimeDto>>.Success(new SingleResult<AnimeDto> { Data = updatedDto }));
        _sourceMock
            .Setup(s => s.GetAnimeStreamingAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(
                new SingleResult<IReadOnlyList<StreamingDto>> { Data = [] }));

        var svc = CreateService();
        var result = await svc.GetAnimeAsync(7, force: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Title.Should().Be("Refreshed Anime 7");
        result.Value.TotalEpisodes.Should().Be(12);
    }

    [Fact]
    public async Task GetAnimeAsync_CacheMiss_CallsTheSourceAndStores()
    {
        var dto = MakeAnimeDto(42);
        var apiResponse = new SingleResult<AnimeDto> { Data = dto };
        _sourceMock
            .Setup(j => j.GetAnimeFullAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<AnimeDto>>.Success(apiResponse));
        _sourceMock
            .Setup(s => s.GetAnimeStreamingAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(
                new SingleResult<IReadOnlyList<StreamingDto>> { Data = [] }));

        var svc = CreateService();
        var result = await svc.GetAnimeAsync(42);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(42);
        result.Value.Title.Should().Be("Test Anime 42");

        var stored = await _db.Anime.FindAsync(42);
        stored.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAnimeAsync_StaleCache_ReturnsStaleImmediately()
    {
        var stale = MakeCachedAnime(5, daysOld: 10); // 10 days old, threshold is 7
        _db.Anime.Add(stale);
        await _db.SaveChangesAsync();

        // Allow background refresh call
        var dto = MakeAnimeDto(5);
        _sourceMock
            .Setup(j => j.GetAnimeFullAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<AnimeDto>>.Success(
                new SingleResult<AnimeDto> { Data = dto }));
        _sourceMock
            .Setup(s => s.GetAnimeStreamingAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(
                new SingleResult<IReadOnlyList<StreamingDto>> { Data = [] }));

        var svc = CreateService();
        var result = await svc.GetAnimeAsync(5);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(5);
    }

    [Fact]
    public async Task GetAnimeEpisodesAsync_EmptySourceAndKnownTotal_CreatesPlaceholders()
    {
        var anime = MakeCachedAnime(10, daysOld: 10, totalEpisodes: 12);
        _db.Anime.Add(anime);
        await _db.SaveChangesAsync();

        var emptyPage = new PagedResult<AnimeEpisodeDto>
        {
            Data = [],
            Pagination = new PaginationDto { HasNextPage = false }
        };
        _sourceMock
            .Setup(j => j.GetAnimeEpisodesAsync(10, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeEpisodeDto>>.Success(emptyPage));

        var svc = CreateService();
        var result = await svc.GetAnimeEpisodesAsync(10);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Count.Should().Be(12);
        result.Value.Select(e => e.EpisodeNumber).Should().BeEquivalentTo(Enumerable.Range(1, 12));
    }

    [Fact]
    public async Task GetAnimeEpisodesAsync_MissingParentAnime_FetchesParentFirst()
    {
        var dto = new AnimeDto { MalId = 77, Title = "Parent Fetched", Episodes = 3 };
        _sourceMock
            .Setup(j => j.GetAnimeFullAsync(77, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<AnimeDto>>.Success(new SingleResult<AnimeDto> { Data = dto }));
        _sourceMock
            .Setup(s => s.GetAnimeStreamingAsync(77, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(
                new SingleResult<IReadOnlyList<StreamingDto>> { Data = [] }));
        _sourceMock
            .Setup(j => j.GetAnimeEpisodesAsync(77, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeEpisodeDto>>.Success(new PagedResult<AnimeEpisodeDto>
            {
                Data = [new AnimeEpisodeDto { MalId = 1, Title = "Ep 1" }],
                Pagination = new PaginationDto { HasNextPage = false }
            }));

        var svc = CreateService();
        var result = await svc.GetAnimeEpisodesAsync(77);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle(e => e.EpisodeNumber == 1 && e.Title == "Ep 1");
        (await _db.Anime.AsNoTracking().AnyAsync(a => a.Id == 77)).Should().BeTrue();
    }

    [Fact]
    public async Task GetAnimeEpisodesAsync_StaleCacheAndUpstreamFailure_PreservesCachedEpisodes()
    {
        _db.Anime.Add(MakeCachedAnime(88, daysOld: 14, totalEpisodes: 2));
        _db.Episodes.AddRange(
            new CachedEpisode { AnimeId = 88, EpisodeNumber = 1, Title = "Real Ep 1" },
            new CachedEpisode { AnimeId = 88, EpisodeNumber = 2, Title = "Real Ep 2" });
        await _db.SaveChangesAsync();

        _sourceMock
            .Setup(j => j.GetAnimeEpisodesAsync(88, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeEpisodeDto>>.Failure("Network down"));

        var svc = CreateService();
        var result = await svc.GetAnimeEpisodesAsync(88);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Select(e => e.Title).Should().Equal("Real Ep 1", "Real Ep 2");
        _db.Episodes.AsNoTracking().Where(e => e.AnimeId == 88).Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAnimeStreamingAsync_CacheHit_DoesNotCallTheSource()
    {
        _db.Anime.Add(MakeCachedAnime(1, daysOld: 0));
        _db.AnimeStreaming.Add(new CachedAnimeStreaming
        {
            AnimeId = 1,
            PlatformName = "Crunchyroll",
            Url = "https://crunchyroll.com"
        });
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var result = await svc.GetAnimeStreamingAsync(1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(1);
        result.Value![0].PlatformName.Should().Be("Crunchyroll");
        _sourceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAnimeStreamingAsync_CacheMiss_AutoFetchesParentAnimeAndStoresStreaming()
    {
        var streamingResponse = new SingleResult<IReadOnlyList<StreamingDto>>
        {
            Data = [new StreamingDto { Name = "Disney+", Url = "https://disneyplus.com" }]
        };
        _sourceMock
            .Setup(s => s.GetAnimeStreamingAsync(41467, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(streamingResponse));
        _sourceMock
            .Setup(j => j.GetAnimeFullAsync(41467, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<AnimeDto>>.Success(
                new SingleResult<AnimeDto> { Data = MakeAnimeDto(41467) }));

        var svc = CreateService();
        var result = await svc.GetAnimeStreamingAsync(41467);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(1);
        result.Value![0].PlatformName.Should().Be("Disney+");

        (await _db.Anime.AsNoTracking().AnyAsync(a => a.Id == 41467)).Should().BeTrue(
            "parent Anime must be cached first so FK_AnimeStreaming_Anime_AnimeId never fails");
        var stored = await _db.AnimeStreaming.Where(s => s.AnimeId == 41467).ToListAsync();
        stored.Should().HaveCount(1);
        stored[0].PlatformName.Should().Be("Disney+");
        _sourceMock.Verify(s => s.GetAnimeStreamingAsync(41467, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAnimeStreamingAsync_NegativeCache_DoesNotCallSourceRepeatedly()
    {
        _db.Anime.Add(MakeCachedAnime(99999, daysOld: 0));
        await _db.SaveChangesAsync();

        var emptyResponse = new SingleResult<IReadOnlyList<StreamingDto>> { Data = [] };
        _sourceMock
            .Setup(s => s.GetAnimeStreamingAsync(99999, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(emptyResponse));

        var svc = CreateService();
        var first = await svc.GetAnimeStreamingAsync(99999);
        first.IsSuccess.Should().BeTrue();
        first.Value!.Should().BeEmpty();

        // Second call should hit negative cache and not call source again
        var second = await svc.GetAnimeStreamingAsync(99999);
        second.IsSuccess.Should().BeTrue();
        second.Value!.Should().BeEmpty();

        _sourceMock.Verify(s => s.GetAnimeStreamingAsync(99999, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CachedAnime MakeCachedAnime(int id, int daysOld, int? totalEpisodes = null) => new()
    {
        Id = id,
        Title = $"Cached Anime {id}",
        FetchedAt = DateTime.UtcNow.AddDays(-daysOld),
        TotalEpisodes = totalEpisodes
    };

    private static AnimeDto MakeAnimeDto(int id) => new()
    {
        MalId = id,
        Title = $"Test Anime {id}"
    };

    public void Dispose()
    {
        _db.Dispose();
        _factory.Dispose();
    }
}
