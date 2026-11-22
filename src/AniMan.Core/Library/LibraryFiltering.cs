namespace AniMan.Core.Library;

/// <summary>What the library filter and sort need to know about one library item.</summary>
public interface ILibraryEntry
{
    string Title { get; }
    /// <summary>TV, Movie, OVA… for anime; Manga, Light Novel, One-shot… for manga.</summary>
    string? Format { get; }
    int? Year { get; }
    IReadOnlyList<string> Genres { get; }
    IReadOnlyList<string> Themes { get; }
    IReadOnlyList<string> Demographics { get; }
    /// <summary>Raw source status, e.g. "Finished Airing", "Publishing".</summary>
    string? ReleaseStatus { get; }
    string? Source { get; }
    /// <summary>Short code from <see cref="AgeRatingCodes.Shorten"/>, e.g. "PG-13".</summary>
    string? AgeRating { get; }
    decimal? UserRating { get; }
    double? MalScore { get; }
    DateTime AddedAt { get; }
    DateTime UpdatedAt { get; }
    double ProgressPercent { get; }
}

public enum LibrarySort { RecentlyUpdated, Title, MyRating, MalScore, Year, RecentlyAdded, Progress }

public enum RatingFilter { Any, AtLeast9, AtLeast8, AtLeast7, AtLeast6, Unrated }

