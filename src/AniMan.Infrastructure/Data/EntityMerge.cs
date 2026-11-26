using AniMan.Core.Domain;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace AniMan.Infrastructure.Data;

/// <summary>
/// The one rule for writing fresh media data over a stored row: never lose information.
/// A null in the incoming data means "the source did not say", not "this is now empty",
/// and data from the fallback source never replaces data from MyAnimeList.
/// </summary>
internal static class EntityMerge
{
    /// <summary>
    /// True when the incoming row may only fill gaps: it is partial (fallback source) and the
    /// stored row is not — rank, themes, the MAL score and so on must survive.
    /// </summary>
    public static bool FillOnly(int storedVersion, int incomingVersion) =>
        incomingVersion == MediaDataVersion.Partial && storedVersion != MediaDataVersion.Partial;

    /// <summary>
    /// Copies <paramref name="incoming"/> onto the tracked entity. Nulls in the incoming data
    /// never overwrite. With <paramref name="fillOnly"/> only properties that are still null are
    /// set, so non-nullable values (data version, fetch time) also keep the stored row's state.
    /// Keys and shadow properties are left alone.
    /// </summary>
    public static void Apply<T>(EntityEntry<T> stored, T incoming, bool fillOnly) where T : class
    {
        foreach (var property in stored.Properties)
        {
            if (property.Metadata.IsPrimaryKey() || property.Metadata.IsShadowProperty())
                continue;

            var info = property.Metadata.PropertyInfo;
            if (info is null)
                continue;

            var value = info.GetValue(incoming);
            if (value is null)
                continue;
            if (fillOnly && property.CurrentValue is not null)
                continue;

            property.CurrentValue = value;
        }
    }
}
