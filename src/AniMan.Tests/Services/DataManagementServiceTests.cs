using AniMan.Core.Domain;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Infrastructure;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Services;
using AniMan.Tests.Integration;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniMan.Tests.Services;

public class DataManagementServiceTests : IDisposable
{
    private readonly SqliteContextFactory<LibraryDbContext> _libraryFactory;
    private readonly SqliteContextFactory<CatalogDbContext> _catalogFactory;
    private readonly StoragePaths _paths;
    private readonly DataManagementService _service;

    public DataManagementServiceTests()
    {
        _libraryFactory = new SqliteContextFactory<LibraryDbContext>(opts => new LibraryDbContext(opts));
        _catalogFactory = new SqliteContextFactory<CatalogDbContext>(opts => new CatalogDbContext(opts));

        _paths = new StoragePaths(
            Path.GetTempPath(),
            Path.Combine(Path.GetTempPath(), $"covers_{Guid.NewGuid()}"));
        Directory.CreateDirectory(_paths.CoversPath);

        _service = new DataManagementService(
            _libraryFactory, _catalogFactory, _paths,
            NullLogger<DataManagementService>.Instance);
    }

    [Fact]
    public async Task ResetAllData_PurgesLibraryAndCatalog_WhilePreservingUserSettingsAndDefaultStatuses()
    {
        // 1. Seed Library DB
        await using (var lib = await _libraryFactory.CreateDbContextAsync())
        {
            var item = new LibraryItem
            {
                Id = 1,
                MalId = 100,
                MediaType = MediaType.Anime,
                StatusId = TrackingStatusIds.Watching,
                Score = 9,
                Snapshot = new MediaSnapshot
                {
                    Title = "Test Anime",
                    TotalEpisodes = 12,
                    Genres = "[\"Action\"]",
                    SnapshotAt = DateTime.UtcNow
                }
            };
            lib.LibraryItems.Add(item);

            lib.EpisodeProgress.Add(new EpisodeProgress
            {
                LibraryItemId = 1,
                EpisodeNumber = 1,
                IsWatched = true
            });

            lib.Notes.Add(new Note
            {
                LibraryItemId = 1,
                Content = "Series Note",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            // Add custom non-default status
            lib.TrackingStatuses.Add(new TrackingStatus
            {
                Id = 99,
                Name = "Custom Status",
                Color = "#FFFFFF",
                DisplayOrder = 99,
                IsDefault = false
            });

            // Add user settings (preserved) and transient settings (purged)
            lib.Settings.AddRange(
                new AppSetting { Key = "Theme", Value = "Dark" },
                new AppSetting { Key = "Language", Value = "tr" },
                new AppSetting { Key = "StartupPage", Value = "Anime" },
                new AppSetting { Key = "FontFamily", Value = "Consolas" },
                new AppSetting { Key = "CacheRefreshDays", Value = "14" },
                new AppSetting { Key = "LastBackupAt", Value = "2026-10-28T12:00:00Z" },
                new AppSetting { Key = "SomeTempKey", Value = "Ephemeral" }
            );

            await lib.SaveChangesAsync();
        }

        // 2. Seed Catalog DB
        await using (var cat = await _catalogFactory.CreateDbContextAsync())
        {
            cat.Anime.Add(new CachedAnime { Id = 100, Title = "Test Anime", FetchedAt = DateTime.UtcNow });
            cat.Episodes.Add(new CachedEpisode { AnimeId = 100, EpisodeNumber = 1, Title = "Ep 1", FetchedAt = DateTime.UtcNow });
            cat.AnimeStreaming.Add(new CachedAnimeStreaming { AnimeId = 100, PlatformName = "Crunchyroll" });
            cat.Manga.Add(new CachedManga { Id = 200, Title = "Test Manga", FetchedAt = DateTime.UtcNow });
            cat.Genres.Add(new CachedGenre { Id = 1, Name = "Action", MediaType = "anime" });
            cat.MediaGenres.Add(new CachedMediaGenre { MediaId = 100, MediaType = "anime", GenreId = 1 });

            await cat.SaveChangesAsync();
        }

        // 3. Act: Reset all data
        var result = await _service.ResetAllDataAsync();
        result.IsSuccess.Should().BeTrue();

        // 4. Assert: Library data purged, settings & default statuses preserved
        await using (var lib = await _libraryFactory.CreateDbContextAsync())
        {
            (await lib.LibraryItems.CountAsync()).Should().Be(0);
            (await lib.Snapshots.CountAsync()).Should().Be(0);
            (await lib.EpisodeProgress.CountAsync()).Should().Be(0);
            (await lib.Notes.CountAsync()).Should().Be(0);

            // Custom status 99 removed, default statuses 1..7 preserved
            (await lib.TrackingStatuses.AnyAsync(s => s.Id == 99)).Should().BeFalse();
            (await lib.TrackingStatuses.CountAsync()).Should().Be(7);

            // Preserved settings remain
            (await lib.Settings.FindAsync(["Theme"]))?.Value.Should().Be("Dark");
            (await lib.Settings.FindAsync(["Language"]))?.Value.Should().Be("tr");
            (await lib.Settings.FindAsync(["StartupPage"]))?.Value.Should().Be("Anime");
            (await lib.Settings.FindAsync(["FontFamily"]))?.Value.Should().Be("Consolas");
            (await lib.Settings.FindAsync(["CacheRefreshDays"]))?.Value.Should().Be("14");

            // Ephemeral settings purged
            (await lib.Settings.FindAsync(["LastBackupAt"])).Should().BeNull();
            (await lib.Settings.FindAsync(["SomeTempKey"])).Should().BeNull();
        }

        // 5. Assert: Catalog data completely purged
        await using (var cat = await _catalogFactory.CreateDbContextAsync())
        {
            (await cat.Anime.CountAsync()).Should().Be(0);
            (await cat.Episodes.CountAsync()).Should().Be(0);
            (await cat.AnimeStreaming.CountAsync()).Should().Be(0);
            (await cat.Manga.CountAsync()).Should().Be(0);
            (await cat.Genres.CountAsync()).Should().Be(0);
            (await cat.MediaGenres.CountAsync()).Should().Be(0);
        }
    }

    public void Dispose()
    {
        _libraryFactory.Dispose();
        _catalogFactory.Dispose();
        if (Directory.Exists(_paths.CoversPath))
            Directory.Delete(_paths.CoversPath, recursive: true);
    }
}

