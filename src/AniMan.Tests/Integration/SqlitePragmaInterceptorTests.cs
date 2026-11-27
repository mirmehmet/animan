using AniMan.Infrastructure.Data;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniMan.Tests.Integration;

/// <summary>AM-118: the pragmas run once per pooled handle and stay set on it.</summary>
public sealed class SqlitePragmaInterceptorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"AniMan_pragma_{Guid.NewGuid():N}.db");
    private readonly SqlitePragmaInterceptor _interceptor = new();

    private LibraryDbContext Create(bool pooling) => new(new DbContextOptionsBuilder<LibraryDbContext>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = _dbPath, Pooling = pooling }.ToString())
        .AddInterceptors(_interceptor)
        .Options);

    private static async Task<string> PragmaAsync(LibraryDbContext db, string name)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    [Fact]
    public async Task FirstOpen_SetsWalAndNormalSync()
    {
        await using var db = Create(pooling: true);
        await db.Database.OpenConnectionAsync();

        (await PragmaAsync(db, "journal_mode")).Should().Be("wal");
        (await PragmaAsync(db, "synchronous")).Should().Be("1", "1 = NORMAL");
    }

    [Fact]
    public async Task PooledReopen_DoesNotRunThePragmasAgain()
    {
        await using (var first = Create(pooling: true))
        {
            await first.Database.OpenConnectionAsync();
            await using var change = first.Database.GetDbConnection().CreateCommand();
            change.CommandText = "PRAGMA synchronous=FULL;";
            await change.ExecuteNonQueryAsync();
        }

        await using var second = Create(pooling: true);
        await second.Database.OpenConnectionAsync();

        (await PragmaAsync(second, "synchronous")).Should().Be("2",
            "the pooled handle was reused and the interceptor skipped it (2 = FULL, set above)");
    }

    [Fact]
    public async Task UnpooledReopen_ConfiguresTheNewHandle()
    {
        await using (var first = Create(pooling: false))
            await first.Database.OpenConnectionAsync();

        await using var second = Create(pooling: false);
        await second.Database.OpenConnectionAsync();

        (await PragmaAsync(second, "synchronous")).Should().Be("1");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
