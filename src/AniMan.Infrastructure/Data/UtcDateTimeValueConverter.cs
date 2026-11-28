using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AniMan.Infrastructure.Data;

/// <summary>
/// Every time the app stores is UTC, but SQLite keeps no kind, so values used to come back
/// as <see cref="DateTimeKind.Unspecified"/> and each reader had to remember (AM-090). Reads
/// are now marked UTC; a local time is converted before it is written. The stored text is
/// unchanged, so existing databases and backups read the same.
/// </summary>
public sealed class UtcDateTimeValueConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
{
    /// <summary>Applies the converter to every <see cref="DateTime"/> property of a context.</summary>
    public static void Register(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeValueConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeValueConverter>();
    }
}
