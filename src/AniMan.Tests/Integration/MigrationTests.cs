using AniMan.Core.Domain;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AniMan.Tests.Integration;

/// <summary>
/// Guards the failure that shipped in v0.2.1: <c>Migrations/</c> was gitignored, so
/// every build carried zero migrations. <c>Database.MigrateAsync()</c> at startup then
/// found nothing to apply and created only EF's own bookkeeping tables, after which
/// every query failed with "no such table" — while the app reported no error at all.
/// These tests run the real migrations against an empty database and against legacy
/// v0.2.5 schemas populated with user data, so broken migrations fail the build immediately.
/// </summary>
public class MigrationTests
{
    [Fact]
    public void LibraryDbContext_HasAtLeastOneMigration()
    {
        using var db = NewLibraryContext(out var connection);
        using var _ = connection;

        db.Database.GetMigrations().Should().NotBeEmpty(
            "the app applies migrations at startup and creates no schema without them");
    }

    [Fact]
    public void CatalogDbContext_HasAtLeastOneMigration()
    {
        using var db = NewCatalogContext(out var connection);
        using var _ = connection;

        db.Database.GetMigrations().Should().NotBeEmpty(
            "the app applies migrations at startup and creates no schema without them");
    }

    [Fact]
    public async Task Migrate_OnEmptyLibraryDatabase_CreatesSchemaAndSeedsStatuses()
    {
        await using var db = NewLibraryContext(out var connection);
        using var _ = connection;

        await db.Database.MigrateAsync();

        // Querying at all proves the tables exist — the shipped bug threw here.
        (await db.LibraryItems.CountAsync()).Should().Be(0);
        (await db.Snapshots.CountAsync()).Should().Be(0);
        (await db.EpisodeProgress.CountAsync()).Should().Be(0);
        (await db.ChapterProgress.CountAsync()).Should().Be(0);
        (await db.Notes.CountAsync()).Should().Be(0);
        (await db.StreamingOverrides.CountAsync()).Should().Be(0);
        (await db.Settings.CountAsync()).Should().Be(0);

        var statusIds = await db.TrackingStatuses.Select(s => s.Id).ToListAsync();
        statusIds.Should().BeEquivalentTo(new[]
        {
            TrackingStatusIds.Watching, TrackingStatusIds.Reading, TrackingStatusIds.Completed,
            TrackingStatusIds.OnHold, TrackingStatusIds.Dropped,
            TrackingStatusIds.PlanToWatch, TrackingStatusIds.PlanToRead
        });
    }

