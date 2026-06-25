using AniTrack.Core.Domain.Enums;
using AniTrack.Core.Domain.Models;
using AniTrack.Infrastructure.Data;
using AniTrack.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniTrack.Tests.Services;

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
    public async Task GetTotalWatchTime_Is24MinutesPerWatchedEpisode()
    {
        AddItem(1, 100, MediaType.Anime, 1, null);
        _db.EpisodeProgress.AddRange(
            new EpisodeProgress { LibraryItemId = 1, EpisodeNumber = 1, IsWatched = true, WatchedAt = DateTime.UtcNow },
            new EpisodeProgress { LibraryItemId = 1, EpisodeNumber = 2, IsWatched = true, WatchedAt = DateTime.UtcNow },
            new EpisodeProgress { LibraryItemId = 1, EpisodeNumber = 3, IsWatched = true, WatchedAt = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await Create().GetTotalWatchTimeAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(TimeSpan.FromMinutes(3 * 24));
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

    public void Dispose() => _db.Dispose();
}
