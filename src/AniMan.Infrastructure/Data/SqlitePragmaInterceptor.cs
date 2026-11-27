using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SQLitePCL;

namespace AniMan.Infrastructure.Data;

/// <summary>
/// Configures SQLite connections for concurrent desktop read/write workloads
/// (<c>WAL</c> journal mode + <c>NORMAL</c> synchronous durability).
/// </summary>
/// <remarks>
/// Pooled connections reuse the same native handle on every open, and these settings stay on
/// that handle, so they run once per handle instead of on each open (AM-118).
/// </remarks>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string PragmaSql = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";

    // Weak keys: a handle closed by the pool drops out on its own.
    private readonly ConditionalWeakTable<sqlite3, object> _configured = new();

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (IsConfigured(connection)) return;

        using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        command.ExecuteNonQuery();
        MarkConfigured(connection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (IsConfigured(connection)) return;

        await using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        MarkConfigured(connection);
    }

    private bool IsConfigured(DbConnection connection) =>
        connection is SqliteConnection { Handle: { } handle } && _configured.TryGetValue(handle, out _);

    private void MarkConfigured(DbConnection connection)
    {
        if (connection is SqliteConnection { Handle: { } handle })
            _configured.AddOrUpdate(handle, true);
    }
}
