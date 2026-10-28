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
            ForeignKeys = true
        }.ToString());
        connection.Open();
        return connection;
    }
}
