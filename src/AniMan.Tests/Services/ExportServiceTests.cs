using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AniMan.Tests.Services;

public class ExportServiceTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"AniMan_test_{Guid.NewGuid():N}.json");
    private readonly DbContextOptions<LibraryDbContext> _options;
    private readonly StoragePaths _paths;

    public ExportServiceTests()
    {
        _options = new DbContextOptionsBuilder<LibraryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _paths = new StoragePaths(
            Path.GetTempPath(),
            Path.Combine(Path.GetTempPath(), $"covers_{Guid.NewGuid()}"));
        Directory.CreateDirectory(_paths.CoversPath);
    }

    private ExportService Create()
    {
        var factoryMock = new Mock<IDbContextFactory<LibraryDbContext>>();
        factoryMock.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => CreateDb());

        // No real HTTP in tests — downloads fail gracefully, leaving covers null.
        var coverStore = new CoverStore(
            Mock.Of<IHttpClientFactory>(f => f.CreateClient("covers") == new HttpClient()),
            _paths, NullLogger<CoverStore>.Instance);

        return new ExportService(factoryMock.Object, coverStore, NullLogger<ExportService>.Instance);
    }

    private LibraryDbContext CreateDb()
    {
        var db = new LibraryDbContext(_options);
        db.Database.EnsureCreated();
        return db;
    }

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
        // Query via the item: the InMemory provider doesn't cascade-delete the
        // old snapshot on Overwrite (real SQLite does), so Snapshots may hold both.
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

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
        if (Directory.Exists(_paths.CoversPath))
            Directory.Delete(_paths.CoversPath, recursive: true);
    }
}
