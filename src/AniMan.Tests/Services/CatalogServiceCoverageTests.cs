using AniMan.Core.Common;
using AniMan.Core.Domain;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.MediaSource;
using AniMan.Infrastructure.MediaSource.Dtos;
using AniMan.Infrastructure.Services;
using AniMan.Tests.Integration;
using AniMan.Tests.TestDoubles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.Services;

/// <summary>
/// AM-131: CatalogService paths the other catalog tests leave out — multi-page and cut-off
/// episode lists, placeholder episodes, the manga cache, and duplicate streaming rows.
/// </summary>
[Collection(CatalogStateCollection.Name)]
public sealed class CatalogServiceCoverageTests : IDisposable
{
    private readonly SqliteContextFactory<CatalogDbContext> _factory = new(o => new CatalogDbContext(o));
    private readonly Mock<IMediaSourceClient> _source = new(MockBehavior.Strict);
    private readonly Mock<ISettingsService> _settings = new();
    private readonly BackgroundWork _background = new(NullLogger<BackgroundWork>.Instance);

    public CatalogServiceCoverageTests()
    {
        CatalogService.ClearCaches();
        _settings.Setup(s => s.GetCacheRefreshDaysAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);
    }

    private CatalogService Create() => new(
        _factory, _source.Object, _settings.Object, _background, NullLogger<CatalogService>.Instance);

    private async Task SeedAsync(Action<CatalogDbContext> seed)
    {
        await using var db = await _factory.CreateDbContextAsync();
        seed(db);
        await db.SaveChangesAsync();
    }

    private async Task<List<CachedEpisode>> StoredEpisodesAsync(int animeId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Episodes.AsNoTracking().Where(e => e.AnimeId == animeId).OrderBy(e => e.EpisodeNumber).ToListAsync();
    }

    private static CachedAnime Anime(int id, int? total) => new()
    {
        Id = id, Title = $"Anime {id}", TotalEpisodes = total, FetchedAt = DateTime.UtcNow, DataVersion = MediaDataVersion.Current
    };

