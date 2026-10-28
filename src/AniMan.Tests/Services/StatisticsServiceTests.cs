using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.Services;

public class StatisticsServiceTests : IDisposable
{
    private readonly LibraryDbContext _db;
    private readonly IDbContextFactory<LibraryDbContext> _factory;

    public StatisticsServiceTests()
    {
        var options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new LibraryDbContext(options);
        _db.Database.EnsureCreated();

        var factoryMock = new Mock<IDbContextFactory<LibraryDbContext>>();
        factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new LibraryDbContext(options));
        _factory = factoryMock.Object;
    }

    private StatisticsService Create() => new(_factory, NullLogger<StatisticsService>.Instance);

    private void AddItem(int id, int malId, MediaType type, int statusId, int? score, string genres = "[]")
    {
        _db.LibraryItems.Add(new LibraryItem
        {
            Id = id, MalId = malId, MediaType = type,
            StatusId = statusId, Score = score,
            AddedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        _db.Snapshots.Add(new MediaSnapshot
        {
            LibraryItemId = id, Title = $"Item {id}",
            Genres = genres, SnapshotAt = DateTime.UtcNow
        });
    }

    [Fact]
    public async Task GetTotalEpisodesWatched_CountsOnlyWatched()
    {
        AddItem(1, 100, MediaType.Anime, 1, null);
        _db.EpisodeProgress.AddRange(
            new EpisodeProgress { LibraryItemId = 1, EpisodeNumber = 1, IsWatched = true, WatchedAt = DateTime.UtcNow },
            new EpisodeProgress { LibraryItemId = 1, EpisodeNumber = 2, IsWatched = true, WatchedAt = DateTime.UtcNow },
            new EpisodeProgress { LibraryItemId = 1, EpisodeNumber = 3, IsWatched = false });
        await _db.SaveChangesAsync();

        var result = await Create().GetTotalEpisodesWatchedAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);
    }

    [Fact]
    public async Task GetTotalWatchTime_Is24MinutesPerWatchedEpisodeAndIncludesRewatches()
    {
        AddItem(1, 100, MediaType.Anime, 3, null);
        _db.LibraryItems.Local.Single(i => i.Id == 1).RewatchCount = 1;
        _db.Snapshots.Local.Single(s => s.LibraryItemId == 1).TotalEpisodes = 3;
        _db.EpisodeProgress.AddRange(
            new EpisodeProgress { LibraryItemId = 1, EpisodeNumber = 1, IsWatched = true, WatchedAt = DateTime.UtcNow },
            new EpisodeProgress { LibraryItemId = 1, EpisodeNumber = 2, IsWatched = true, WatchedAt = DateTime.UtcNow },
            new EpisodeProgress { LibraryItemId = 1, EpisodeNumber = 3, IsWatched = true, WatchedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await Create().GetTotalWatchTimeAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(TimeSpan.FromMinutes(6 * 24));
    }

    [Fact]
    public async Task GetTotalChaptersRead_CountsOnlyRead()
    {
        AddItem(1, 100, MediaType.Manga, 2, null);
        _db.ChapterProgress.AddRange(
            new ChapterProgress { LibraryItemId = 1, ChapterNumber = 1, IsRead = true, ReadAt = DateTime.UtcNow },
            new ChapterProgress { LibraryItemId = 1, ChapterNumber = 2, IsRead = false });
        await _db.SaveChangesAsync();

        var result = await Create().GetTotalChaptersReadAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
    }

    [Fact]
    public async Task GetMeanScore_AveragesScoredItemsOnly()
    {
        AddItem(1, 100, MediaType.Anime, 3, 8);
        AddItem(2, 101, MediaType.Anime, 3, 6);
        AddItem(3, 102, MediaType.Anime, 1, null); // unscored, ignored
        await _db.SaveChangesAsync();

        var result = await Create().GetMeanScoreAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(7.0);
    }

    [Fact]
    public async Task GetMeanScore_NoScores_ReturnsNull()
    {
        AddItem(1, 100, MediaType.Anime, 1, null);
        await _db.SaveChangesAsync();

        var result = await Create().GetMeanScoreAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusBreakdown_GroupsAndOrdersByCountDescending()
    {
        AddItem(1, 100, MediaType.Anime, 3, null); // Completed
        AddItem(2, 101, MediaType.Anime, 3, null); // Completed
        AddItem(3, 102, MediaType.Anime, 1, null); // Watching
        await _db.SaveChangesAsync();

        var result = await Create().GetStatusBreakdownAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        result.Value![0].StatusName.Should().Be("Completed");
        result.Value![0].Count.Should().Be(2);
        result.Value![1].StatusName.Should().Be("Watching");
        result.Value![1].Count.Should().Be(1);
    }

    [Fact]
    public async Task GetScoreHistogram_FillsAllTenBucketsWithZeroForMissing()
    {
        AddItem(1, 100, MediaType.Anime, 3, 8);
        AddItem(2, 101, MediaType.Anime, 3, 8);
        AddItem(3, 102, MediaType.Anime, 3, 5);
        await _db.SaveChangesAsync();

        var result = await Create().GetScoreHistogramAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(10);
        result.Value![8].Should().Be(2);
        result.Value![5].Should().Be(1);
        result.Value![1].Should().Be(0);
    }

    [Fact]
    public async Task GetTopGenres_AggregatesAcrossSnapshotsAndLimitsToN()
    {
        AddItem(1, 100, MediaType.Anime, 1, null, """["Action","Comedy"]""");
        AddItem(2, 101, MediaType.Anime, 1, null, """["Action","Drama"]""");
        AddItem(3, 102, MediaType.Anime, 1, null, """["Action"]""");
        await _db.SaveChangesAsync();

        var result = await Create().GetTopGenresAsync(2);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        result.Value![0].Should().Be(("Action", 3));
    }

    [Fact]
    public async Task GetTopGenres_MalformedJson_IsSkipped()
    {
        AddItem(1, 100, MediaType.Anime, 1, null, "not-json");
        AddItem(2, 101, MediaType.Anime, 1, null, """["Action"]""");
        await _db.SaveChangesAsync();

        var result = await Create().GetTopGenresAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle()
            .Which.Should().Be(("Action", 1));
    }

    [Fact]
    public async Task GetMonthlyActivity_GroupsByYearMonthAndExcludesOlderThanWindow()
    {
        AddItem(1, 100, MediaType.Anime, 1, null);
        var now = DateTime.UtcNow;
        _db.EpisodeProgress.AddRange(
            new EpisodeProgress { LibraryItemId = 1, EpisodeNumber = 1, IsWatched = true, WatchedAt = now },
            new EpisodeProgress { LibraryItemId = 1, EpisodeNumber = 2, IsWatched = true, WatchedAt = now },
            new EpisodeProgress { LibraryItemId = 1, EpisodeNumber = 3, IsWatched = true, WatchedAt = now.AddMonths(-24) });
        await _db.SaveChangesAsync();

        var result = await Create().GetMonthlyActivityAsync(12);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle();
        result.Value![0].Year.Should().Be(now.Year);
        result.Value![0].Month.Should().Be(now.Month);
        result.Value![0].Count.Should().Be(2);
    }

    [Fact]
    public async Task GetMonthlyActivity_IncludesChaptersAndExcludesSoftDeletedItems()
    {
        AddItem(1, 100, MediaType.Anime, 1, 8);
        AddItem(2, 200, MediaType.Manga, 2, 9);
        AddItem(3, 300, MediaType.Anime, 1, 10);
        var trashed = _db.LibraryItems.Local.Single(i => i.Id == 3);
        trashed.DeletedAt = DateTime.UtcNow;

        var now = DateTime.UtcNow;
        _db.EpisodeProgress.AddRange(
            new EpisodeProgress { LibraryItemId = 1, EpisodeNumber = 1, IsWatched = true, WatchedAt = now },
            new EpisodeProgress { LibraryItemId = 3, EpisodeNumber = 1, IsWatched = true, WatchedAt = now });
        _db.ChapterProgress.Add(
            new ChapterProgress { LibraryItemId = 2, ChapterNumber = 1, IsRead = true, ReadAt = now });
        await _db.SaveChangesAsync();

        var svc = Create();
        var activity = await svc.GetMonthlyActivityAsync(12);
        activity.IsSuccess.Should().BeTrue();
        activity.Value!.Should().ContainSingle().Which.Count.Should().Be(2);

        var mean = await svc.GetMeanScoreAsync();
        mean.Value.Should().Be(8.5);
    }

    [Fact]
    public async Task GetMeanScoreAndHistogram_ReadsUserRatingWithFallbackToLegacyScore()
    {
        AddItem(1, 100, MediaType.Anime, 3, null);
        _db.LibraryItems.Local.Single(i => i.Id == 1).UserRating = 8.5m;
        AddItem(2, 101, MediaType.Anime, 3, 7); // legacy Score fallback
        await _db.SaveChangesAsync();

        var svc = Create();
        var mean = await svc.GetMeanScoreAsync();
        mean.IsSuccess.Should().BeTrue();
        mean.Value.Should().Be(7.75);

        var hist = await svc.GetScoreHistogramAsync();
        hist.IsSuccess.Should().BeTrue();
        hist.Value![9].Should().Be(1); // 8.5 rounds AwayFromZero to 9
        hist.Value![7].Should().Be(1);
    }

    public void Dispose() => _db.Dispose();
}