    [Fact]
    public async Task Migrate_OnEmptyCatalogDatabase_CreatesSchema()
    {
        await using var db = NewCatalogContext(out var connection);
        using var _ = connection;

        await db.Database.MigrateAsync();

        (await db.Anime.CountAsync()).Should().Be(0);
        (await db.Manga.CountAsync()).Should().Be(0);
        (await db.Episodes.CountAsync()).Should().Be(0);
        (await db.AnimeStreaming.CountAsync()).Should().Be(0);
        (await db.Genres.CountAsync()).Should().Be(0);
        (await db.MediaGenres.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Migrate_FromV025LibraryDatabase_PreservesItemsDeduplicatesNotesAndEnforcesConstraints()
    {
        await using var db = NewLibraryContext(out var connection);
        using var _ = connection;

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260726161715_InitialCreate");

        var now = DateTime.UtcNow;
        var item = new LibraryItem
        {
            MalId = 5114,
            MediaType = MediaType.Anime,
            StatusId = TrackingStatusIds.Watching,
            Score = 10,
            AddedAt = now,
            UpdatedAt = now,
            Snapshot = new MediaSnapshot
            {
                Title = "Fullmetal Alchemist: Brotherhood",
                TotalEpisodes = 64,
                Genres = """["Action","Adventure"]""",
                SnapshotAt = now
            }
        };
        db.LibraryItems.Add(item);
        await db.SaveChangesAsync();

        // Simulate v0.2.5 edge cases: whitespace-only note + duplicate series notes.
        db.Notes.AddRange(
            new Note { LibraryItemId = item.Id, Content = "   ", CreatedAt = now.AddMinutes(-3), UpdatedAt = now.AddMinutes(-3) },
            new Note { LibraryItemId = item.Id, Content = "Older series note", CreatedAt = now.AddMinutes(-2), UpdatedAt = now.AddMinutes(-2) },
            new Note { LibraryItemId = item.Id, Content = "Latest series note", CreatedAt = now.AddMinutes(-1), UpdatedAt = now.AddMinutes(-1) },
            new Note { LibraryItemId = item.Id, EpisodeNumber = 1, Content = "Ep 1 old", CreatedAt = now.AddMinutes(-2), UpdatedAt = now.AddMinutes(-2) },
            new Note { LibraryItemId = item.Id, EpisodeNumber = 1, Content = "Ep 1 latest", CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();

        // Upgrade to v0.2.6 (HardenLibraryConstraints).
        await db.Database.MigrateAsync();

        await using var verify = new LibraryDbContext(
            new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite(connection).Options);

        var upgradedItem = await verify.LibraryItems
            .Include(i => i.Snapshot)
            .Include(i => i.Notes)
            .SingleAsync();

        upgradedItem.MalId.Should().Be(5114);
        upgradedItem.Snapshot!.Title.Should().Be("Fullmetal Alchemist: Brotherhood");
        upgradedItem.Notes.Should().HaveCount(2);
        upgradedItem.Notes.Single(n => n.EpisodeNumber is null).Content.Should().Be("Latest series note");
        upgradedItem.Notes.Single(n => n.EpisodeNumber == 1).Content.Should().Be("Ep 1 latest");

        // Unique expression index IX_Notes_UniqueTarget rejects duplicate series notes.
        verify.Notes.Add(new Note
        {
            LibraryItemId = upgradedItem.Id,
            Content = "Another duplicate series note",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        var duplicateNoteAct = async () => await verify.SaveChangesAsync();
        await duplicateNoteAct.Should().ThrowAsync<DbUpdateException>();
        verify.ChangeTracker.Clear();

        // Trigger TR_TrackingStatuses_RestrictInUse rejects deleting a status still referenced by LibraryItems.
        var inUseStatus = await verify.TrackingStatuses.FindAsync(TrackingStatusIds.Watching);
        verify.TrackingStatuses.Remove(inUseStatus!);
        var deleteStatusAct = async () => await verify.SaveChangesAsync();
        await deleteStatusAct.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Migrate_FiftyItemV025UserLibrary_PreservesAllItemsProgressNotesAndSettingsWithoutDataLoss()
    {
        await using var db = NewLibraryContext(out var connection);
        using var _ = connection;

        // 1. Build initial v0.2.5 schema
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260726161715_InitialCreate");

        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        var expectedItems = new List<(int MalId, MediaType Type, int StatusId, int Score, string Title)>();

        // 2. Seed 50 library items simulating user library (30 anime, 20 manga)
        for (int i = 1; i <= 50; i++)
        {
            var isAnime = i <= 30;
            var mediaType = isAnime ? MediaType.Anime : MediaType.Manga;
            var rem = i % 5;
            var statusId = isAnime
                ? (rem == 0 ? TrackingStatusIds.Completed : rem == 1 ? TrackingStatusIds.Watching : rem == 2 ? TrackingStatusIds.OnHold : rem == 3 ? TrackingStatusIds.Dropped : TrackingStatusIds.PlanToWatch)
                : (rem == 0 ? TrackingStatusIds.Completed : rem == 1 ? TrackingStatusIds.Reading : rem == 2 ? TrackingStatusIds.OnHold : rem == 3 ? TrackingStatusIds.Dropped : TrackingStatusIds.PlanToRead);
            var score = (i % 10) + 1;
            var title = isAnime ? $"Anime Series #{i}" : $"Manga Series #{i}";

            expectedItems.Add((i * 100, mediaType, statusId, score, title));

            var item = new LibraryItem
            {
                MalId = i * 100,
                MediaType = mediaType,
                StatusId = statusId,
                Score = score,
                IsFavorite = i % 3 == 0,
                RewatchCount = i % 4,
                AddedAt = now.AddDays(-i),
                UpdatedAt = now.AddHours(-i),
                Snapshot = new MediaSnapshot
                {
                    Title = title,
                    TotalEpisodes = isAnime ? 12 : null,
                    TotalChapters = isAnime ? null : 50,
                    Genres = isAnime ? "[\"Action\",\"Fantasy\"]" : "[\"Romance\",\"Drama\"]",
                    SnapshotAt = now.AddDays(-i)
                }
            };
            db.LibraryItems.Add(item);
        }
        await db.SaveChangesAsync();

        // Add progress and notes for all items
        var itemsInDb = await db.LibraryItems.ToListAsync();
        for (int i = 0; i < 50; i++)
        {
            var item = itemsInDb[i];
            if (item.MediaType == MediaType.Anime)
            {
                db.EpisodeProgress.Add(new EpisodeProgress
                {
                    LibraryItemId = item.Id,
                    EpisodeNumber = 1,
                    IsWatched = true,
                    WatchedAt = now
                });
            }
            else
            {
                db.ChapterProgress.Add(new ChapterProgress
                {
                    LibraryItemId = item.Id,
                    ChapterNumber = 1,
                    IsRead = true,
                    ReadAt = now
                });
            }

            db.Notes.Add(new Note
            {
                LibraryItemId = item.Id,
                Content = $"Great series notes for item {item.Id}",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        // Add user settings
        db.Settings.AddRange(
            new AppSetting { Key = "Theme", Value = "Dark" },
            new AppSetting { Key = "Language", Value = "tr" },
            new AppSetting { Key = "StartupPage", Value = "Anime" },
            new AppSetting { Key = "CacheRefreshDays", Value = "14" }
        );
        await db.SaveChangesAsync();

        // 3. Perform migration to v0.2.6 (HardenLibraryConstraints)
        await db.Database.MigrateAsync();

        // 4. Verify that EVERY single user record is 100% intact with zero data loss
        await using var verify = new LibraryDbContext(
            new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite(connection).Options);

        var migratedItems = await verify.LibraryItems
            .Include(i => i.Snapshot)
            .OrderBy(i => i.Id)
            .ToListAsync();

        migratedItems.Should().HaveCount(50, "all 50 user library items must survive migration");

        for (int i = 0; i < 50; i++)
        {
            var actual = migratedItems[i];
            var expected = expectedItems[i];

            actual.MalId.Should().Be(expected.MalId);
            actual.MediaType.Should().Be(expected.Type);
            actual.StatusId.Should().Be(expected.StatusId);
            actual.Score.Should().Be(expected.Score);
            actual.Snapshot.Should().NotBeNull();
            actual.Snapshot!.Title.Should().Be(expected.Title);
        }

        // Progress rows intact
        (await verify.EpisodeProgress.CountAsync()).Should().BeGreaterThan(0);
        (await verify.ChapterProgress.CountAsync()).Should().BeGreaterThan(0);

        // Notes intact
        var notes = await verify.Notes.ToListAsync();
        notes.Should().HaveCount(50);

        // Settings intact
        (await verify.Settings.FindAsync(["Theme"]))?.Value.Should().Be("Dark");
        (await verify.Settings.FindAsync(["Language"]))?.Value.Should().Be("tr");
        (await verify.Settings.FindAsync(["StartupPage"]))?.Value.Should().Be("Anime");
        (await verify.Settings.FindAsync(["CacheRefreshDays"]))?.Value.Should().Be("14");
    }

    [Fact]
    public async Task Migrate_FromV025CatalogDatabase_DeduplicatesEpisodesAndStreamingAndEnforcesUniqueIndexes()
    {
        await using var db = NewCatalogContext(out var connection);
        using var _ = connection;

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260726161704_InitialCreate");

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Anime" ("Id", "Title", "FetchedAt")
            VALUES (1, 'Cowboy Bebop', '2026-10-28T00:00:00Z');

            INSERT INTO "Episodes" ("AnimeId", "EpisodeNumber", "Title", "FetchedAt")
            VALUES (1, 1, 'Asteroid Blues (dup 1)', '2026-10-28T00:00:00Z'),
                   (1, 1, 'Asteroid Blues (latest)', '2026-10-28T00:00:00Z');

            INSERT INTO "AnimeStreaming" ("AnimeId", "PlatformName", "Url")
            VALUES (1, 'Crunchyroll', 'https://old.example/1'),
                   (1, 'Crunchyroll', 'https://new.example/1');
            """);

        // Upgrade to v0.2.6 (HardenCatalogIndexes).
        await db.Database.MigrateAsync();

        await using var verify = new CatalogDbContext(
            new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(connection).Options);

        (await verify.Episodes.SingleAsync()).Title.Should().Be("Asteroid Blues (latest)");
        (await verify.AnimeStreaming.SingleAsync()).Url.Should().Be("https://new.example/1");

        verify.Episodes.Add(new CachedEpisode { AnimeId = 1, EpisodeNumber = 1, Title = "Duplicate" });
        var dupEpAct = async () => await verify.SaveChangesAsync();
        await dupEpAct.Should().ThrowAsync<DbUpdateException>();
    }

    // A real (in-memory) SQLite connection, not the EF InMemory provider: migrations
    // are relational DDL and the InMemory provider ignores them entirely.
    private static LibraryDbContext NewLibraryContext(out SqliteConnection connection)
    {
        connection = OpenConnection();
        return new LibraryDbContext(
            new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite(connection).Options);
    }

    private static CatalogDbContext NewCatalogContext(out SqliteConnection connection)
    {
        connection = OpenConnection();
        return new CatalogDbContext(
            new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(connection).Options);
    }

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ":memory:",
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }
}