/// <summary>
/// The user's current filter selection. Within a group values are alternatives (TV or Movie).
/// Genres and themes default to "must have all", switchable per group to "any of them"
/// (<see cref="GenresMatchAny"/>, <see cref="ThemesMatchAny"/>). Groups combine with AND.
/// An empty group or <see cref="RatingFilter.Any"/>/0 year means "no restriction".
/// </summary>
public sealed class LibraryFilter
{
    public HashSet<string> Formats { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Genres { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Themes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Demographics { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ReleaseStatuses { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Sources { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> AgeRatings { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Inclusive lower bound; 0 = none.</summary>
    public int YearFrom { get; set; }

    /// <summary>Inclusive upper bound; 0 = none.</summary>
    public int YearTo { get; set; }

    public RatingFilter Rating { get; set; }

    /// <summary>False: an item needs every selected genre. True: at least one.</summary>
    public bool GenresMatchAny { get; set; }

    /// <summary>False: an item needs every selected theme. True: at least one.</summary>
    public bool ThemesMatchAny { get; set; }

    public bool HasYearRange => YearFrom > 0 || YearTo > 0;

    /// <summary>Number of active restrictions: each selected value, plus the year range and rating as one each.</summary>
    public int ActiveCount =>
        Formats.Count + Genres.Count + Themes.Count + Demographics.Count +
        ReleaseStatuses.Count + Sources.Count + AgeRatings.Count +
        (HasYearRange ? 1 : 0) + (Rating != RatingFilter.Any ? 1 : 0);

    public bool IsEmpty => ActiveCount == 0;
}

public sealed record FilterOption(string Value, int Count);

/// <summary>Values that actually occur in the library, with how many items carry each.</summary>
public sealed record LibraryFilterOptions(
    IReadOnlyList<FilterOption> Formats,
    IReadOnlyList<FilterOption> Genres,
    IReadOnlyList<FilterOption> Themes,
    IReadOnlyList<FilterOption> Demographics,
    IReadOnlyList<FilterOption> ReleaseStatuses,
    IReadOnlyList<FilterOption> Sources,
    IReadOnlyList<FilterOption> AgeRatings,
    IReadOnlyList<int> Years);

public static class LibraryFilterEngine
{
    /// <remarks>
    /// When a group is active, an item missing that data (no year, themes not filled in yet)
    /// does not match: showing it would claim a match nobody can verify.
    /// </remarks>
    public static bool Matches(ILibraryEntry e, LibraryFilter f)
    {
        if (f.Formats.Count > 0 && (e.Format is null || !f.Formats.Contains(e.Format)))
            return false;

        if (f.HasYearRange)
        {
            if (e.Year is not { } year) return false;
            if (f.YearFrom > 0 && year < f.YearFrom) return false;
            if (f.YearTo > 0 && year > f.YearTo) return false;
        }

        if (!Contains(e.Genres, f.Genres, f.GenresMatchAny)) return false;
        if (!Contains(e.Themes, f.Themes, f.ThemesMatchAny)) return false;
        if (!ContainsAny(e.Demographics, f.Demographics)) return false;

        if (f.ReleaseStatuses.Count > 0 && (e.ReleaseStatus is null || !f.ReleaseStatuses.Contains(e.ReleaseStatus)))
            return false;
        if (f.Sources.Count > 0 && (e.Source is null || !f.Sources.Contains(e.Source)))
            return false;
        if (f.AgeRatings.Count > 0 && (e.AgeRating is null || !f.AgeRatings.Contains(e.AgeRating)))
            return false;

        return f.Rating switch
        {
            RatingFilter.AtLeast9 => e.UserRating >= 9,
            RatingFilter.AtLeast8 => e.UserRating >= 8,
            RatingFilter.AtLeast7 => e.UserRating >= 7,
            RatingFilter.AtLeast6 => e.UserRating >= 6,
            RatingFilter.Unrated => e.UserRating is null,
            _ => true
        };
    }

    /// <summary>
    /// Sorts in each mode's natural direction (highest, newest, A→Z). Items lacking the sort
    /// key always go last; ties keep their incoming order (most recently updated first).
    /// </summary>
    public static IEnumerable<T> Sort<T>(IEnumerable<T> items, LibrarySort sort) where T : ILibraryEntry =>
        sort switch
        {
            LibrarySort.Title => items.OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase),
            LibrarySort.MyRating => items.OrderBy(e => e.UserRating is null).ThenByDescending(e => e.UserRating),
            LibrarySort.MalScore => items.OrderBy(e => e.MalScore is null).ThenByDescending(e => e.MalScore),
            LibrarySort.Year => items.OrderBy(e => e.Year is null).ThenByDescending(e => e.Year),
            LibrarySort.RecentlyAdded => items.OrderByDescending(e => e.AddedAt),
            LibrarySort.Progress => items.OrderByDescending(e => e.ProgressPercent),
            _ => items.OrderByDescending(e => e.UpdatedAt)
        };

    public static LibraryFilterOptions BuildOptions(IReadOnlyCollection<ILibraryEntry> items) => new(
        Count(items.Select(e => e.Format)),
        Count(items.SelectMany(e => e.Genres)),
        Count(items.SelectMany(e => e.Themes)),
        Count(items.SelectMany(e => e.Demographics)),
        Count(items.Select(e => e.ReleaseStatus)),
        Count(items.Select(e => e.Source)),
        Count(items.Select(e => e.AgeRating)),
        items.Where(e => e.Year is > 0).Select(e => e.Year!.Value).Distinct().OrderByDescending(y => y).ToList());

    /// <summary>Most common first, then alphabetical; blanks ignored; case-insensitive.</summary>
    private static List<FilterOption> Count(IEnumerable<string?> values) =>
        values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new FilterOption(g.Key, g.Count()))
            .OrderByDescending(o => o.Count)
            .ThenBy(o => o.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool Contains(IReadOnlyList<string> have, HashSet<string> wanted, bool matchAny) =>
        matchAny ? ContainsAny(have, wanted) : ContainsAll(have, wanted);

    private static bool ContainsAll(IReadOnlyList<string> have, HashSet<string> wanted) =>
        wanted.Count == 0 || wanted.All(w => have.Contains(w, StringComparer.OrdinalIgnoreCase));

    private static bool ContainsAny(IReadOnlyList<string> have, HashSet<string> wanted) =>
        wanted.Count == 0 || have.Any(wanted.Contains);
}

/// <summary>MyAnimeList age ratings are long ("PG-13 - Teens 13 or older"); chips use the code.</summary>
public static class AgeRatingCodes
{
    public static string? Shorten(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();

        // "R - 17+ (violence & profanity)": the code alone ("R") would read like "R+".
        if (trimmed.StartsWith("R - 17+", StringComparison.OrdinalIgnoreCase))
            return "R-17+";

        var dash = trimmed.IndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? trimmed[..dash].Trim() : trimmed;
    }
}
