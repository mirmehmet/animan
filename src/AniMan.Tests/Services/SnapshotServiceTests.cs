using AniMan.Core.Common;
using AniMan.Core.Domain;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Services;
using AniMan.Tests.Integration;
using AniMan.Tests.TestDoubles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.Services;

public class SnapshotServiceTests : IDisposable
{
    private readonly SqliteContextFactory<LibraryDbContext> _libraryFactory;
    private readonly SqliteContextFactory<CatalogDbContext> _catalogFactory;
    private readonly LibraryDbContext _libraryDb;
    private readonly Mock<ICatalogService> _catalogServiceMock;
    private readonly TestStorage _storage = new();
    /// <summary>Offline unless a test swaps in an answering handler; never real network.</summary>
    private StubHttpHandler _http = new();

    public SnapshotServiceTests()
    {
        _libraryFactory = new SqliteContextFactory<LibraryDbContext>(opts => new LibraryDbContext(opts));
        _catalogFactory = new SqliteContextFactory<CatalogDbContext>(opts => new CatalogDbContext(opts));
        _libraryDb = _libraryFactory.CreateDbContext();

        _catalogServiceMock = new Mock<ICatalogService>();
    }

    private SnapshotService CreateService() => new(
        _libraryFactory, _catalogFactory,
        _catalogServiceMock.Object,
        new CoverStore(_http.Factory(), _storage.Paths, NullLogger<CoverStore>.Instance),
        NullLogger<SnapshotService>.Instance);

    [Fact]
    public async Task SnapshotAsync_Success_CreatesLibraryItemAndSnapshot()
    {
        SetupAnime(5, "Steins;Gate");
        _http = StubHttpHandler.Returning();

        var svc = CreateService();
        var result = await svc.SnapshotAsync(5, MediaType.Anime, 1);

        result.IsSuccess.Should().BeTrue();
        result.Value!.MalId.Should().Be(5);
        result.Value.StartedAt.Should().NotBeNull();
        result.Value.Snapshot!.Title.Should().Be("Steins;Gate");

        _libraryDb.LibraryItems.Should().HaveCount(1);
        _libraryDb.Snapshots.Should().HaveCount(1);
        result.Value.Snapshot.CoverLocalPath.Should().Be(Path.Combine(_storage.Paths.CoversPath, "5_anime.jpg"));
        File.Exists(result.Value.Snapshot.CoverLocalPath).Should().BeTrue();
    }

    [Fact]
    public async Task SnapshotAsync_CoverDownloadFails_SnapshotWrittenWithNullPath()
    {
        SetupAnime(10, "Naruto");

        var svc = CreateService(); // offline handler: the download is attempted and fails

        var result = await svc.SnapshotAsync(10, MediaType.Anime, 1);

        result.IsSuccess.Should().BeTrue();
        _http.Requests.Should().ContainSingle("the download must really be attempted (AM-049)")
            .Which.AbsoluteUri.Should().Be(CoverUrl(10));
        var snapshot = _libraryDb.Snapshots.Single();
        snapshot.CoverLocalPath.Should().BeNull();
        snapshot.CoverOriginalUrl.Should().Be(CoverUrl(10), "the URL is kept so the cover can be fetched later");
    }

    [Fact]
    public async Task SnapshotAsync_DuplicateAdd_ReturnsFailure()
    {
        SetupAnime(20, "FMA");

        var svc = CreateService();
        await svc.SnapshotAsync(20, MediaType.Anime, 1);

        var second = await svc.SnapshotAsync(20, MediaType.Anime, 1);
        second.IsSuccess.Should().BeFalse();
        second.Error.Should().Contain("already in your library");
    }

