using AniTrack.Core.Common;
using AniTrack.Core.Domain.Models;
using AniTrack.Core.Interfaces;
using AniTrack.Infrastructure.Data;
using AniTrack.Infrastructure.Jikan;
using AniTrack.Infrastructure.Jikan.Dtos;
using AniTrack.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniTrack.Tests.Services;

public class CatalogServiceTests : IDisposable
{
    private readonly CatalogDbContext _db;
    private readonly IDbContextFactory<CatalogDbContext> _factory;
    private readonly Mock<IJikanClient> _jikanMock;
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

        _jikanMock = new Mock<IJikanClient>(MockBehavior.Strict);

        _settingsMock = new Mock<ISettingsService>();
        _settingsMock.Setup(s => s.GetCacheRefreshDaysAsync()).ReturnsAsync(7);
    }

    private CatalogService CreateService() => new(
        _factory, _jikanMock.Object, _settingsMock.Object,
        NullLogger<CatalogService>.Instance);

    [Fact]
    public async Task GetAnimeAsync_CacheHit_DoesNotCallJikan()
    {
        var anime = MakeCachedAnime(1, daysOld: 0);
        _db.Anime.Add(anime);
        await _db.SaveChangesAsync();

        var svc = CreateService();
        var result = await svc.GetAnimeAsync(1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(1);
        _jikanMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAnimeAsync_CacheMiss_CallsJikanAndStores()
    {
        var dto = MakeAnimeDto(42);
        var apiResponse = new JikanSingleResult<JikanAnimeDto> { Data = dto };
        _jikanMock
            .Setup(j => j.GetAnimeFullAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanSingleResult<JikanAnimeDto>>.Success(apiResponse));

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
        _jikanMock
            .Setup(j => j.GetAnimeFullAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanSingleResult<JikanAnimeDto>>.Success(
                new JikanSingleResult<JikanAnimeDto> { Data = dto }));

        var svc = CreateService();
        var result = await svc.GetAnimeAsync(5);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(5);
        // Background refresh fires async — we don't wait for it
    }

    [Fact]
    public async Task GetAnimeEpisodesAsync_EmptyJikanAndKnownTotal_CreatesPlaceholders()
    {
        var anime = MakeCachedAnime(10, daysOld: 10, totalEpisodes: 12);
        _db.Anime.Add(anime);
        await _db.SaveChangesAsync();

        var emptyPage = new JikanPagedResult<JikanEpisodeDto>
        {
            Data = [],
            Pagination = new JikanPaginationDto { HasNextPage = false }
        };
        _jikanMock
            .Setup(j => j.GetAnimeEpisodesAsync(10, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<JikanPagedResult<JikanEpisodeDto>>.Success(emptyPage));

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

    private static JikanAnimeDto MakeAnimeDto(int id) => new()
    {
        MalId = id,
        Title = $"Test Anime {id}"
    };

    public void Dispose() => _db.Dispose();
}
