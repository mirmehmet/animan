using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AniTrack.Tests.Integration;

/// <summary>
/// An <see cref="IDbContextFactory{TContext}"/> backed by a single, kept-open
/// in-memory SQLite connection. Unlike the EF Core InMemory provider this exercises
/// real relational behaviour: foreign keys, unique indexes and cascade deletes.
/// The shared connection lives until <see cref="Dispose"/>; the schema is created once.
/// </summary>
internal sealed class SqliteContextFactory<TContext> : IDbContextFactory<TContext>, IDisposable
    where TContext : DbContext
{
    private readonly SqliteConnection _connection;
    private readonly Func<DbContextOptions<TContext>, TContext> _ctor;

    public SqliteContextFactory(Func<DbContextOptions<TContext>, TContext> ctor)
    {
        _ctor = ctor;
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var ctx = CreateDbContext();
        ctx.Database.EnsureCreated();
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

    public void Dispose() => _connection.Dispose();
}
