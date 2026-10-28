using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniMan.Tests.Integration;

/// <summary>
/// An <see cref="IDbContextFactory{TContext}"/> backed by a single, kept-open
/// in-memory SQLite connection. Unlike the EF Core InMemory provider this exercises
/// real relational behaviour: foreign keys, unique indexes, SQL triggers, and cascade deletes.
/// The shared connection lives until <see cref="Dispose"/>; migrations run once at construction.
/// </summary>
internal sealed class SqliteContextFactory<TContext> : IDbContextFactory<TContext>, IDisposable
    where TContext : DbContext
{
    private readonly SqliteConnection _connection;
    private readonly Func<DbContextOptions<TContext>, TContext> _ctor;

    public SqliteContextFactory(Func<DbContextOptions<TContext>, TContext> ctor)
    {
        _ctor = ctor;
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = ":memory:",
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        using var ctx = CreateDbContext();
        ctx.Database.Migrate();
    }

    public TContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseSqlite(_connection)
            .Options;
        return _ctor(options);
    }

    public Task<TContext> CreateDbContextAsync(CancellationToken ct = default) =>
        Task.FromResult(CreateDbContext());

    public void Dispose()
    {
        try
        {
            _connection.Dispose();
        }
        catch
        {
            // Suppress teardown exceptions on already closed in-memory connections
        }
    }
}