    private void EpisodePage(int animeId, int page, bool hasNext, params int[] numbers) =>
        _source.Setup(s => s.GetAnimeEpisodesAsync(animeId, page, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeEpisodeDto>>.Success(new PagedResult<AnimeEpisodeDto>
            {
                Data = numbers.Select(n => new AnimeEpisodeDto { MalId = n, Title = $"Ep {n}" }).ToList(),
                Pagination = new PaginationDto { HasNextPage = hasNext }
            }));

    private void EpisodePageFails(int animeId, int page) =>
        _source.Setup(s => s.GetAnimeEpisodesAsync(animeId, page, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeEpisodeDto>>.Failure("504"));

    // ── Episodes ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Episodes_SpanningPages_AreJoinedSortedAndDeduplicated()
    {
        await SeedAsync(db => db.Anime.Add(Anime(1, total: 4)));
        EpisodePage(1, 1, hasNext: true, 2, 1, 0);   // 0 is not a real episode number
        EpisodePage(1, 2, hasNext: false, 2, 3, 4);  // 2 repeated across pages

        var result = await Create().GetAnimeEpisodesAsync(1);

        result.Value!.Select(e => e.EpisodeNumber).Should().Equal(1, 2, 3, 4);
        (await StoredEpisodesAsync(1)).Select(e => e.EpisodeNumber).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task Episodes_CutOffMidway_WithNothingCached_AreShownButNotCached()
    {
        // A half list in the cache would look complete and never be fetched again.
        await SeedAsync(db => db.Anime.Add(Anime(2, total: 200)));
        EpisodePage(2, 1, hasNext: true, 1, 2);
        EpisodePageFails(2, 2);

        var result = await Create().GetAnimeEpisodesAsync(2);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Select(e => e.EpisodeNumber).Should().Equal(1, 2);
        (await StoredEpisodesAsync(2)).Should().BeEmpty();
    }

    [Fact]
    public async Task Episodes_EmptyLaterPage_BeforeTheTotal_CountsAsCutOff()
    {
        await SeedAsync(db => db.Anime.Add(Anime(3, total: 5)));
        EpisodePage(3, 1, hasNext: true, 1, 2);
        EpisodePage(3, 2, hasNext: false); // the fallback source answers later pages empty

        var result = await Create().GetAnimeEpisodesAsync(3);

        result.Value!.Should().HaveCount(2);
        (await StoredEpisodesAsync(3)).Should().BeEmpty();
    }

    [Fact]
    public async Task Episodes_RefreshCutOffMidway_KeepsTheCachedList()
    {
        await SeedAsync(db =>
        {
            db.Anime.Add(Anime(4, total: 3));
            db.Episodes.AddRange(Enumerable.Range(1, 3).Select(n => new CachedEpisode
            {
                AnimeId = 4, EpisodeNumber = n, Title = $"Cached {n}", FetchedAt = DateTime.UtcNow.AddDays(-30)
            }));
        });
        EpisodePage(4, 1, hasNext: true, 1);
        EpisodePageFails(4, 2);

        var result = await Create().GetAnimeEpisodesAsync(4); // stale → background refresh
        await _background.WhenIdleAsync();

        result.Value!.Select(e => e.Title).Should().Equal("Cached 1", "Cached 2", "Cached 3");
        (await StoredEpisodesAsync(4)).Select(e => e.Title).Should().Equal("Cached 1", "Cached 2", "Cached 3");
    }

    [Fact]
    public async Task Episodes_SourceHasNone_ButTheTotalIsKnown_GetPlaceholders()
    {
        await SeedAsync(db => db.Anime.Add(Anime(5, total: 3)));
        EpisodePage(5, 1, hasNext: false);

        var result = await Create().GetAnimeEpisodesAsync(5);

        result.Value!.Select(e => (e.EpisodeNumber, e.Title)).Should().Equal((1, null), (2, null), (3, null));
        (await StoredEpisodesAsync(5)).Should().HaveCount(3);
    }

    // ── Manga ────────────────────────────────────────────────────────────────

    private void MangaFromSource(int id, string title) =>
        _source.Setup(s => s.GetMangaFullAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<MangaDto>>.Success(new SingleResult<MangaDto>
            {
                Data = new MangaDto { MalId = id, Title = title, Chapters = 380 }
            }));

    private static CachedManga Manga(int id, int daysOld) => new()
    {
        Id = id, Title = $"Cached manga {id}", FetchedAt = DateTime.UtcNow.AddDays(-daysOld), DataVersion = MediaDataVersion.Current
    };

    [Fact]
    public async Task Manga_FreshCacheHit_DoesNotCallTheSource()
    {
        await SeedAsync(db => db.Manga.Add(Manga(10, daysOld: 0)));

        var result = await Create().GetMangaAsync(10);
        await _background.WhenIdleAsync();

        result.Value!.Title.Should().Be("Cached manga 10");
        _source.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Manga_CacheMiss_FetchesAndStores()
    {
        MangaFromSource(11, "Berserk");

        var result = await Create().GetMangaAsync(11);

        result.Value!.Title.Should().Be("Berserk");
        await using var db = await _factory.CreateDbContextAsync();
        (await db.Manga.SingleAsync(m => m.Id == 11)).TotalChapters.Should().Be(380);
    }

    [Fact]
    public async Task Manga_StaleCache_IsServedAtOnce_AndRefreshedBehindIt()
    {
        await SeedAsync(db => db.Manga.Add(Manga(12, daysOld: 30)));
        MangaFromSource(12, "Refreshed");

        var result = await Create().GetMangaAsync(12);
        result.Value!.Title.Should().Be("Cached manga 12");

        await _background.WhenIdleAsync();
        await using var db = await _factory.CreateDbContextAsync();
        (await db.Manga.SingleAsync(m => m.Id == 12)).Title.Should().Be("Refreshed");
    }

    [Fact]
    public async Task Manga_Force_SkipsAFreshCache()
    {
        await SeedAsync(db => db.Manga.Add(Manga(13, daysOld: 0)));
        MangaFromSource(13, "Forced");

        (await Create().GetMangaAsync(13, force: true)).Value!.Title.Should().Be("Forced");
    }

    [Fact]
    public async Task Manga_InvalidId_FailsWithoutCallingTheSource()
    {
        (await Create().GetMangaAsync(0)).IsSuccess.Should().BeFalse();
        _source.VerifyNoOtherCalls();
    }

    // ── Streaming ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Streaming_DuplicateAndBlankNames_AreStoredOnce()
    {
        await SeedAsync(db => db.Anime.Add(Anime(20, total: 12)));
        _source.Setup(s => s.GetAnimeStreamingAsync(20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Success(new SingleResult<IReadOnlyList<StreamingDto>>
            {
                Data =
                [
                    new StreamingDto { Name = "Crunchyroll", Url = "https://www.crunchyroll.com/a" },
                    new StreamingDto { Name = "crunchyroll", Url = "https://www.crunchyroll.com/b" },
                    new StreamingDto { Name = "  ", Url = "https://example.com" },
                    new StreamingDto { Name = "Netflix", Url = "https://www.netflix.com/title/1" }
                ]
            }));

        var result = await Create().GetAnimeStreamingAsync(20, force: true);

        result.Value!.Select(s => s.PlatformName).Should().Equal("Crunchyroll", "Netflix");
        await using var db = await _factory.CreateDbContextAsync();
        (await db.AnimeStreaming.Where(s => s.AnimeId == 20).Select(s => s.PlatformName).ToListAsync())
            .Should().BeEquivalentTo(["Crunchyroll", "Netflix"]);
    }

    public void Dispose()
    {
        _background.StopAndWait(TimeSpan.FromSeconds(5));
        _background.Dispose();
        _factory.Dispose();
    }
}