    [Fact]
    public async Task SnapshotAsync_SoftDeletedItem_RestoresAndPreservesProgressRatingAndNotes()
    {
        SetupAnime(25, "Cowboy Bebop");

        var svc = CreateService();
        var first = await svc.SnapshotAsync(25, MediaType.Anime, 1);
        first.IsSuccess.Should().BeTrue();

        // Add user progress, rating, and note, then soft-delete to Trash
        using (var db = await _libraryFactory.CreateDbContextAsync())
        {
            var item = await db.LibraryItems.SingleAsync(i => i.MalId == 25);
            item.UserRating = 9.5m;
            db.EpisodeProgress.Add(new EpisodeProgress
            {
                LibraryItemId = item.Id, EpisodeNumber = 1, IsWatched = true, WatchedAt = DateTime.UtcNow
            });
            db.Notes.Add(new Note
            {
                LibraryItemId = item.Id, Content = "Masterpiece", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            item.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        // Re-adding the soft-deleted item restores it with preserved user data
        var reAdded = await svc.SnapshotAsync(25, MediaType.Anime, 6);
        reAdded.IsSuccess.Should().BeTrue();
        reAdded.Value!.DeletedAt.Should().BeNull();
        reAdded.Value.StatusId.Should().Be(6);

        using (var verify = await _libraryFactory.CreateDbContextAsync())
        {
            var restored = await verify.LibraryItems
                .Include(i => i.EpisodeProgress)
                .Include(i => i.Notes)
                .SingleAsync(i => i.MalId == 25);
            restored.UserRating.Should().Be(9.5m);
            restored.EpisodeProgress.Should().ContainSingle(e => e.EpisodeNumber == 1 && e.IsWatched);
            restored.Notes.Should().ContainSingle(n => n.Content == "Masterpiece");
        }
    }

    [Fact]
    public async Task SnapshotAsync_SoftDeletedItem_CatalogFetchFails_PreservesTrashedItem()
    {
        SetupAnime(30, "Trigun");

        var svc = CreateService();
        await svc.SnapshotAsync(30, MediaType.Anime, 1);

        using (var db = await _libraryFactory.CreateDbContextAsync())
        {
            var item = await db.LibraryItems.SingleAsync(i => i.MalId == 30);
            item.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        _catalogServiceMock
            .Setup(c => c.GetAnimeAsync(30, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CachedAnime>.Failure("Offline"));

        var attempt = await svc.SnapshotAsync(30, MediaType.Anime, 1);
        attempt.IsSuccess.Should().BeFalse();

        using (var verify = await _libraryFactory.CreateDbContextAsync())
        {
            verify.LibraryItems.Should().ContainSingle(i => i.MalId == 30 && i.DeletedAt != null);
        }
    }

    [Fact]
    public async Task ReSnapshotAsync_UsesForceRefreshAndPreservesExistingCoverWhenDownloadFails()
    {
        var coverPath = Path.Combine(_storage.Paths.CoversPath, "50_anime.jpg");
        await File.WriteAllBytesAsync(coverPath, [1, 2, 3]);

        using (var db = await _libraryFactory.CreateDbContextAsync())
        {
            db.LibraryItems.Add(new LibraryItem
            {
                Id = 50,
                MalId = 50,
                MediaType = MediaType.Anime,
                StatusId = 1,
                AddedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Snapshot = new MediaSnapshot
                {
                    LibraryItemId = 50,
                    Title = "Old Title",
                    TotalEpisodes = 10,
                    CoverLocalPath = coverPath,
                    Genres = "[]",
                    SnapshotAt = DateTime.UtcNow.AddDays(-5)
                }
            });
            await db.SaveChangesAsync();
        }

        _catalogServiceMock
            .Setup(c => c.RefreshAnimeDetailsAsync(50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CachedAnime>.Success(new CachedAnime
            {
                Id = 50,
                Title = "Updated Title",
                TotalEpisodes = 12,
                CoverLargeUrl = "https://example.com/50.jpg",
                FetchedAt = DateTime.UtcNow
            }));

        var svc = CreateService();
        var refreshed = await svc.ReSnapshotAsync(50);

        refreshed.IsSuccess.Should().BeTrue();
        refreshed.Value!.Title.Should().Be("Updated Title");
        refreshed.Value.TotalEpisodes.Should().Be(12);
        refreshed.Value.CoverLocalPath.Should().Be(coverPath);
        _catalogServiceMock.Verify(c => c.RefreshAnimeDetailsAsync(50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReSnapshotAsync_FallbackData_NeverOverwritesACompleteSnapshot()
    {
        var itemId = SeedLegacyItem(60, MediaType.Anime, MediaDataVersion.Current);
        await using (var db = await _libraryFactory.CreateDbContextAsync())
        {
            var snap = await db.Snapshots.SingleAsync(s => s.LibraryItemId == itemId);
            snap.MalScore = 8.9;
            snap.Themes = """["Gore"]""";
            await db.SaveChangesAsync();
        }

        _catalogServiceMock
            .Setup(c => c.RefreshAnimeDetailsAsync(60, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CachedAnime>.Success(new CachedAnime
            {
                Id = 60, Title = "AniList Title", Score = 7.5, TotalEpisodes = 26,
                DataVersion = MediaDataVersion.Partial, FetchedAt = DateTime.UtcNow
            }));

        var result = await CreateService().ReSnapshotAsync(itemId);

        result.IsSuccess.Should().BeTrue();
        await using var verify = await _libraryFactory.CreateDbContextAsync();
        var stored = await verify.Snapshots.SingleAsync(s => s.LibraryItemId == itemId);
        stored.Title.Should().Be("Shingeki no Kyojin");
        stored.MalScore.Should().Be(8.9);
        stored.Themes.Should().Be("""["Gore"]""");
        stored.Genres.Should().Be("""["Action"]""");
        stored.DataVersion.Should().Be(MediaDataVersion.Current);
        stored.TotalEpisodes.Should().Be(26, "a gap the snapshot had is still filled");
    }

    [Fact]
    public async Task FillExtendedMetadata_FallbackData_AddsOnlyWhatIsMissing_AndStaysIncomplete()
    {
        var itemId = SeedLegacyItem(61, MediaType.Anime);
        _catalogServiceMock
            .Setup(c => c.RefreshAnimeDetailsAsync(61, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CachedAnime>.Success(new CachedAnime
            {
                Id = 61, Title = "x", TitleEnglish = "Attack on Titan", DurationMinutes = 24,
                DataVersion = MediaDataVersion.Partial, FetchedAt = DateTime.UtcNow
            }));

        var result = await CreateService().FillExtendedMetadataAsync(itemId);

        result.IsSuccess.Should().BeFalse("only partial data was available; the backfill should count it as not done");
        await using var verify = await _libraryFactory.CreateDbContextAsync();
        var stored = await verify.Snapshots.SingleAsync(s => s.LibraryItemId == itemId);
        stored.TitleEnglish.Should().Be("Attack on Titan");
        stored.DurationMinutes.Should().Be(24);
        stored.DataVersion.Should().BeLessThan(MediaDataVersion.Current, "it is completed later from the primary source");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // ── Extended metadata ─────────────────────────────────────────────────────

    [Fact]
    public async Task SnapshotAsync_FromCurrentCatalogRow_CopiesExtendedMetadata()
    {
        _catalogServiceMock
            .Setup(c => c.GetAnimeAsync(16498, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CachedAnime>.Success(new CachedAnime
            {
                Id = 16498, Title = "Shingeki no Kyojin", FetchedAt = DateTime.UtcNow,
                TitleEnglish = "Attack on Titan", Themes = """["Military"]""", Demographics = """["Shounen"]""",
                DurationMinutes = 24, AgeRating = "R - 17+", Source = "Manga",
                DataVersion = MediaDataVersion.Current
            }));

        var result = await CreateService().SnapshotAsync(16498, MediaType.Anime, 1);

        result.IsSuccess.Should().BeTrue();
        var snap = result.Value!.Snapshot!;
        snap.TitleEnglish.Should().Be("Attack on Titan");
        snap.Themes.Should().Be("""["Military"]""");
        snap.DurationMinutes.Should().Be(24);
        snap.Source.Should().Be("Manga");
        snap.DataVersion.Should().Be(MediaDataVersion.Current);
    }

    [Fact]
    public async Task SnapshotAsync_FromLegacyCatalogRow_StaysLegacySoBackfillCompletesIt()
    {
        // A catalog row cached before the update has no extended fields; marking the new
        // snapshot Current would hide it from the backfill forever.
        SetupAnime(5, "Steins;Gate");

        var result = await CreateService().SnapshotAsync(5, MediaType.Anime, 1);

        result.Value!.Snapshot!.DataVersion.Should().Be(MediaDataVersion.Legacy);
    }

    [Fact]
    public async Task FillExtendedMetadata_LegacySnapshot_FillsOnlyNewFieldsAndGenres()
    {
        var itemId = SeedLegacyItem(16498, MediaType.Anime);
        SeedCatalogGenres(16498, "anime", (1, "Action"), (9, "Ecchi"));
        _catalogServiceMock
            .Setup(c => c.RefreshAnimeDetailsAsync(16498, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CachedAnime>.Success(new CachedAnime
            {
                Id = 16498, Title = "A different upstream title", Synopsis = "Different synopsis",
                FetchedAt = DateTime.UtcNow,
                TitleEnglish = "Attack on Titan", Themes = """["Gore","Military"]""",
                Demographics = """["Shounen"]""", DurationMinutes = 24,
                AgeRating = "R - 17+ (violence & profanity)", Source = "Manga",
                DataVersion = MediaDataVersion.Current
            }));

        var result = await CreateService().FillExtendedMetadataAsync(itemId);

        result.IsSuccess.Should().BeTrue();
        using var verify = _libraryFactory.CreateDbContext();
        var item = verify.LibraryItems.Include(i => i.Snapshot).Include(i => i.Notes).Single();
        var snap = item.Snapshot!;

        // New fields filled
        snap.TitleEnglish.Should().Be("Attack on Titan");
        snap.Themes.Should().Be("""["Gore","Military"]""");
        snap.Demographics.Should().Be("""["Shounen"]""");
        snap.DurationMinutes.Should().Be(24);
        snap.AgeRating.Should().Be("R - 17+ (violence & profanity)");
        snap.Source.Should().Be("Manga");
        // The genre query has no ORDER BY, so only the set is guaranteed (AM-051).
        NameList.Parse(snap.Genres).Should().BeEquivalentTo(["Action", "Ecchi"]);
        snap.DataVersion.Should().Be(MediaDataVersion.Current);

        // Everything else exactly as it was
        snap.Title.Should().Be("Shingeki no Kyojin");
        snap.Synopsis.Should().Be("Original synopsis");
        snap.CoverLocalPath.Should().Be(@"C:\covers\16498_anime.jpg");
        snap.StreamingPlatforms.Should().Be("""[{"Name":"Crunchyroll"}]""");
        item.Score.Should().Be(9);
        item.IsFavorite.Should().BeTrue();
        item.Notes.Single().Content.Should().Be("keep me");
        _catalogServiceMock.Verify(c => c.RefreshAnimeDetailsAsync(16498, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FillExtendedMetadata_SourceFails_LeavesSnapshotLegacyAndUnchanged()
    {
        var itemId = SeedLegacyItem(16498, MediaType.Anime);
        _catalogServiceMock
            .Setup(c => c.RefreshAnimeDetailsAsync(16498, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CachedAnime>.Failure("offline"));

        var result = await CreateService().FillExtendedMetadataAsync(itemId);

        result.IsSuccess.Should().BeFalse();
        using var verify = _libraryFactory.CreateDbContext();
        var snap = verify.Snapshots.Single();
        snap.DataVersion.Should().Be(MediaDataVersion.Legacy, "it is retried next launch");
        snap.Genres.Should().Be("""["Action"]""");
        snap.TitleEnglish.Should().BeNull();
    }

    [Fact]
    public async Task FillExtendedMetadata_CatalogHasNoGenres_KeepsExistingGenres()
    {
        var itemId = SeedLegacyItem(2, MediaType.Manga);
        _catalogServiceMock
            .Setup(c => c.GetMangaAsync(2, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CachedManga>.Success(new CachedManga
            {
                Id = 2, Title = "Berserk", FetchedAt = DateTime.UtcNow,
                Authors = """["Miura, Kentarou"]""", DataVersion = MediaDataVersion.Current
            }));

        (await CreateService().FillExtendedMetadataAsync(itemId)).IsSuccess.Should().BeTrue();

        using var verify = _libraryFactory.CreateDbContext();
        var snap = verify.Snapshots.Single();
        snap.Genres.Should().Be("""["Action"]""", "an empty catalog genre list means a failed cache write, not no genres");
        snap.Authors.Should().Be("""["Miura, Kentarou"]""");
        snap.DataVersion.Should().Be(MediaDataVersion.Current);
    }

    [Fact]
    public async Task FillExtendedMetadata_AlreadyCurrent_DoesNotFetch()
    {
        var itemId = SeedLegacyItem(16498, MediaType.Anime, MediaDataVersion.Current);

        (await CreateService().FillExtendedMetadataAsync(itemId)).IsSuccess.Should().BeTrue();

        _catalogServiceMock.Verify(
            c => c.GetAnimeAsync(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _catalogServiceMock.Verify(
            c => c.RefreshAnimeDetailsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private int SeedLegacyItem(int malId, MediaType type, int dataVersion = MediaDataVersion.Legacy)
    {
        using var db = _libraryFactory.CreateDbContext();
        var item = new LibraryItem
        {
            MalId = malId, MediaType = type, StatusId = 1, Score = 9, IsFavorite = true,
            AddedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Snapshot = new MediaSnapshot
            {
                Title = type == MediaType.Anime ? "Shingeki no Kyojin" : "Berserk",
                Synopsis = "Original synopsis",
                Genres = """["Action"]""",
                CoverLocalPath = $@"C:\covers\{malId}_{type.ToString().ToLowerInvariant()}.jpg",
                StreamingPlatforms = """[{"Name":"Crunchyroll"}]""",
                SnapshotAt = DateTime.UtcNow,
                DataVersion = dataVersion
            },
            Notes = { new Note { Content = "keep me", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow } }
        };
        db.LibraryItems.Add(item);
        db.SaveChanges();
        return item.Id;
    }

    private void SeedCatalogGenres(int malId, string mediaType, params (int Id, string Name)[] genres)
    {
        using var db = _catalogFactory.CreateDbContext();
        foreach (var (id, name) in genres)
        {
            db.Genres.Add(new CachedGenre { Id = id, MediaType = mediaType, Name = name });
            db.MediaGenres.Add(new CachedMediaGenre { MediaId = malId, MediaType = mediaType, GenreId = id });
        }
        db.SaveChanges();
    }

    private void SetupAnime(int malId, string title)
    {
        _catalogServiceMock
            .Setup(c => c.GetAnimeAsync(malId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<CachedAnime>.Success(new CachedAnime
            {
                Id = malId, Title = title, FetchedAt = DateTime.UtcNow, CoverLargeUrl = CoverUrl(malId)
            }));
    }

    private static string CoverUrl(int malId) => $"https://cdn.myanimelist.net/images/anime/{malId}.jpg";

    public void Dispose()
    {
        _libraryDb.Dispose();
        _libraryFactory.Dispose();
        _catalogFactory.Dispose();
        _http.Dispose();
        _storage.Dispose();
    }
}
