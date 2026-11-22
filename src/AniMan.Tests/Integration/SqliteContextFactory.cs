using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniMan.Tests.Integration;

/// <summary>
/// An <see cref="IDbContextFactory{TContext}"/> over a private, named in-memory SQLite
/// database. Unlike the EF Core InMemory provider this exercises real relational behaviour:
/// foreign keys, unique indexes, SQL triggers, and cascade deletes. Migrations run once at
/// construction; the database lives until <see cref="Dispose"/>.
/// <para>
/// Every context gets its <b>own connection</b> to the shared-cache database, the way the
/// app's factories hand out their own pooled connections. Services such as CatalogService
/// write from fire-and-forget background tasks; with one connection shared by every
/// context those writes raced the test thread on a non-thread-safe SqliteConnection and
/// failed intermittently ("unable to delete/modify user-function due to active statements").
/// Separate connections leave concurrency to SQLite's own locking, which retries on busy.
/// </para>
/// </summary>
internal sealed class SqliteContextFactory<TContext> : IDbContextFactory<TContext>, IDisposable
    where TContext : DbContext
{
    /// <summary>An in-memory database exists only while a connection to it is open.</summary>
    private readonly SqliteConnection _keepAlive;
    private readonly string _connectionString;
    private readonly Func<DbContextOptions<TContext>, TContext> _ctor;

    public SqliteContextFactory(Func<DbContextOptions<TContext>, TContext> ctor)
    {
        _ctor = ctor;
        _connectionString = new SqliteConnectionStringBuilder
        {
            // Unique name: test classes run in parallel and must never share a database.
            DataSource = $"animan-test-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();

        using var ctx = CreateDbContext();
        ctx.Database.Migrate();
    }

    public TContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseSqlite(_connectionString)
            .Options;
        return _ctor(options);
    }

    public Task<TContext> CreateDbContextAsync(CancellationToken ct = default) =>
        Task.FromResult(CreateDbContext());

    public void Dispose()
    {
        try
        {
            _keepAlive.Dispose();
        }
        catch
        {
            // Teardown only; a failure here must not mask the test result.
        }
    }
}
