using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Infrastructure.Data;
using AniMan.Tests.TestDoubles;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AniMan.Tests.Integration;

/// <summary>An app update must never migrate the user's library without a copy of it.</summary>
public sealed class LibraryMigrationBackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"animan_backup_{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly string _backups;

    public LibraryMigrationBackupTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "library.db");
        _backups = Path.Combine(_dir, "backups");
    }

    private LibraryDbContext Open() => new(new DbContextOptionsBuilder<LibraryDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = _dbPath, Pooling = false }.ToString())
        .Options);

    /// <summary>A library at the first schema version, with one item: an "older app" database.</summary>
    private async Task CreateOlderLibraryAsync()
    {
        await using var db = Open();
        var first = db.Database.GetMigrations().First();
        await db.GetService<IMigrator>().MigrateAsync(first);
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO LibraryItems (MalId, MediaType, StatusId, IsFavorite, RewatchCount, AddedAt, UpdatedAt) " +
            "VALUES (5114, 'Anime', 1, 0, 0, '2026-01-01', '2026-01-01')");
    }

    [Fact]
    public async Task PendingMigration_CopiesTheLibraryFirst()
    {
        await CreateOlderLibraryAsync();

        await using var db = Open();
        var backup = await LibraryMigrationBackup.BackupIfMigrationsPendingAsync(db, _backups);

        backup.Should().NotBeNull();
        await using var copy = new LibraryDbContextReader(backup!);
        copy.CountItems().Should().Be(1, "the copy holds the user's data as it was before the update");
    }

    [Fact]
    public async Task NoPendingMigration_MakesNoCopy()
    {
        await using (var db = Open())
            await db.Database.MigrateAsync();

        await using var current = Open();
        (await LibraryMigrationBackup.BackupIfMigrationsPendingAsync(current, _backups)).Should().BeNull();
        Directory.Exists(_backups).Should().BeFalse();
    }

    [Fact]
    public async Task FreshInstall_MakesNoCopy()
    {
        await using var db = Open();
        (await LibraryMigrationBackup.BackupIfMigrationsPendingAsync(db, _backups)).Should().BeNull();
    }

    [Fact]
    public async Task OnlyTheNewestCopiesAreKept()
    {
        await CreateOlderLibraryAsync();
        var time = new FakeTimeProvider();

        for (var i = 0; i < LibraryMigrationBackup.KeepCount + 2; i++)
        {
            await using var db = Open();
            await LibraryMigrationBackup.BackupIfMigrationsPendingAsync(db, _backups, time);
            time.Advance(TimeSpan.FromMinutes(1));
        }

        Directory.GetFiles(_backups).Should().HaveCount(LibraryMigrationBackup.KeepCount);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>Reads a backup file without EF, so the check does not depend on its schema version.</summary>
    private sealed class LibraryDbContextReader(string path) : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = Open(path);

        private static SqliteConnection Open(string path)
        {
            var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            c.Open();
            return c;
        }

        public long CountItems()
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM LibraryItems";
            return (long)cmd.ExecuteScalar()!;
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
