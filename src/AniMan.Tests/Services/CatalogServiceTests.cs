using AniMan.Core.Common;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Tenrai;
using AniMan.Infrastructure.MediaSource.Dtos;
using AniMan.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.Services;

public class CatalogServiceTests : IDisposable
{
    private readonly CatalogDbContext _db;
    private readonly IDbContextFactory<CatalogDbContext> _factory;
    private readonly Mock<IMediaSourceClient> _sourceMock;
    private readonly Mock<ISettingsService> _settingsMock;

    public CatalogServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CatalogDbContext(options);
        _db.Database.EnsureCreated();

        var factoryMock = new Mock<IDbContextFactory<CatalogDbContext>>();
        factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new CatalogDbContext(options));
        _factory = factoryMock.Object;

        _sourceMock = new Mock<IMediaSourceClient>(MockBehavior.Strict);

        _settingsMock = new Mock<ISettingsService>();
        _settingsMock.Setup(s => s.GetCacheRefreshDaysAsync()).ReturnsAsync(7);
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
    public async Task GetAnimeAsync_CacheMiss_CallsTheSourceAndStores()
    {
        var dto = MakeAnimeDto(42);
        var apiResponse = new SingleResult<AnimeDto> { Data = dto };
        _sourceMock
            .Setup(j => j.GetAnimeFullAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<AnimeDto>>.Success(apiResponse));

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

        var svc = CreateService();
        var result = await svc.GetAnimeAsync(5);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(5);
        // Background refresh fires async — we don't wait for it
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

    public void Dispose() => _db.Dispose();
}
