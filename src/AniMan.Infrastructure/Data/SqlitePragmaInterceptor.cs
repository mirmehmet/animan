using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AniMan.Infrastructure.Data;

/// <summary>
/// Configures SQLite connections for concurrent desktop read/write workloads
/// (<c>WAL</c> journal mode + <c>NORMAL</c> synchronous durability).
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string PragmaSql = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = PragmaSql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

