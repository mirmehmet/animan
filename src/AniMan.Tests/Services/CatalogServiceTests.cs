using AniMan.Core.Common;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.MediaSource;
using AniMan.Infrastructure.MediaSource.Dtos;
using AniMan.Infrastructure.Services;
using AniMan.Tests.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using AniMan.Tests.TestDoubles;

namespace AniMan.Tests.Services;

[Collection(CatalogStateCollection.Name)]
public class CatalogServiceTests : IDisposable
{
    private readonly SqliteContextFactory<CatalogDbContext> _factory;
    private readonly CatalogDbContext _db;
    private readonly Mock<IMediaSourceClient> _sourceMock;
    private readonly Mock<ISettingsService> _settingsMock;
    private readonly BackgroundWork _background = new(NullLogger<BackgroundWork>.Instance);

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
        _factory, _sourceMock.Object, _settingsMock.Object, _background,
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
        await _background.WhenIdleAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Title.Should().Be("Refreshed Anime 7");
        result.Value.TotalEpisodes.Should().Be(12);
        _sourceMock.Verify(j => j.GetAnimeFullAsync(7, It.IsAny<CancellationToken>()), Times.Once,
            "force skips the fresh cached row");
        _sourceMock.Verify(s => s.GetAnimeStreamingAsync(7, It.IsAny<CancellationToken>()), Times.Once,
            "a normal fetch queues a streaming refresh");
    }

    [Fact]
    public async Task RefreshAnimeDetailsAsync_FetchesFreshDetails_WithoutAStreamingRequest()
    {
        _db.Anime.Add(MakeCachedAnime(8, daysOld: 0, totalEpisodes: 10));
        await _db.SaveChangesAsync();

        _sourceMock
            .Setup(j => j.GetAnimeFullAsync(8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<AnimeDto>>.Success(new SingleResult<AnimeDto>
            {
                Data = new AnimeDto { MalId = 8, Title = "Refreshed Anime 8", Episodes = 12 }
            }));

        var result = await CreateService().RefreshAnimeDetailsAsync(8);
        await _background.WhenIdleAsync(); // a queued streaming refresh would run here

        result.IsSuccess.Should().BeTrue();
        result.Value!.Title.Should().Be("Refreshed Anime 8");
        _sourceMock.Verify(s => s.GetAnimeStreamingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never,
            "snapshot refreshes keep their own platforms (AM-119)");
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
        await _background.WhenIdleAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(42);
        result.Value.Title.Should().Be("Test Anime 42");

        await using var verify = await _factory.CreateDbContextAsync();
        (await verify.Anime.AsNoTracking().SingleAsync(a => a.Id == 42)).Title.Should().Be("Test Anime 42");
        _sourceMock.Verify(j => j.GetAnimeFullAsync(42, It.IsAny<CancellationToken>()), Times.Once);
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

        // Answered from the cache first: the stale title, not the refreshed one…
        result.IsSuccess.Should().BeTrue();
        result.Value!.Title.Should().Be(stale.Title);

        // …and refreshed behind it (AM-050: the test used to stop at the first half).
        await _background.WhenIdleAsync();
        _sourceMock.Verify(j => j.GetAnimeFullAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        await using var verify = await _factory.CreateDbContextAsync();
        (await verify.Anime.AsNoTracking().SingleAsync(a => a.Id == 5)).Title.Should().Be(dto.Title);
    }

    [Fact]
    public async Task GetAnimeAsync_FreshButLegacyRow_ServesItAndRefreshesInBackground()
    {
        // Cached by a version without extended metadata: still served instantly, but
        // refreshed so English title, themes, etc. appear on the next visit.
        var legacy = MakeCachedAnime(9, daysOld: 0);
        legacy.DataVersion = AniMan.Core.Domain.MediaDataVersion.Legacy;
        _db.Anime.Add(legacy);
        await _db.SaveChangesAsync();

        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sourceMock
            .Setup(j => j.GetAnimeFullAsync(9, It.IsAny<CancellationToken>()))
            .Callback(() => refreshed.TrySetResult())
            .ReturnsAsync(Result<SingleResult<AnimeDto>>.Success(
                new SingleResult<AnimeDto> { Data = new AnimeDto { MalId = 9, Title = "Cached Anime 9", TitleEnglish = "English 9" } }));
        _sourceMock
            .Setup(s => s.GetAnimeStreamingAsync(9, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(
                new SingleResult<IReadOnlyList<StreamingDto>> { Data = [] }));

        var result = await CreateService().GetAnimeAsync(9);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TitleEnglish.Should().BeNull("the cached row is served immediately");
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
    public async Task GetAnimeEpisodesAsync_StaleEpisodes_AnswersFromCacheAtOnce_AndRefreshesInBackground()
    {
        // The anime row is fresh (re-stamped by a season/top list), the episodes are not:
        // freshness must follow the episodes' own fetch time.
        _db.Anime.Add(MakeCachedAnime(90, daysOld: 0, totalEpisodes: 2));
        _db.Episodes.AddRange(
            new CachedEpisode { AnimeId = 90, EpisodeNumber = 1, Title = "Old 1", FetchedAt = DateTime.UtcNow.AddDays(-14) },
            new CachedEpisode { AnimeId = 90, EpisodeNumber = 2, Title = "Old 2", FetchedAt = DateTime.UtcNow.AddDays(-14) });
        await _db.SaveChangesAsync();

        var upstream = new TaskCompletionSource<Result<PagedResult<AnimeEpisodeDto>>>();
        _sourceMock
            .Setup(j => j.GetAnimeEpisodesAsync(90, 1, It.IsAny<CancellationToken>()))
            .Returns(upstream.Task);

        var result = await CreateService().GetAnimeEpisodesAsync(90);

        // Answered while the upstream call is still pending — nothing waits on the network.
        result.IsSuccess.Should().BeTrue();
        result.Value!.Select(e => e.Title).Should().Equal("Old 1", "Old 2");

        upstream.SetResult(Result<PagedResult<AnimeEpisodeDto>>.Success(new PagedResult<AnimeEpisodeDto>
        {
            Data = [new AnimeEpisodeDto { MalId = 1, Title = "New 1" }, new AnimeEpisodeDto { MalId = 2, Title = "New 2" }],
            Pagination = new PaginationDto { HasNextPage = false }
        }));

        await _background.WhenIdleAsync();
        await using var check = await _factory.CreateDbContextAsync();
        var titles = await check.Episodes.AsNoTracking().Where(e => e.AnimeId == 90)
            .OrderBy(e => e.EpisodeNumber).Select(e => e.Title).ToArrayAsync();
        titles.Should().Equal(["New 1", "New 2"], "the background refresh replaces the stale titles");
    }

    [Fact]
    public async Task GetAnimeEpisodesAsync_FreshEpisodesUnderAStaleAnimeRow_DoesNotRefetch()
    {
        _db.Anime.Add(MakeCachedAnime(91, daysOld: 30, totalEpisodes: 1));
        _db.Episodes.Add(new CachedEpisode { AnimeId = 91, EpisodeNumber = 1, Title = "Ep 1", FetchedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await CreateService().GetAnimeEpisodesAsync(91);
        await _background.WhenIdleAsync(); // a wrongly queued background refresh would have run by now

        result.Value!.Should().ContainSingle(e => e.Title == "Ep 1");
        _sourceMock.Verify(j => j.GetAnimeEpisodesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
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

    [Fact]
    public async Task GetAnimeStreamingAsync_ConcurrentCalls_ShareOneFetch()
    {
        var gate = new TaskCompletionSource<Result<SingleResult<IReadOnlyList<StreamingDto>>>>();
        _sourceMock
            .Setup(s => s.GetAnimeStreamingAsync(77, It.IsAny<CancellationToken>()))
            .Returns(gate.Task);

        var svc = CreateService();
        var first = svc.GetAnimeStreamingAsync(77, force: true);
        var second = svc.GetAnimeStreamingAsync(77, force: true);
        gate.SetResult(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(
            new SingleResult<IReadOnlyList<StreamingDto>> { Data = [] }));

        (await first).IsSuccess.Should().BeTrue();
        (await second).IsSuccess.Should().BeTrue();
        _sourceMock.Verify(s => s.GetAnimeStreamingAsync(77, It.IsAny<CancellationToken>()), Times.Once,
            "the second caller awaits the fetch already running instead of starting its own");
    }

    [Fact]
    public async Task GetAnimeStreamingAsync_FirstCallerCancelled_WaitingCallerFetchesItself()
    {
        var calls = 0;
        var firstFetch = new TaskCompletionSource<Result<SingleResult<IReadOnlyList<StreamingDto>>>>();
        _sourceMock
            .Setup(s => s.GetAnimeStreamingAsync(78, It.IsAny<CancellationToken>()))
            .Returns((int _, CancellationToken ct) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    ct.Register(() => firstFetch.TrySetCanceled(ct));
                    return firstFetch.Task;
                }
                return Task.FromResult(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(
                    new SingleResult<IReadOnlyList<StreamingDto>> { Data = [] }));
            });

        var svc = CreateService();
        using var leaving = new CancellationTokenSource();
        var first = svc.GetAnimeStreamingAsync(78, force: true, leaving.Token);
        var second = svc.GetAnimeStreamingAsync(78, force: true);

        await leaving.CancelAsync();

        await FluentActions.Awaiting(() => first).Should().ThrowAsync<OperationCanceledException>();
        (await second).IsSuccess.Should().BeTrue("its own page is still open");
        calls.Should().Be(2);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CachedAnime MakeCachedAnime(int id, int daysOld, int? totalEpisodes = null) => new()
    {
        Id = id,
        Title = $"Cached Anime {id}",
        FetchedAt = DateTime.UtcNow.AddDays(-daysOld),
        TotalEpisodes = totalEpisodes,
        DataVersion = AniMan.Core.Domain.MediaDataVersion.Current
    };

    private static AnimeDto MakeAnimeDto(int id) => new()
    {
        MalId = id,
        Title = $"Test Anime {id}"
    };

    public void Dispose()
    {
        // Ends fire-and-forget cache refreshes before the test database goes away.
        _background.StopAndWait(TimeSpan.FromSeconds(5));
        _background.Dispose();
        _db.Dispose();
        _factory.Dispose();
    }
}
