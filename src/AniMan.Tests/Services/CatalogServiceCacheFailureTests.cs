using AniMan.Core.Common;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Tenrai;
using AniMan.Infrastructure.MediaSource.Dtos;
using AniMan.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.Services;

/// <summary>
/// The catalog is an optimisation, not the product: when the source answers successfully
/// but the row cannot be cached, the fetched data must still reach the caller. Getting
/// this wrong is what turned the missing-migrations bug into a blank Discover page —
/// the API returned 200 and the result was discarded because the write threw.
///
/// The database here is deliberately left without a schema, reproducing that exact
/// failure ("no such table") rather than simulating it with a mock.
/// </summary>
public class CatalogServiceCacheFailureTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<CatalogDbContext> _factory;
    private readonly Mock<IMediaSourceClient> _sourceMock = new(MockBehavior.Strict);
    private readonly Mock<ISettingsService> _settingsMock = new();

    public CatalogServiceCacheFailureTests()
    {
        // Opened but never migrated / EnsureCreated — every table is missing.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(_connection).Options;

        var factoryMock = new Mock<IDbContextFactory<CatalogDbContext>>();
        factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new CatalogDbContext(options));
        _factory = factoryMock.Object;

        _settingsMock.Setup(s => s.GetCacheRefreshDaysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
    }

    private CatalogService CreateService(ILogger<CatalogService>? logger = null) => new(
        _factory, _sourceMock.Object, _settingsMock.Object,
        logger ?? NullLogger<CatalogService>.Instance);

    [Fact]
    public async Task GetTopAnimeAsync_CacheWriteFails_StillReturnsFetchedData()
    {
        _sourceMock.Setup(j => j.GetTopAnimeAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeDto>>.Success(
                new PagedResult<AnimeDto>
                {
                    Data = [MakeAnimeDto(1), MakeAnimeDto(2)]
                }));

        var logger = new RecordingLogger();
        var result = await CreateService(logger).GetTopAnimeAsync();

        int[] expectedIds = [1, 2];
        result.IsSuccess.Should().BeTrue("the API call succeeded; only caching failed");
        result.Value!.Select(a => a.Id).Should().BeEquivalentTo(expectedIds);

        // Proves the recovery path actually ran: if the fixture ever gained a schema,
        // these tests would pass while guarding nothing.
        logger.Errors.Should().ContainMatch("*Caching*failed*");
    }

    [Fact]
    public async Task GetTopMangaAsync_CacheWriteFails_StillReturnsFetchedData()
    {
        _sourceMock.Setup(j => j.GetTopMangaAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<MangaDto>>.Success(
                new PagedResult<MangaDto> { Data = [MakeMangaDto(7)] }));

        var result = await CreateService().GetTopMangaAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Single().Id.Should().Be(7);
    }

    [Fact]
    public async Task SearchAnimeAsync_CacheWriteFails_StillReturnsFetchedData()
    {
        _sourceMock.Setup(j => j.SearchAnimeAsync("konosuba", 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeDto>>.Success(
                new PagedResult<AnimeDto> { Data = [MakeAnimeDto(30831)] }));

        var result = await CreateService().SearchAnimeAsync("konosuba");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Single().Id.Should().Be(30831);
    }

    [Fact]
    public async Task GetCurrentSeasonAsync_CacheWriteFails_StillReturnsFetchedData()
    {
        _sourceMock.Setup(j => j.GetCurrentSeasonAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeDto>>.Success(
                new PagedResult<AnimeDto> { Data = [MakeAnimeDto(99)] }));

        var result = await CreateService().GetCurrentSeasonAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Single().Id.Should().Be(99);
    }

    [Fact]
    public async Task GetTopAnimeAsync_ApiFails_ReportsFailure()
    {
        // The guard above must not turn a genuine API failure into a false success.
        _sourceMock.Setup(j => j.GetTopAnimeAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeDto>>.Failure("504 Gateway Time-out"));

        var result = await CreateService().GetTopAnimeAsync();

        result.IsSuccess.Should().BeFalse();
    }

    private static AnimeDto MakeAnimeDto(int id) => new()
    {
        MalId = id,
        Title = $"Anime {id}"
    };

    private static MangaDto MakeMangaDto(int id) => new()
    {
        MalId = id,
        Title = $"Manga {id}"
    };

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Captures error-level messages so a test can assert the failure path was taken.</summary>
    private sealed class RecordingLogger : ILogger<CatalogService>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
                Errors.Add(formatter(state, exception));
        }
    }
}
