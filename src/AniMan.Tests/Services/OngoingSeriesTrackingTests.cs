using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Services;
using AniMan.Tests.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniMan.Tests.Services;

/// <summary>
/// Audit B3: ongoing series often have no episode/chapter total. Progress must still save,
/// "?" stays the total, and a completion prompt must never fire against an unknown end.
/// </summary>
public class OngoingSeriesTrackingTests : IDisposable
{
    private const int MangaId = 1, AnimeId = 2;
    private readonly SqliteContextFactory<LibraryDbContext> _factory;

    public OngoingSeriesTrackingTests()
    {
        _factory = new SqliteContextFactory<LibraryDbContext>(opts => new LibraryDbContext(opts));
        using var db = _factory.CreateDbContext();
        var now = DateTime.UtcNow;
        db.LibraryItems.AddRange(
            new LibraryItem
            {
                Id = MangaId, MalId = 2, MediaType = MediaType.Manga, StatusId = 2, AddedAt = now, UpdatedAt = now,
                Snapshot = new MediaSnapshot { Title = "Berserk", Status = "Publishing", TotalChapters = null, Genres = "[]", SnapshotAt = now }
            },
            new LibraryItem
            {
                Id = AnimeId, MalId = 3, MediaType = MediaType.Anime, StatusId = 1, AddedAt = now, UpdatedAt = now,
                Snapshot = new MediaSnapshot { Title = "Airing", Status = "Currently Airing", TotalEpisodes = null, Genres = "[]", SnapshotAt = now }
            });
        db.SaveChanges();
    }

    private TrackingService Tracking() => new(_factory, NullLogger<TrackingService>.Instance);

    [Fact]
    public async Task JumpingToAChapter_MarksEveryChapterUpToIt_WithoutATotal()
    {
        var result = await Tracking().MarkChaptersUpToAsync(MangaId, 152);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AutoCompleteNeeded.Should().BeFalse("the series has no known last chapter");

        using var db = _factory.CreateDbContext();
        db.ChapterProgress.Where(c => c.LibraryItemId == MangaId && c.IsRead).Should().HaveCount(152);

        var progress = await Tracking().GetProgressAsync(MangaId);
        progress.Value.Should().Be(new AniMan.Core.Interfaces.ProgressInfo(152, 0), "0 is shown as \"?\"");
    }

    [Fact]
    public async Task JumpingBack_ClearsTheLaterChapters()
    {
        await Tracking().MarkChaptersUpToAsync(MangaId, 152);

        await Tracking().MarkChaptersUpToAsync(MangaId, 100);

        using var db = _factory.CreateDbContext();
        (await db.ChapterProgress.Where(c => c.LibraryItemId == MangaId).MaxAsync(c => c.ChapterNumber)).Should().Be(100);
    }

    [Fact]
    public async Task WatchingAnEpisode_OfAnAiringAnime_NeverAsksToComplete()
    {
        await Tracking().MarkUpToHereAsync(AnimeId, 4);

        var result = await Tracking().SetEpisodeWatchedAsync(AnimeId, 5, watched: true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AutoCompleteNeeded.Should().BeFalse();
    }

    public void Dispose() => _factory.Dispose();
}
