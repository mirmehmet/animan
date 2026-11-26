namespace AniMan.Core.Domain;

/// <summary>
/// Which generation of metadata a catalog row or library snapshot carries.
/// <para>
/// Version 1 is everything stored before the extended fields existed (English title,
/// themes, demographics, duration, age rating, source, authors). Rows written by the
/// current mapper carry <see cref="Current"/>. Anything older is treated as incomplete:
/// catalog rows are refreshed on next access and library snapshots are filled in by the
/// background backfill — never by discarding what the user already has.
/// </para>
/// </summary>
public static class MediaDataVersion
{
    /// <summary>
    /// Data from the fallback source (AniList) while the primary was unavailable: no MAL rank,
    /// themes, demographics or age rating, and an AniList score. It never overwrites MAL data
    /// and is refreshed like Legacy. Negative on purpose: 0 is the CLR default, and the columns'
    /// database default (Legacy) would silently replace it on insert.
    /// </summary>
    public const int Partial = -1;

    /// <summary>Data written before extended metadata existed.</summary>
    public const int Legacy = 1;

    /// <summary>Data that includes the extended metadata fields.</summary>
    public const int Current = 2;
}
