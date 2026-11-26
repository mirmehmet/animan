using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniMan.Infrastructure.Data;

/// <summary>
/// Before an app update changes the library schema, a copy of <c>library.db</c> is kept in
/// <c>backups/</c> — the user's only copy of their library is never migrated without one.
/// Nothing happens on ordinary launches (no pending migration) or on a fresh install.
/// </summary>
public static class LibraryMigrationBackup
{
    /// <summary>How many pre-update copies are kept; older ones are deleted.</summary>
    public const int KeepCount = 3;

    private const string FilePattern = "library-*-before-update.db";

    /// <returns>The backup's path, or null when no backup was needed.</returns>
    public static async Task<string?> BackupIfMigrationsPendingAsync(
        LibraryDbContext db, string backupsDirectory, TimeProvider? time = null, CancellationToken ct = default)
    {
        var pending = await db.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false);
        if (!pending.Any())
            return null;

        var applied = await db.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false);
        if (!applied.Any())
            return null; // fresh install: there is no library yet

        Directory.CreateDirectory(backupsDirectory);
        var stamp = (time ?? TimeProvider.System).GetUtcNow()
            .ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(backupsDirectory, $"library-{stamp}-before-update.db");

        // SQLite's online backup copies a consistent image including changes still in the
        // WAL file, which a plain file copy of library.db would miss.
        await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);
        try
        {
            var source = (SqliteConnection)db.Database.GetDbConnection();
            await using var target = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            await target.OpenAsync(ct).ConfigureAwait(false);
            source.BackupDatabase(target);
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }

        DeleteOldBackups(backupsDirectory);
        return path;
    }

    private static void DeleteOldBackups(string backupsDirectory)
    {
        // The timestamp in the name sorts chronologically.
        var old = Directory.GetFiles(backupsDirectory, FilePattern)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(KeepCount);
        foreach (var file in old)
        {
            try { File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* next launch retries */ }
        }
    }
}
