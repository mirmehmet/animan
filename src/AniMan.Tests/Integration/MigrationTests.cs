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
            UpdatedAt = now
        };
        db.LibraryItems.Add(item);
        await db.SaveChangesAsync();
        await InsertLegacySnapshotAsync(db, item.Id, "Fullmetal Alchemist: Brotherhood",
            totalEpisodes: 64, totalChapters: null, """["Action","Adventure"]""", now);

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
        upgradedItem.Snapshot.Genres.Should().Be("""["Action","Adventure"]""");
        // Pre-existing snapshots come through as Legacy so the backfill completes them.
        upgradedItem.Snapshot.DataVersion.Should().Be(MediaDataVersion.Legacy);
        upgradedItem.Snapshot.TitleEnglish.Should().BeNull();
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
        var legacySnapshots = new Dictionary<int, (string Title, int? Episodes, int? Chapters, string Genres, DateTime At)>();

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
                UpdatedAt = now.AddHours(-i)
            };
            db.LibraryItems.Add(item);
            legacySnapshots[i * 100] = (title, isAnime ? 12 : null, isAnime ? null : 50,
                isAnime ? "[\"Action\",\"Fantasy\"]" : "[\"Romance\",\"Drama\"]", now.AddDays(-i));
        }
        await db.SaveChangesAsync();

        foreach (var seeded in await db.LibraryItems.AsNoTracking().ToListAsync())
        {
            var snap = legacySnapshots[seeded.MalId];
            await InsertLegacySnapshotAsync(db, seeded.Id, snap.Title, snap.Episodes, snap.Chapters, snap.Genres, snap.At);
        }

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
            actual.Snapshot.Genres.Should().Be(legacySnapshots[expected.MalId].Genres);
            actual.Snapshot.DataVersion.Should().Be(MediaDataVersion.Legacy);
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

    /// <summary>
    /// The upgrade every current user takes: a v0.2.6+ library (HardenLibraryConstraints)
    /// gaining the extended snapshot columns. Nothing the user entered may change; existing
    /// snapshots must read as Legacy (so the backfill picks them up) and the new columns
    /// must be writable afterwards.
    /// </summary>
    [Fact]
    public async Task Migrate_FromV026LibraryDatabase_AddsExtendedSnapshotColumnsWithoutTouchingUserData()
    {
        await using var db = NewLibraryContext(out var connection);
        using var _ = connection;

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261028195010_HardenLibraryConstraints");

        var now = new DateTime(2026, 11, 1, 9, 30, 0, DateTimeKind.Utc);
        var anime = new LibraryItem
        {
            MalId = 16498, MediaType = MediaType.Anime, StatusId = TrackingStatusIds.Completed,
            Score = 9, UserRating = 8.5m, IsFavorite = true, RewatchCount = 2,
            StartedAt = new DateOnly(2026, 1, 3), CompletedAt = new DateOnly(2026, 2, 14),
            AddedAt = now, UpdatedAt = now
        };
        var manga = new LibraryItem
        {
            MalId = 2, MediaType = MediaType.Manga, StatusId = TrackingStatusIds.Reading,
            AddedAt = now, UpdatedAt = now, DeletedAt = now.AddDays(1) // in Trash
        };
        db.LibraryItems.AddRange(anime, manga);
        await db.SaveChangesAsync();

        await InsertLegacySnapshotAsync(db, anime.Id, "Shingeki no Kyojin", 25, null, """["Action","Drama"]""", now);
        await InsertLegacySnapshotAsync(db, manga.Id, "Berserk", null, 380, """["Action","Adventure"]""", now);

        db.EpisodeProgress.AddRange(
            new EpisodeProgress { LibraryItemId = anime.Id, EpisodeNumber = 1, IsWatched = true, WatchedAt = now },
            new EpisodeProgress { LibraryItemId = anime.Id, EpisodeNumber = 2, IsWatched = true, WatchedAt = now });
        db.ChapterProgress.Add(new ChapterProgress { LibraryItemId = manga.Id, ChapterNumber = 10, IsRead = true, ReadAt = now });
        db.Notes.Add(new Note { LibraryItemId = anime.Id, Content = "Rewatch before S4", CreatedAt = now, UpdatedAt = now });
        db.StreamingOverrides.Add(new UserStreamingOverride { LibraryItemId = anime.Id, PlatformName = "Crunchyroll", AddedAt = now });
        db.Settings.Add(new AppSetting { Key = "Language", Value = "tr" });
        await db.SaveChangesAsync();

        await db.Database.MigrateAsync();

        await using (var verify = new LibraryDbContext(
            new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite(connection).Options))
        {
            var items = await verify.LibraryItems
                .Include(i => i.Snapshot)
                .Include(i => i.EpisodeProgress)
                .Include(i => i.ChapterProgress)
                .Include(i => i.Notes)
                .Include(i => i.StreamingOverrides)
                .OrderBy(i => i.MalId)
                .ToListAsync();

            items.Should().HaveCount(2);
            var upgradedManga = items[0];
            var upgradedAnime = items[1];

            upgradedAnime.Score.Should().Be(9);
            upgradedAnime.UserRating.Should().Be(8.5m);
            upgradedAnime.IsFavorite.Should().BeTrue();
            upgradedAnime.RewatchCount.Should().Be(2);
            upgradedAnime.StartedAt.Should().Be(new DateOnly(2026, 1, 3));
            upgradedAnime.CompletedAt.Should().Be(new DateOnly(2026, 2, 14));
            upgradedAnime.EpisodeProgress.Should().HaveCount(2);
            upgradedAnime.Notes.Single().Content.Should().Be("Rewatch before S4");
            upgradedAnime.StreamingOverrides.Single().PlatformName.Should().Be("Crunchyroll");
            upgradedAnime.Snapshot!.Title.Should().Be("Shingeki no Kyojin");
            upgradedAnime.Snapshot.Genres.Should().Be("""["Action","Drama"]""");

            upgradedManga.DeletedAt.Should().NotBeNull("trashed items stay trashed");
            upgradedManga.ChapterProgress.Single().ChapterNumber.Should().Be(10);

            items.Should().AllSatisfy(i =>
            {
                i.Snapshot!.DataVersion.Should().Be(MediaDataVersion.Legacy);
                i.Snapshot.TitleEnglish.Should().BeNull();
                i.Snapshot.Themes.Should().BeNull();
                i.Snapshot.DurationMinutes.Should().BeNull();
            });
            (await verify.Settings.FindAsync(["Language"]))!.Value.Should().Be("tr");

            // The new columns are writable once migrated.
            upgradedAnime.Snapshot.TitleEnglish = "Attack on Titan";
            upgradedAnime.Snapshot.DurationMinutes = 24;
            upgradedAnime.Snapshot.DataVersion = MediaDataVersion.Current;
            await verify.SaveChangesAsync();
        }

        await using var reread = new LibraryDbContext(
            new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite(connection).Options);
        var snapshot = await reread.Snapshots.SingleAsync(s => s.Title == "Shingeki no Kyojin");
        snapshot.TitleEnglish.Should().Be("Attack on Titan");
        snapshot.DurationMinutes.Should().Be(24);
        snapshot.DataVersion.Should().Be(MediaDataVersion.Current);
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
    /// <summary>
    /// Writes a snapshot exactly as the original (v0.2.5–v0.2.x) schema stored it, with raw
    /// SQL over the original columns only. Going through the current EF model would try to
    /// write columns the legacy table does not have yet — and would not reflect what a real
    /// user's old database contains.
    /// </summary>
    private static Task InsertLegacySnapshotAsync(
        LibraryDbContext db, int libraryItemId, string title,
        int? totalEpisodes, int? totalChapters, string genres, DateTime snapshotAt) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Snapshots (LibraryItemId, Title, TotalEpisodes, TotalChapters, Genres, SnapshotAt)
            VALUES ({libraryItemId}, {title}, {totalEpisodes}, {totalChapters}, {genres}, {snapshotAt})
            """);

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
