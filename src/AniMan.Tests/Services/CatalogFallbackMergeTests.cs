using AniMan.Core.Common;
using AniMan.Core.Domain;
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

/// <summary>
/// When the primary source is down the fallback (AniList) answers with thinner data:
/// no MAL rank, themes, demographics or age rating, and its own score. These tests pin
/// that such data only ever fills gaps and that a thin answer never erases stored data.
/// </summary>
[Collection(CatalogStateCollection.Name)]
public sealed class CatalogFallbackMergeTests : IDisposable
{
    private readonly SqliteContextFactory<CatalogDbContext> _factory;
    private readonly Mock<IMediaSourceClient> _source = new();
    private readonly Mock<ISettingsService> _settings = new();
    private readonly BackgroundWork _background = new(NullLogger<BackgroundWork>.Instance);

    /// <summary>Ends fire-and-forget cache refreshes before the test database goes away.</summary>
    private void StopBackground()
    {
        _background.StopAndWait(TimeSpan.FromSeconds(5));
        _background.Dispose();
    }

    public CatalogFallbackMergeTests()
    {
        CatalogService.ClearCaches();
        _factory = new SqliteContextFactory<CatalogDbContext>(opts => new CatalogDbContext(opts));
        _settings.Setup(s => s.GetCacheRefreshDaysAsync(It.IsAny<CancellationToken>())).ReturnsAsync(7);
        // Streaming refreshes are queued after every anime write; keep them quiet.
        _source.Setup(s => s.GetAnimeStreamingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<IReadOnlyList<StreamingDto>>>.Failure("n/a"));
    }

    private CatalogService Create() => new(_factory, _source.Object, _settings.Object, _background, NullLogger<CatalogService>.Instance);

    private void SeedCompleteAnime(int id)
    {
        using var db = _factory.CreateDbContext();
        db.Anime.Add(new CachedAnime
        {
            Id = id, Title = "MAL Title", Score = 8.9, Rank = 42, Popularity = 100,
            Themes = """["Gore"]""", Demographics = """["Seinen"]""", AgeRating = "R - 17+",
            TotalEpisodes = 25, DataVersion = MediaDataVersion.Current, FetchedAt = DateTime.UtcNow.AddDays(-30)
        });
        db.SaveChanges();
    }

    private static AnimeDto FallbackDto(int id) => new()
    {
        IsPartial = true, MalId = id, Title = "AniList Title", Score = 7.5, Episodes = 26
    };

    private async Task<CachedAnime> StoredAsync(int id)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await db.Anime.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    [Fact]
    public async Task FallbackFullRecord_OverACompleteRow_FillsNothingAndKeepsMalData()
    {
        SeedCompleteAnime(1);
        _source.Setup(s => s.GetAnimeFullAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<AnimeDto>>.Success(new SingleResult<AnimeDto> { Data = FallbackDto(1) }));

        var result = await Create().GetAnimeAsync(1, force: true);

        var stored = await StoredAsync(1);
        stored.Title.Should().Be("MAL Title");
        stored.Score.Should().Be(8.9, "an AniList average must never replace the MAL score");
        stored.Rank.Should().Be(42);
        stored.Themes.Should().Be("""["Gore"]""");
        stored.AgeRating.Should().Be("R - 17+");
        stored.DataVersion.Should().Be(MediaDataVersion.Current);

        // Callers (snapshots) get the complete stored row, not the thin answer.
        result.Value!.Score.Should().Be(8.9);
        result.Value.Title.Should().Be("MAL Title");
    }

    [Fact]
    public async Task FallbackRecord_WithNothingCached_IsStoredAsPartial()
    {
        _source.Setup(s => s.GetAnimeFullAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SingleResult<AnimeDto>>.Success(new SingleResult<AnimeDto> { Data = FallbackDto(2) }));

        await Create().GetAnimeAsync(2);

        // Guards the EF sentinel trap: a version of 0 would have been stored as the column default (Legacy).
        (await StoredAsync(2)).DataVersion.Should().Be(MediaDataVersion.Partial);
    }

