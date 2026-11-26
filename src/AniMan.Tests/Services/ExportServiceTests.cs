using System.Text.Json.Nodes;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Services;
using AniMan.Tests.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using AniMan.Core.Common;

namespace AniMan.Tests.Services;

public class ExportServiceTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"AniMan_test_{Guid.NewGuid():N}.json");
    private readonly SqliteContextFactory<LibraryDbContext> _factory;
    private readonly StoragePaths _paths;

    public ExportServiceTests()
    {
        _factory = new SqliteContextFactory<LibraryDbContext>(opts => new LibraryDbContext(opts));

        _paths = new StoragePaths(
            Path.GetTempPath(),
            Path.Combine(Path.GetTempPath(), $"covers_{Guid.NewGuid()}"));
        Directory.CreateDirectory(_paths.CoversPath);
    }

    private ExportService Create()
    {
        var coverStore = new CoverStore(
            Mock.Of<IHttpClientFactory>(f => f.CreateClient("covers") == new HttpClient()),
            _paths, NullLogger<CoverStore>.Instance);

        return new ExportService(_factory, coverStore, NullLogger<ExportService>.Instance);
    }

    private LibraryDbContext CreateDb() => _factory.CreateDbContext();

    private void SeedItem(int malId, MediaType type, int statusId, int? score, string title)
    {
        using var db = CreateDb();
        var item = new LibraryItem
        {
            MalId = malId, MediaType = type, StatusId = statusId, Score = score,
            IsFavorite = true, RewatchCount = 2,
            AddedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Snapshot = new MediaSnapshot { Title = title, TotalEpisodes = 12, Genres = """["Action"]""", SnapshotAt = DateTime.UtcNow },
            EpisodeProgress = { new EpisodeProgress { EpisodeNumber = 1, IsWatched = true, WatchedAt = DateTime.UtcNow } },
            Notes = { new Note { Content = "note", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow } }
        };
        db.LibraryItems.Add(item);
        db.SaveChanges();
    }

    [Fact]
    public async Task Export_WritesTimestampsAsUtc()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Original");

        (await Create().ExportAsync(_tempFile)).IsSuccess.Should().BeTrue();

        var root = JsonNode.Parse(await File.ReadAllTextAsync(_tempFile))!;
        ((string)root["items"]![0]!["addedAt"]!).Should().EndWith("Z", "the file states that its times are UTC");
    }

    [Fact]
    public async Task Import_TimestampWithOffset_IsStoredAsTheSameInstantInUtc()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Original");
        var svc = Create();
        (await svc.ExportAsync(_tempFile)).IsSuccess.Should().BeTrue();

        var root = JsonNode.Parse(await File.ReadAllTextAsync(_tempFile))!;
        root["items"]![0]!["episodes"]![0]!["watchedAt"] = "2026-03-01T15:00:00+03:00"; // 12:00 UTC
        root["items"]![0]!["addedAt"] = "2026-03-01T12:00:00"; // no marker: older backups were UTC
        await File.WriteAllTextAsync(_tempFile, root.ToJsonString());

        (await svc.ImportAsync(_tempFile, ImportMode.Overwrite)).IsSuccess.Should().BeTrue();

        using var verify = CreateDb();
        var item = verify.LibraryItems.Include(i => i.EpisodeProgress).Single();
        item.EpisodeProgress.Single().WatchedAt.Should().Be(new DateTime(2026, 3, 1, 12, 0, 0));
        item.AddedAt.Should().Be(new DateTime(2026, 3, 1, 12, 0, 0));
    }

    [Fact]
    public async Task Import_NameListsWithNullEntries_AreCleaned()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Original");
        var svc = Create();
        (await svc.ExportAsync(_tempFile)).IsSuccess.Should().BeTrue();

        var root = JsonNode.Parse(await File.ReadAllTextAsync(_tempFile))!;
        root["items"]![0]!["snapshot"]!["genres"] = """["Action", null, " "]""";
        root["items"]![0]!["snapshot"]!["themes"] = """["Gore", null]""";
        await File.WriteAllTextAsync(_tempFile, root.ToJsonString());

        (await svc.ImportAsync(_tempFile, ImportMode.Overwrite)).IsSuccess.Should().BeTrue();

        using var verify = CreateDb();
        var snap = verify.Snapshots.Single();
        snap.Genres.Should().Be("""["Action"]""");
        snap.Themes.Should().Be("""["Gore"]""");
    }

    [Fact]
    public async Task Export_ThenImport_Overwrite_RestoresData()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Original");
        var svc = Create();

        (await svc.ExportAsync(_tempFile)).IsSuccess.Should().BeTrue();
        File.Exists(_tempFile).Should().BeTrue();

        // Wipe the library, then import.
        using (var db = CreateDb())
        {
            db.LibraryItems.RemoveRange(db.LibraryItems);
            db.SaveChanges();
        }

        var result = await svc.ImportAsync(_tempFile, ImportMode.Overwrite);

        result.IsSuccess.Should().BeTrue();
        using var verify = CreateDb();
        var item = verify.LibraryItems
            .Include(i => i.Snapshot).Include(i => i.EpisodeProgress).Include(i => i.Notes).Include(i => i.Status)
            .Single();
        item.MalId.Should().Be(100);
        item.Score.Should().Be(8);
        item.IsFavorite.Should().BeTrue();
        item.Status!.Name.Should().Be("Watching");
        item.Snapshot!.Title.Should().Be("Original");
        item.EpisodeProgress.Should().ContainSingle().Which.IsWatched.Should().BeTrue();
        item.Notes.Should().ContainSingle().Which.Content.Should().Be("note");
    }

    [Fact]
    public async Task Export_OmitsCoverLocalPathFromBackupJson()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Privacy Check");
        using (var db = CreateDb())
        {
            var snap = db.Snapshots.Single();
            snap.CoverLocalPath = @"C:\Users\SecretUsername\AppData\Roaming\AniMan\covers\100_anime.jpg";
            snap.CoverOriginalUrl = "https://cdn.myanimelist.net/images/anime/1/100.jpg";
            db.SaveChanges();
        }

        var result = await Create().ExportAsync(_tempFile);

        result.IsSuccess.Should().BeTrue();
        var rawJson = await File.ReadAllTextAsync(_tempFile);
        rawJson.Should().NotContain("SecretUsername");
        var root = JsonNode.Parse(rawJson)!;
        root["items"]![0]!["snapshot"]!["coverLocalPath"].Should().BeNull();
    }

    [Fact]
    public async Task Import_FiltersUnsafeStreamingOverrideUrls()
    {
        await File.WriteAllTextAsync(_tempFile, """
        {
          "schemaVersion": 1,
          "exportedAt": "2026-10-28T12:00:00Z",
          "statuses": [],
          "items": [
            {
              "malId": 100,
              "mediaType": "Anime",
              "statusName": "Watching",
              "snapshot": {
                "title": "URL Sanitization",
                "coverOriginalUrl": "file:///C:/Windows/win.ini",
                "genres": "[]",
                "snapshotAt": "2026-10-28T12:00:00Z"
              },
              "streamingOverrides": [
                { "platformName": "MaliciousJs", "url": "javascript:alert(1)" },
                { "platformName": "MaliciousFile", "url": "file:///C:/Windows/System32/calc.exe" },
                { "platformName": "Netflix", "url": null },
                { "platformName": "Crunchyroll", "url": "https://www.crunchyroll.com/watch/123" }
              ]
            }
          ]
        }
        """);

        var result = await Create().ImportAsync(_tempFile, ImportMode.Overwrite);

        result.IsSuccess.Should().BeTrue();
        using var verify = CreateDb();
        var item = verify.LibraryItems
            .Include(i => i.Snapshot)
            .Include(i => i.StreamingOverrides)
            .Single();

        item.Snapshot!.CoverOriginalUrl.Should().BeNull();
        item.StreamingOverrides.Should().NotContain(o => o.PlatformName == "MaliciousJs");
        item.StreamingOverrides.Should().NotContain(o => o.PlatformName == "MaliciousFile");
        item.StreamingOverrides.Should().Contain(o => o.PlatformName == "Netflix" && o.Url == null);
        item.StreamingOverrides.Should().Contain(o =>
            o.PlatformName == "Crunchyroll" && o.Url == "https://www.crunchyroll.com/watch/123");
    }

    [Fact]
    public async Task Import_RandomJsonInOverwriteMode_RejectsAndPreservesExistingLibrary()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Do Not Wipe Me");
        await File.WriteAllTextAsync(_tempFile, """{"hello":"world"}""");

        var result = await Create().ImportAsync(_tempFile, ImportMode.Overwrite);

        result.IsSuccess.Should().BeFalse();
        result.Kind.Should().Be(ErrorKind.BackupInvalid);
        using var verify = CreateDb();
        verify.LibraryItems.Should().ContainSingle(i => i.MalId == 100);
    }

    [Fact]
    public async Task Import_EmptyItemsInOverwriteMode_RefusesToWipeNonEmptyLibrary()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Protected");
        await File.WriteAllTextAsync(
            _tempFile,
            """{"schemaVersion":1,"exportedAt":"2026-10-28T12:00:00Z","statuses":[],"items":[]}""");

        var result = await Create().ImportAsync(_tempFile, ImportMode.Overwrite);

        result.IsSuccess.Should().BeFalse();
        result.Kind.Should().Be(ErrorKind.BackupEmptyOverwrite);
        using var verify = CreateDb();
        verify.LibraryItems.Should().ContainSingle(i => i.MalId == 100);
    }

    [Fact]
    public async Task Import_BackupFromNewerVersion_IsRejectedAsTooNew_AndLibraryIsUntouched()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Keep Me");
        await File.WriteAllTextAsync(
            _tempFile,
            """{"schemaVersion":999,"exportedAt":"2026-10-28T12:00:00Z","statuses":[],"items":[]}""");

        var result = await Create().ImportAsync(_tempFile, ImportMode.Overwrite);

        result.IsSuccess.Should().BeFalse();
        result.Kind.Should().Be(ErrorKind.BackupTooNew, "the file is fine — the app is just older than it");
        using var verify = CreateDb();
        verify.LibraryItems.Should().ContainSingle(i => i.MalId == 100);
    }

    [Fact]
    public async Task Import_Merge_DeduplicatesRepeatedBackupItems()
    {
        await File.WriteAllTextAsync(_tempFile, """
        {
          "schemaVersion": 1,
          "exportedAt": "2026-10-28T12:00:00Z",
          "statuses": [],
          "items": [
            { "malId": 100, "mediaType": "Anime", "statusName": "Watching", "snapshot": { "title": "First", "genres": "[]", "snapshotAt": "2026-10-28T12:00:00Z" } },
            { "malId": 100, "mediaType": "Anime", "statusName": "Completed", "snapshot": { "title": "Duplicate", "genres": "[]", "snapshotAt": "2026-10-28T12:00:00Z" } }
          ]
        }
        """);

        var result = await Create().ImportAsync(_tempFile, ImportMode.Merge);

        result.IsSuccess.Should().BeTrue();
        using var verify = CreateDb();
        verify.LibraryItems.Should().ContainSingle(i => i.MalId == 100);
    }

    [Fact]
    public async Task Import_Overwrite_SweepsUnreferencedOrphanCovers()
    {
        var keptCover = Path.Combine(_paths.CoversPath, "100_anime.jpg");
        var orphanCover = Path.Combine(_paths.CoversPath, "999_anime.jpg");
        await File.WriteAllBytesAsync(keptCover, [1, 2, 3]);
        await File.WriteAllBytesAsync(orphanCover, [4, 5, 6]);

        await File.WriteAllTextAsync(_tempFile, """
        {
          "schemaVersion": 1,
          "exportedAt": "2026-10-28T12:00:00Z",
          "statuses": [],
          "items": [
            { "malId": 100, "mediaType": "Anime", "statusName": "Watching", "snapshot": { "title": "First", "genres": "[]", "snapshotAt": "2026-10-28T12:00:00Z" } }
          ]
        }
        """);

        var result = await Create().ImportAsync(_tempFile, ImportMode.Overwrite);

        result.IsSuccess.Should().BeTrue();
        File.Exists(keptCover).Should().BeTrue();
        File.Exists(orphanCover).Should().BeFalse("unreferenced cover files are swept after Overwrite");
    }

    [Fact]
    public async Task Import_Merge_KeepsExistingItemUnchanged()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Original");
        var svc = Create();
        await svc.ExportAsync(_tempFile);

        // Mutate the existing item; merge must NOT overwrite it.
        using (var db = CreateDb())
        {
            var item = db.LibraryItems.Single();
            item.Score = 3;
            db.SaveChanges();
        }

        var result = await svc.ImportAsync(_tempFile, ImportMode.Merge);

        result.IsSuccess.Should().BeTrue();
        using var verify = CreateDb();
        verify.LibraryItems.Should().ContainSingle()
            .Which.Score.Should().Be(3); // unchanged, not reverted to 8
    }

    [Fact]
    public async Task Import_Merge_AddsMissingItem()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Original");
        var svc = Create();
        await svc.ExportAsync(_tempFile);

        // Add a second, different item; merge should leave it and add back the backed-up one if absent.
        using (var db = CreateDb())
        {
            db.LibraryItems.RemoveRange(db.LibraryItems); // remove malId 100
            db.LibraryItems.Add(new LibraryItem
            {
                MalId = 200, MediaType = MediaType.Manga, StatusId = 2,
                AddedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var result = await svc.ImportAsync(_tempFile, ImportMode.Merge);

        result.IsSuccess.Should().BeTrue();
        using var verify = CreateDb();
        verify.LibraryItems.Select(i => i.MalId).Should().BeEquivalentTo([100, 200]);
    }

    [Fact]
    public async Task Import_Merge_RestoresLocallyTrashedItemWhenBackupItemIsActive()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Original");
        var svc = Create();
        await svc.ExportAsync(_tempFile);

        // Soft-delete the local item so it is in Trash
        using (var db = CreateDb())
        {
            var item = db.LibraryItems.Single(i => i.MalId == 100);
            item.DeletedAt = DateTime.UtcNow;
            db.SaveChanges();
        }

        var result = await svc.ImportAsync(_tempFile, ImportMode.Merge);

        result.IsSuccess.Should().BeTrue();
        using var verify = CreateDb();
        var restored = verify.LibraryItems.Single(i => i.MalId == 100);
        restored.DeletedAt.Should().BeNull();
        restored.Score.Should().Be(8);
    }

    [Fact]
    public async Task Import_Overwrite_ReplacesAllItems()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Original");
        var svc = Create();
        await svc.ExportAsync(_tempFile);

        using (var db = CreateDb())
        {
            db.LibraryItems.RemoveRange(db.LibraryItems);
            db.LibraryItems.Add(new LibraryItem
            {
                MalId = 999, MediaType = MediaType.Anime, StatusId = 1,
                AddedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        var result = await svc.ImportAsync(_tempFile, ImportMode.Overwrite);

        result.IsSuccess.Should().BeTrue();
        using var verify = CreateDb();
        verify.LibraryItems.Select(i => i.MalId).Should().BeEquivalentTo([100]); // 999 gone
    }

    [Fact]
    public async Task Export_ThenImport_PreservesSnapshotStatusAndStudio()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Original");
        using (var db = CreateDb())
        {
            var snap = db.Snapshots.Single();
            snap.Status = "Finished Airing";
            snap.Studio = "MAPPA";
            db.SaveChanges();
        }

        var svc = Create();
        await svc.ExportAsync(_tempFile);

        var result = await svc.ImportAsync(_tempFile, ImportMode.Overwrite);

        result.IsSuccess.Should().BeTrue();
        using var verify = CreateDb();
        var imported = verify.LibraryItems.Include(i => i.Snapshot).Single().Snapshot!;
        imported.Status.Should().Be("Finished Airing");
        imported.Studio.Should().Be("MAPPA");
    }

    [Fact]
    public async Task Export_ThenImport_PreservesUserRatingAndTrashState()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Rated");
        SeedItem(200, MediaType.Anime, 1, null, "Trashed");
        using (var db = CreateDb())
        {
            var rated = db.LibraryItems.Single(i => i.MalId == 100);
            rated.UserRating = 7.5m;

            var trashed = db.LibraryItems.Single(i => i.MalId == 200);
            trashed.DeletedAt = DateTime.UtcNow;

            db.SaveChanges();
        }

        var svc = Create();
        await svc.ExportAsync(_tempFile);

        var result = await svc.ImportAsync(_tempFile, ImportMode.Overwrite);

        result.IsSuccess.Should().BeTrue();
        using var verify = CreateDb();
        verify.LibraryItems.Single(i => i.MalId == 100).UserRating.Should().Be(7.5m);
        verify.LibraryItems.Single(i => i.MalId == 200).DeletedAt.Should().NotBeNull(
            "trashed items must stay trashed after a backup round-trip");
    }

    [Fact]
    public async Task Export_WritesLastBackupAtSetting()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Original");
        var svc = Create();

        await svc.ExportAsync(_tempFile);

        using var verify = CreateDb();
        verify.Settings.Find("LastBackupAt").Should().NotBeNull();
    }

    [Fact]
    public async Task Import_MissingFile_ReturnsFailure()
    {
        var result = await Create().ImportAsync("does_not_exist.json", ImportMode.Merge);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task Import_CoverFileOnDisk_RepointsCoverLocalPathToThisMachine()
    {
        // The backup carries a stale absolute path from another machine; the cover
        // file itself still exists locally under the canonical name.
        var localCover = Path.Combine(_paths.CoversPath, "100_anime.jpg");
        await File.WriteAllBytesAsync(localCover, [1, 2, 3]);

        SeedItem(100, MediaType.Anime, 1, 8, "Original");
        using (var db = CreateDb())
        {
            var snap = db.Snapshots.Single();
            snap.CoverLocalPath = @"C:\Users\someone-else\AppData\Roaming\AniMan\covers\100_anime.jpg";
            db.SaveChanges();
        }

        var svc = Create();
        await svc.ExportAsync(_tempFile);

        var result = await svc.ImportAsync(_tempFile, ImportMode.Overwrite);

        result.IsSuccess.Should().BeTrue();
        using var verify = CreateDb();
        verify.LibraryItems.Include(i => i.Snapshot).Single()
            .Snapshot!.CoverLocalPath.Should().Be(localCover);
    }

    [Fact]
    public async Task Import_CoverMissingAndDownloadFails_LeavesCoverNull()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Original");
        using (var db = CreateDb())
        {
            var snap = db.Snapshots.Single();
            snap.CoverLocalPath = @"C:\stale\path\100_anime.jpg";
            snap.CoverOriginalUrl = "http://localhost:1/unreachable.jpg";
            db.SaveChanges();
        }

        var svc = Create();
        await svc.ExportAsync(_tempFile);

        var result = await svc.ImportAsync(_tempFile, ImportMode.Overwrite);

        result.IsSuccess.Should().BeTrue("a failed cover download must never fail the import");
        using var verify = CreateDb();
        verify.LibraryItems.Include(i => i.Snapshot).Single()
            .Snapshot!.CoverLocalPath.Should().BeNull();
    }

    [Fact]
    public async Task Import_CoverUrlOutsideTheKnownCdns_IsKeptButNeverDownloaded()
    {
        SeedItem(100, MediaType.Anime, 1, 8, "Trusted");
        SeedItem(200, MediaType.Anime, 1, 8, "Untrusted");
        using (var db = CreateDb())
        {
            var snaps = db.Snapshots.Include(s => s.LibraryItem).ToList();
            snaps.Single(s => s.LibraryItem!.MalId == 100).CoverOriginalUrl = "https://cdn.myanimelist.net/images/anime/1/100.jpg";
            snaps.Single(s => s.LibraryItem!.MalId == 200).CoverOriginalUrl = "https://tracker.example/cover.jpg";
            db.SaveChanges();
        }
        await Create().ExportAsync(_tempFile);

        var handler = new RecordingJpegHandler();
        var coverStore = new CoverStore(
            Mock.Of<IHttpClientFactory>(f => f.CreateClient("covers") == new HttpClient(handler)),
            _paths, NullLogger<CoverStore>.Instance);
        var svc = new ExportService(_factory, coverStore, NullLogger<ExportService>.Instance);

        var result = await svc.ImportAsync(_tempFile, ImportMode.Overwrite);

        result.Value!.CoversFailed.Should().Be(1, "the skipped cover is reported like a failed one");
        handler.Hosts.Should().Equal("cdn.myanimelist.net");
        using var verify = CreateDb();
        var untrusted = verify.LibraryItems.Include(i => i.Snapshot).Single(i => i.MalId == 200).Snapshot!;
        untrusted.CoverLocalPath.Should().BeNull();
        untrusted.CoverOriginalUrl.Should().Be("https://tracker.example/cover.jpg",
            "the backup's data is kept as it was, only the download is refused");
        verify.LibraryItems.Include(i => i.Snapshot).Single(i => i.MalId == 100)
            .Snapshot!.CoverLocalPath.Should().NotBeNull();
    }

    private sealed class RecordingJpegHandler : HttpMessageHandler
    {
        public List<string> Hosts { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            lock (Hosts) Hosts.Add(request.RequestUri!.Host);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0])
            });
        }
    }

    // ── Backward compatibility: backups from earlier releases ─────────────────

    /// <summary>Offline cover store: imports try to download covers, tests must not hit the network.</summary>
    private ExportService CreateOffline()
    {
        var offline = new HttpClient(new OfflineHandler());
        var coverStore = new CoverStore(
            Mock.Of<IHttpClientFactory>(f => f.CreateClient("covers") == offline),
            _paths, NullLogger<CoverStore>.Instance);
        return new ExportService(_factory, coverStore, NullLogger<ExportService>.Instance);
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("offline test");
    }

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public async Task Import_FrozenV1Backup_RestoresEveryUserFieldAndMarksSnapshotsLegacy()
    {
        var result = await CreateOffline().ImportAsync(FixturePath("library-backup-v1.json"), ImportMode.Overwrite);

        result.IsSuccess.Should().BeTrue(result.Error);
        using var db = CreateDb();
        var items = db.LibraryItems
            .Include(i => i.Snapshot).Include(i => i.Status)
            .Include(i => i.EpisodeProgress).Include(i => i.ChapterProgress)
            .Include(i => i.Notes).Include(i => i.StreamingOverrides)
            .OrderBy(i => i.MalId)
            .ToList();
        items.Should().HaveCount(2);

        var manga = items[0];
        manga.MalId.Should().Be(2);
        manga.MediaType.Should().Be(MediaType.Manga);
        manga.Status!.Name.Should().Be("Reading");
        manga.DeletedAt.Should().Be(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), "trash state survives");
        manga.ChapterProgress.Should().HaveCount(2).And.OnlyContain(c => c.IsRead);
        manga.Snapshot!.Title.Should().Be("Berserk");

        var anime = items[1];
        anime.MalId.Should().Be(16498);
        anime.Status!.Name.Should().Be("Completed");
        anime.Score.Should().Be(9);
        anime.UserRating.Should().Be(8.5m);
        anime.IsFavorite.Should().BeTrue();
        anime.RewatchCount.Should().Be(1);
        anime.LastRewatchDate.Should().Be(new DateOnly(2026, 6, 1));
        anime.StartedAt.Should().Be(new DateOnly(2026, 1, 3));
        anime.CompletedAt.Should().Be(new DateOnly(2026, 2, 14));
        anime.EpisodeProgress.Should().HaveCount(3);
        anime.EpisodeProgress.Count(e => e.IsWatched).Should().Be(2);
        anime.Notes.Should().HaveCount(2);
        anime.Notes.Single(n => n.EpisodeNumber is null).Content.Should().Be("Rewatch before the final season.");
        anime.StreamingOverrides.Single().PlatformName.Should().Be("Netflix");
        anime.Snapshot!.Title.Should().Be("Shingeki no Kyojin");
        anime.Snapshot.TitleJapanese.Should().Be("進撃の巨人");
        anime.Snapshot.Genres.Should().Contain("Award Winning");
        anime.Snapshot.StreamingPlatforms.Should().Contain("Crunchyroll");

        // v1 has no extended metadata: it must arrive empty and marked for the backfill.
        items.Should().AllSatisfy(i =>
        {
            i.Snapshot!.DataVersion.Should().Be(Core.Domain.MediaDataVersion.Legacy);
            i.Snapshot.TitleEnglish.Should().BeNull();
            i.Snapshot.Themes.Should().BeNull();
            i.Snapshot.DurationMinutes.Should().BeNull();
        });
    }

    [Fact]
    public async Task Export_ThenImport_V2_PreservesExtendedMetadata()
    {
        using (var db = CreateDb())
        {
            db.LibraryItems.Add(new LibraryItem
            {
                MalId = 16498, MediaType = MediaType.Anime, StatusId = 3,
                AddedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                Snapshot = new MediaSnapshot
                {
                    Title = "Shingeki no Kyojin", Genres = """["Action"]""", SnapshotAt = DateTime.UtcNow,
                    TitleEnglish = "Attack on Titan",
                    Themes = """["Gore","Military","Survival"]""",
                    Demographics = """["Shounen"]""",
                    DurationMinutes = 24,
                    AgeRating = "R - 17+ (violence & profanity)",
                    Source = "Manga",
                    DataVersion = Core.Domain.MediaDataVersion.Current
                }
            });
            db.SaveChanges();
        }

        var svc = CreateOffline();
        (await svc.ExportAsync(_tempFile)).IsSuccess.Should().BeTrue();
        JsonNode.Parse(File.ReadAllText(_tempFile))!["schemaVersion"]!.GetValue<int>().Should().Be(2);

        (await svc.ImportAsync(_tempFile, ImportMode.Overwrite)).IsSuccess.Should().BeTrue();

        using var verify = CreateDb();
        var snap = verify.Snapshots.Single();
        snap.TitleEnglish.Should().Be("Attack on Titan");
        snap.Themes.Should().Be("""["Gore","Military","Survival"]""");
        snap.Demographics.Should().Be("""["Shounen"]""");
        snap.DurationMinutes.Should().Be(24);
        snap.AgeRating.Should().Be("R - 17+ (violence & profanity)");
        snap.Source.Should().Be("Manga");
        snap.DataVersion.Should().Be(Core.Domain.MediaDataVersion.Current);
    }

    [Fact]
    public async Task Import_DamagedExtendedField_DropsOnlyThatFieldAndKeepsTheItem()
    {
        var json = File.ReadAllText(FixturePath("library-backup-v1.json"));
        var root = JsonNode.Parse(json)!;
        root["schemaVersion"] = 2;
        var snapshot = root["items"]![0]!["snapshot"]!;
        snapshot["themes"] = "not a json array";
        snapshot["titleEnglish"] = "Attack on Titan";
        snapshot["dataVersion"] = 99; // unknown future generation
        File.WriteAllText(_tempFile, root.ToJsonString());

        (await CreateOffline().ImportAsync(_tempFile, ImportMode.Overwrite)).IsSuccess.Should().BeTrue();

        using var verify = CreateDb();
        var snap = verify.Snapshots.Single(s => s.Title == "Shingeki no Kyojin");
        snap.Themes.Should().BeNull("an unparseable list is dropped rather than stored");
        snap.TitleEnglish.Should().Be("Attack on Titan");
        snap.DataVersion.Should().Be(Core.Domain.MediaDataVersion.Legacy, "unknown versions are re-filled, not trusted");
    }

    // ── Progress and summary ──────────────────────────────────────────────────

    /// <summary>Synchronous recorder; Progress&lt;T&gt; would post reports asynchronously.</summary>
    private sealed class RecordingProgress : IProgress<ImportProgress>
    {
        private readonly object _gate = new();
        public List<ImportProgress> Reports { get; } = [];
        public void Report(ImportProgress value) { lock (_gate) Reports.Add(value); }
    }

    [Fact]
    public async Task Import_ReportsProgressInStageOrder_NeverBackwards_EndingAtComplete()
    {
        var progress = new RecordingProgress();

        var result = await CreateOffline().ImportAsync(
            FixturePath("library-backup-v1.json"), ImportMode.Overwrite, progress);

        result.IsSuccess.Should().BeTrue();
        var reports = progress.Reports;
        reports.Should().NotBeEmpty();
        reports.First().Stage.Should().Be(ImportStage.Reading);
        reports.Select(r => r.Stage).Should().BeInAscendingOrder();
        reports.Select(r => r.Fraction).Should().BeInAscendingOrder("the bar must never step back");
        reports.Last().Fraction.Should().Be(1);

        // Both fixture items have a remote cover and none is on disk, so both are attempted.
        var lastCover = reports.Last(r => r.Stage == ImportStage.DownloadingCovers);
        lastCover.Done.Should().Be(2);
        lastCover.Total.Should().Be(2);
    }

    [Fact]
    public async Task Import_Summary_CountsImportedItemsAndFailedCovers()
    {
        var result = await CreateOffline().ImportAsync(FixturePath("library-backup-v1.json"), ImportMode.Overwrite);

        result.Value.Should().Be(new ImportSummary(Imported: 2, Skipped: 0, CoversFailed: 2));
    }

    [Fact]
    public async Task Import_MergeSameBackupTwice_SecondRunSkipsEverything()
    {
        var svc = CreateOffline();
        await svc.ImportAsync(FixturePath("library-backup-v1.json"), ImportMode.Merge);

        var second = await svc.ImportAsync(FixturePath("library-backup-v1.json"), ImportMode.Merge);

        // The anime is already active; the manga is already in the local Trash.
        second.Value.Should().Be(new ImportSummary(Imported: 0, Skipped: 2, CoversFailed: 0));
        using var db = CreateDb();
        db.LibraryItems.Should().HaveCount(2);
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
        if (Directory.Exists(_paths.CoversPath)) Directory.Delete(_paths.CoversPath, recursive: true);
    }
}
