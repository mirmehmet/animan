using System.Data.Common;
using System.Text.RegularExpressions;
using AniMan.Core.Interfaces;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AniMan.Infrastructure.Data;

/// <summary>
/// Watches every command sent to <c>library.db</c> and bumps <see cref="Current"/> when one
/// writes library data. Sitting at the command level means no writer can forget to report a
/// change: <c>SaveChanges</c>, <c>ExecuteDelete</c>/<c>ExecuteUpdate</c> and raw SQL all pass
/// through here. Writes to the <c>Settings</c> table are ignored — a slider or sort change must
/// not make the library pages reload. Counting a write that is later rolled back only costs
/// one extra reload.
/// </summary>
public sealed partial class LibraryWriteInterceptor : DbCommandInterceptor, ILibraryVersion
{
    private long _version;

    public long Current => Interlocked.Read(ref _version);

    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Observe(command);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Observe(command);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Observe(command);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Observe(command);
        return ValueTask.FromResult(result);
    }

    private void Observe(DbCommand command)
    {
        if (WritesLibraryData(command.CommandText))
            Interlocked.Increment(ref _version);
    }

    /// <summary>
    /// <c>true</c> when any statement in <paramref name="sql"/> inserts, updates or deletes rows
    /// of a table other than <c>Settings</c>. EF batches several statements into one command,
    /// separated by semicolons; its values travel as parameters, so splitting is safe.
    /// </summary>
    internal static bool WritesLibraryData(string? sql)
    {
        if (string.IsNullOrEmpty(sql)) return false;

        foreach (var statement in sql.Split(';'))
        {
            var match = WriteTarget().Match(statement);
            if (match.Success && !string.Equals(match.Groups["table"].Value, "Settings", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    [GeneratedRegex("""^\s*(?:INSERT(?:\s+OR\s+\w+)?\s+INTO|REPLACE\s+INTO|UPDATE(?:\s+OR\s+\w+)?|DELETE\s+FROM)\s+["`\[]?(?<table>\w+)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WriteTarget();
}