    [Fact]
    public async Task PrimaryListEntry_WithMissingFields_DoesNotBlankStoredOnes()
    {
        SeedCompleteAnime(3);
        _source.Setup(s => s.GetTopAnimeAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeDto>>.Success(new PagedResult<AnimeDto>
            {
                // A primary-source list entry (not partial) that happens to omit themes and rank.
                Data = [new AnimeDto { MalId = 3, Title = "MAL Title (new)", Score = 9.0 }],
                Pagination = new PaginationDto { HasNextPage = true }
            }));

        await Create().GetTopAnimeAsync();

        var stored = await StoredAsync(3);
        stored.Title.Should().Be("MAL Title (new)", "primary data still updates what it sends");
        stored.Score.Should().Be(9.0);
        stored.Themes.Should().Be("""["Gore"]""", "a field the source did not send is not erased");
        stored.Rank.Should().Be(42);
    }

    [Fact]
    public async Task FallbackListEntry_OverACompleteRow_KeepsMalData()
    {
        SeedCompleteAnime(4);
        _source.Setup(s => s.GetTopAnimeAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeDto>>.Success(new PagedResult<AnimeDto>
            {
                Data = [FallbackDto(4)],
                Pagination = new PaginationDto { HasNextPage = false }
            }));

        var result = await Create().GetTopAnimeAsync();

        (await StoredAsync(4)).Score.Should().Be(8.9);
        result.Value!.Items.Single().Score.Should().Be(8.9, "the list shows the stored MAL row");
    }

    [Fact]
    public async Task TopList_CarriesTheSourcesNextPageFlag()
    {
        _source.Setup(s => s.GetTopAnimeAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PagedResult<AnimeDto>>.Success(new PagedResult<AnimeDto>
            {
                // Fewer than a full page (entries without a MAL id were dropped) — still not the last page.
                Data = [new AnimeDto { MalId = 5, Title = "Only one" }],
                Pagination = new PaginationDto { HasNextPage = true }
            }));

        var result = await Create().GetTopAnimeAsync();

        result.Value!.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task EmptyEpisodeAnswer_KeepsCachedTitledEpisodes()
    {
        await using (var db = await _factory.CreateDbContextAsync())
        {
            db.Anime.Add(new CachedAnime { Id = 6, Title = "Show", TotalEpisodes = 2, DataVersion = MediaDataVersion.Current, FetchedAt = DateTime.UtcNow });
            db.Episodes.AddRange(
                new CachedEpisode { AnimeId = 6, EpisodeNumber = 1, Title = "Real 1", FetchedAt = DateTime.UtcNow.AddDays(-30) },
                new CachedEpisode { AnimeId = 6, EpisodeNumber = 2, Title = "Real 2", FetchedAt = DateTime.UtcNow.AddDays(-30) });
            await db.SaveChangesAsync();
        }

        var refreshed = new TaskCompletionSource();
        _source.Setup(s => s.GetAnimeEpisodesAsync(6, 1, It.IsAny<CancellationToken>()))
            .Callback(() => refreshed.TrySetResult())
            .ReturnsAsync(Result<PagedResult<AnimeEpisodeDto>>.Success(new PagedResult<AnimeEpisodeDto>
            {
                Data = [], // what the fallback source always answers
                Pagination = new PaginationDto { HasNextPage = false }
            }));

        await Create().GetAnimeEpisodesAsync(6); // stale → background refresh
        (await Task.WhenAny(refreshed.Task, Task.Delay(TimeSpan.FromSeconds(5)))).Should().Be(refreshed.Task);
        await Task.Delay(200); // let the refresh finish its (non-)write

        await using var verify = await _factory.CreateDbContextAsync();
        (await verify.Episodes.Where(e => e.AnimeId == 6).OrderBy(e => e.EpisodeNumber).Select(e => e.Title).ToListAsync())
            .Should().Equal("Real 1", "Real 2");
    }

    public void Dispose()
    {
        StopBackground();
        _factory.Dispose();
    }
}
