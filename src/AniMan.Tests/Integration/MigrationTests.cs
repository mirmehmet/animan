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
        using var connection = OpenConnection();
        await CreateV025LibraryAsync(connection);

        // Written the way v0.2.5 stored it, including its edge cases: a whitespace-only note
        // and duplicate series / episode notes.
        await ExecAsync(connection, """
            INSERT INTO LibraryItems (Id, MalId, MediaType, StatusId, Score, IsFavorite, RewatchCount, AddedAt, UpdatedAt)
            VALUES (1, 5114, 'Anime', 1, 10, 0, 0, '2026-10-28 12:00:00', '2026-10-28 12:00:00');
            INSERT INTO Snapshots (LibraryItemId, Title, TotalEpisodes, Genres, SnapshotAt)
            VALUES (1, 'Fullmetal Alchemist: Brotherhood', 64, '["Action","Adventure"]', '2026-10-28 12:00:00');
            INSERT INTO Notes (LibraryItemId, EpisodeNumber, Content, CreatedAt, UpdatedAt) VALUES
                (1, NULL, '   ',                '2026-10-28 11:57:00', '2026-10-28 11:57:00'),
                (1, NULL, 'Older series note',  '2026-10-28 11:58:00', '2026-10-28 11:58:00'),
                (1, NULL, 'Latest series note', '2026-10-28 11:59:00', '2026-10-28 11:59:00'),
                (1, 1,    'Ep 1 old',           '2026-10-28 11:58:00', '2026-10-28 11:58:00'),
                (1, 1,    'Ep 1 latest',        '2026-10-28 12:00:00', '2026-10-28 12:00:00');
            """);

        await using (var db = NewLibraryContext(connection))
            await db.Database.MigrateAsync();

        await using var verify = NewLibraryContext(connection);

        var upgradedItem = await verify.LibraryItems
            .Include(i => i.Snapshot)
            .Include(i => i.Notes)
            .SingleAsync();

        upgradedItem.MalId.Should().Be(5114);
        upgradedItem.Score.Should().Be(10);
        upgradedItem.Snapshot!.Title.Should().Be("Fullmetal Alchemist: Brotherhood");
        upgradedItem.Snapshot.TotalEpisodes.Should().Be(64);
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
        using var connection = OpenConnection();
        await CreateV025LibraryAsync(connection);

        // 50 items as v0.2.5 wrote them (30 anime, 20 manga), each with every column it had:
        // ratings, favourites, rewatches, dates, progress, a note, some streaming overrides.
        var now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        var expected = Enumerable.Range(1, 50).Select(i => V025Item.Make(i, now)).ToList();
        foreach (var e in expected)
            await e.InsertAsync(connection);

        await ExecAsync(connection, """
            INSERT INTO Settings (Key, Value) VALUES
                ('Theme', 'Dark'), ('Language', 'tr'), ('StartupPage', 'Anime'), ('CacheRefreshDays', '14');
            """);

        await using (var db = NewLibraryContext(connection))
            await db.Database.MigrateAsync();

        // Every user record must come through unchanged (AM-055: every column is checked).
        await using var verify = NewLibraryContext(connection);
        var migrated = await verify.LibraryItems.AsNoTracking()
            .Include(i => i.Snapshot)
            .Include(i => i.EpisodeProgress)
            .Include(i => i.ChapterProgress)
            .Include(i => i.Notes)
            .Include(i => i.StreamingOverrides)
            .OrderBy(i => i.Id)
            .ToListAsync();

        migrated.Should().HaveCount(50, "all 50 user library items must survive migration");
        foreach (var (actual, e) in migrated.Zip(expected))
            e.AssertMatches(actual);

        // Settings intact. Each row must exist: a `?.Value.Should()` chain would silently
        // skip the assertion if the migration lost the setting.
        async Task AssertSettingAsync(string key, string value)
        {
            var setting = await verify.Settings.FindAsync([key]);
            setting.Should().NotBeNull($"setting '{key}' must survive the migration");
            setting!.Value.Should().Be(value);
        }

        await AssertSettingAsync("Theme", "Dark");
        await AssertSettingAsync("Language", "tr");
        await AssertSettingAsync("StartupPage", "Anime");
        await AssertSettingAsync("CacheRefreshDays", "14");
    }

    /// <summary>One library item of the 50-item v0.2.5 library, with what it must read back as.</summary>
    private sealed record V025Item(
        int Id, int MalId, MediaType Type, int StatusId, int Score, decimal? UserRating, bool IsFavorite,
        int RewatchCount, DateOnly? StartedAt, DateOnly? CompletedAt, DateTime AddedAt, DateTime UpdatedAt,
        DateTime? DeletedAt, string Title, int? Episodes, int? Chapters, string Genres, DateTime SnapshotAt,
        int Progress, DateTime ProgressAt, string Note, string? Override)
    {
        public static V025Item Make(int i, DateTime now)
        {
            var isAnime = i <= 30;
            var rem = i % 5;
            var status = isAnime
                ? (rem == 0 ? TrackingStatusIds.Completed : rem == 1 ? TrackingStatusIds.Watching : rem == 2 ? TrackingStatusIds.OnHold : rem == 3 ? TrackingStatusIds.Dropped : TrackingStatusIds.PlanToWatch)
                : (rem == 0 ? TrackingStatusIds.Completed : rem == 1 ? TrackingStatusIds.Reading : rem == 2 ? TrackingStatusIds.OnHold : rem == 3 ? TrackingStatusIds.Dropped : TrackingStatusIds.PlanToRead);
            return new V025Item(
                Id: i, MalId: i * 100, Type: isAnime ? MediaType.Anime : MediaType.Manga, StatusId: status,
                Score: (i % 10) + 1, UserRating: i % 4 == 0 ? 7.5m + i % 3 : null, IsFavorite: i % 3 == 0,
                RewatchCount: i % 4, StartedAt: new DateOnly(2026, 1, 1).AddDays(i),
                CompletedAt: rem == 0 ? new DateOnly(2026, 6, 1).AddDays(i) : null,
                AddedAt: now.AddDays(-i), UpdatedAt: now.AddHours(-i), DeletedAt: i == 49 ? now.AddDays(-2) : null,
                Title: isAnime ? $"Anime Series #{i}" : $"Manga Series #{i}",
                Episodes: isAnime ? 12 : null, Chapters: isAnime ? null : 50,
                Genres: isAnime ? """["Action","Fantasy"]""" : """["Romance","Drama"]""", SnapshotAt: now.AddDays(-i),
                Progress: i % 12 + 1, ProgressAt: now.AddMinutes(-i),
                Note: $"Great series notes for item {i}", Override: isAnime && i % 10 == 0 ? "Crunchyroll" : null);
        }

        public async Task InsertAsync(SqliteConnection connection)
        {
            await ExecAsync(connection, """
                INSERT INTO LibraryItems (Id, MalId, MediaType, StatusId, Score, IsFavorite, RewatchCount,
                    StartedAt, CompletedAt, UserRating, AddedAt, UpdatedAt, DeletedAt)
                VALUES ($id, $mal, $type, $status, $score, $fav, $rewatch, $started, $completed, $rating, $added, $updated, $deleted);
                INSERT INTO Snapshots (LibraryItemId, Title, TotalEpisodes, TotalChapters, Genres, SnapshotAt)
                VALUES ($id, $title, $episodes, $chapters, $genres, $snapAt);
                INSERT INTO Notes (LibraryItemId, Content, CreatedAt, UpdatedAt) VALUES ($id, $note, $added, $added);
                """,
                ("$id", Id), ("$mal", MalId), ("$type", Type.ToString()), ("$status", StatusId), ("$score", Score),
                ("$fav", IsFavorite ? 1 : 0), ("$rewatch", RewatchCount), ("$started", Date(StartedAt)),
                ("$completed", Date(CompletedAt)), ("$rating", UserRating?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("$added", Time(AddedAt)), ("$updated", Time(UpdatedAt)), ("$deleted", DeletedAt is { } d ? Time(d) : null),
                ("$title", Title), ("$episodes", Episodes), ("$chapters", Chapters), ("$genres", Genres),
                ("$snapAt", Time(SnapshotAt)), ("$note", Note));

            var progressSql = Type == MediaType.Anime
                ? "INSERT INTO EpisodeProgress (LibraryItemId, EpisodeNumber, IsWatched, WatchedAt) VALUES ($id, $n, 1, $at);"
                : "INSERT INTO ChapterProgress (LibraryItemId, ChapterNumber, IsRead, ReadAt) VALUES ($id, $n, 1, $at);";
            await ExecAsync(connection, progressSql, ("$id", Id), ("$n", Progress), ("$at", Time(ProgressAt)));

            if (Override is not null)
                await ExecAsync(connection,
                    "INSERT INTO StreamingOverrides (LibraryItemId, PlatformName, Url, AddedAt) VALUES ($id, $name, 'https://www.crunchyroll.com/', $at);",
                    ("$id", Id), ("$name", Override), ("$at", Time(AddedAt)));
        }

        public void AssertMatches(LibraryItem actual)
        {
            actual.MalId.Should().Be(MalId);
            actual.MediaType.Should().Be(Type);
            actual.StatusId.Should().Be(StatusId);
            actual.Score.Should().Be(Score);
            actual.UserRating.Should().Be(UserRating);
            actual.IsFavorite.Should().Be(IsFavorite);
            actual.RewatchCount.Should().Be(RewatchCount);
            actual.StartedAt.Should().Be(StartedAt);
            actual.CompletedAt.Should().Be(CompletedAt);
            actual.AddedAt.Should().Be(AddedAt);
            actual.UpdatedAt.Should().Be(UpdatedAt);
            actual.DeletedAt.Should().Be(DeletedAt, "trashed items stay trashed");

            actual.Snapshot.Should().NotBeNull();
            actual.Snapshot!.Title.Should().Be(Title);
            actual.Snapshot.TotalEpisodes.Should().Be(Episodes);
            actual.Snapshot.TotalChapters.Should().Be(Chapters);
            actual.Snapshot.Genres.Should().Be(Genres);
            actual.Snapshot.SnapshotAt.Should().Be(SnapshotAt);
            actual.Snapshot.DataVersion.Should().Be(MediaDataVersion.Legacy);

            if (Type == MediaType.Anime)
            {
                var ep = actual.EpisodeProgress.Should().ContainSingle().Subject;
                ep.EpisodeNumber.Should().Be(Progress);
                ep.IsWatched.Should().BeTrue();
                ep.WatchedAt.Should().Be(ProgressAt);
                actual.ChapterProgress.Should().BeEmpty();
            }
            else
            {
                var ch = actual.ChapterProgress.Should().ContainSingle().Subject;
                ch.ChapterNumber.Should().Be(Progress);
                ch.IsRead.Should().BeTrue();
                ch.ReadAt.Should().Be(ProgressAt);
                actual.EpisodeProgress.Should().BeEmpty();
            }

            var note = actual.Notes.Should().ContainSingle().Subject;
            note.Content.Should().Be(Note);
            note.EpisodeNumber.Should().BeNull();

            if (Override is null)
                actual.StreamingOverrides.Should().BeEmpty();
            else
                actual.StreamingOverrides.Should().ContainSingle(o => o.PlatformName == Override && o.Url == "https://www.crunchyroll.com/");
        }

        // v0.2.5's EF Core wrote times and dates in these formats.
        private static string Time(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        private static string? Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Builds an empty library.db exactly as v0.2.5 created it, from the frozen fixture.</summary>
    private static Task CreateV025LibraryAsync(SqliteConnection connection) =>
        ExecAsync(connection, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "library-v0.2.5.sql")));

    private static async Task ExecAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
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

        // AddExtendedMetadata: rows cached before it read as Legacy, so they get refreshed (AM-056).
        (await verify.Anime.SingleAsync()).DataVersion.Should().Be(MediaDataVersion.Legacy);
        (await verify.Episodes.SingleAsync()).Title.Should().Be("Asteroid Blues (latest)");
        (await verify.AnimeStreaming.SingleAsync()).Url.Should().Be("https://new.example/1");

        verify.Episodes.Add(new CachedEpisode { AnimeId = 1, EpisodeNumber = 1, Title = "Duplicate" });
        var dupEpAct = async () => await verify.SaveChangesAsync();
        await dupEpAct.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Migrate_FromV040CatalogDatabase_DropsUnusedGenreColumnsAndKeepsGenreLinks()
    {
        await using var db = NewCatalogContext(out var connection);
        using var _ = connection;

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261122153917_AddExtendedMetadata");

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "Anime" ("Id", "Title", "FetchedAt") VALUES (1, 'Cowboy Bebop', '2026-11-01 00:00:00');
            INSERT INTO "Manga" ("Id", "Title", "FetchedAt") VALUES (2, 'Berserk', '2026-11-01 00:00:00');
            INSERT INTO "Genres" ("Id", "Name", "MediaType") VALUES (1, 'Action', 'anime'), (2, 'Drama', 'manga');
            INSERT INTO "MediaGenres" ("MediaId", "MediaType", "GenreId") VALUES (1, 'anime', 1), (2, 'manga', 2);
            """);

        await db.Database.MigrateAsync();

        await using var verify = new CatalogDbContext(
            new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(connection).Options);
        (await verify.GenreNamesOf(1, "anime").ToListAsync()).Should().Equal("Action");
        (await verify.GenreNamesOf(2, "manga").ToListAsync()).Should().Equal("Drama");
        (await ColumnNamesAsync(connection, "MediaGenres")).Should().BeEquivalentTo(["MediaId", "MediaType", "GenreId"]);
    }

    [Fact]
    public async Task Migrate_FromV040LibraryDatabase_AddsListIndexAndKeepsTheStatusGuard()
    {
        await using var db = NewLibraryContext(out var connection);
        using var _ = connection;

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261122153921_AddExtendedSnapshotMetadata");

        var now = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
        db.LibraryItems.Add(new LibraryItem
        {
            MalId = 1, MediaType = MediaType.Anime, StatusId = TrackingStatusIds.Watching,
            IsFavorite = true, AddedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();

        await db.Database.MigrateAsync();

        await using var verify = new LibraryDbContext(
            new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite(connection).Options);
        var item = await verify.LibraryItems.AsNoTracking().SingleAsync();
        item.IsFavorite.Should().BeTrue();
        item.AddedAt.Should().Be(now);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_LibraryItems_MediaType_DeletedAt';""";
            ((long)(await command.ExecuteScalarAsync())!).Should().Be(1);
        }

        // AM-048: the FK was not rebuilt, so the trigger still guards statuses in use…
        var watching = await verify.TrackingStatuses.FindAsync(TrackingStatusIds.Watching);
        verify.TrackingStatuses.Remove(watching!);
        var act = async () => await verify.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        // …and EF now refuses too, instead of cascade-deleting a loaded item (the old model).
        await using var tracked = new LibraryDbContext(
            new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite(connection).Options);
        var loaded = await tracked.LibraryItems.Include(i => i.Status).SingleAsync();
        var remove = () => tracked.TrackingStatuses.Remove(loaded.Status!);
        remove.Should().Throw<InvalidOperationException>();
    }

    private static async Task<List<string>> ColumnNamesAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT name FROM pragma_table_info('{table}');""";
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names;
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

    private static LibraryDbContext NewLibraryContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<LibraryDbContext>().UseSqlite(connection).Options);

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
