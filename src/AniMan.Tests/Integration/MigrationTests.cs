using AniMan.Core.Domain;
using AniMan.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniMan.Tests.Integration;

/// <summary>
/// Guards the failure that shipped in v0.2.1: <c>Migrations/</c> was gitignored, so
/// every build carried zero migrations. <c>Database.MigrateAsync()</c> at startup then
/// found nothing to apply and created only EF's own bookkeeping tables, after which
/// every query failed with "no such table" — while the app reported no error at all.
/// These tests run the real migrations against an empty database, so deleting or
/// re-ignoring them breaks the build instead of silently shipping an empty schema.
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
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        return connection;
    }
}
