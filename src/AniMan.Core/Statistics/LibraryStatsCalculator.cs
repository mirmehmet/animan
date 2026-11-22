namespace AniMan.Core.Statistics;

/// <summary>
/// One library item reduced to what the statistics page needs. "Units" are episodes for
/// anime and chapters for manga.
/// </summary>
public sealed record StatsEntry
{
    public int StatusId { get; init; }

    /// <summary>The user's score (0–10); null when unrated.</summary>
    public decimal? Rating { get; init; }

    public double? MalScore { get; init; }

    /// <summary>Minutes per episode; null when unknown (or for manga).</summary>
    public int? MinutesPerUnit { get; init; }

    public int? TotalUnits { get; init; }
    public int RewatchCount { get; init; }

    /// <summary>Units marked watched/read, with or without a timestamp.</summary>
    public int UnitsDone { get; init; }

    /// <summary>Local calendar dates of units that carry a watched/read timestamp.</summary>
    public IReadOnlyList<DateOnly> UnitDates { get; init; } = [];

    public DateOnly? CompletedAt { get; init; }

    /// <summary>Release (season or start) year.</summary>
    public int? Year { get; init; }

    public IReadOnlyList<string> Genres { get; init; } = [];
}

public sealed record StatusSlice(int StatusId, int Count);
public sealed record ScoreBucket(int Score, int Count);
public sealed record RankedValue(string Name, int Count);

/// <param name="YourMean">The user's mean score for the genre.</param>
/// <param name="MalMean">MyAnimeList's mean over the same titles.</param>
/// <param name="Count">Titles that have both scores.</param>
public sealed record TasteRow(string Genre, double YourMean, double MalMean, int Count);

/// <summary>A release-year column; <see cref="From"/> == <see cref="To"/> unless years are grouped.</summary>
public sealed record YearBucket(int From, int To, int Count);

public sealed record LibraryOverview(
    int TotalTitles,
    int Completed,
    int InProgress,
    int UnitsDone,
    double WatchMinutes,
    double? MeanScore,
    double? MalDelta,
    IReadOnlyList<StatusSlice> Statuses,
    IReadOnlyList<ScoreBucket> Scores,
    IReadOnlyList<RankedValue> TopGenres,
    IReadOnlyList<TasteRow> Taste,
    IReadOnlyList<YearBucket> ReleaseYears);

/// <param name="DailyUnits">Units per local date, only dates with activity.</param>
public sealed record PeriodSummary(
    DateOnly Start,
    DateOnly End,
    IReadOnlyDictionary<DateOnly, int> DailyUnits,
    int UnitsInPeriod,
    double MinutesInPeriod,
    int CompletedInPeriod,
    string? TopGenre,
    (int Year, int Month)? MostActiveMonth);

/// <summary>
/// Pure statistics over a loaded library (one media type at a time). Everything is computed
/// in memory from the items the library page already loads — no extra queries.
/// </summary>
public static class LibraryStatsCalculator
{
    /// <summary>Used for watch time when a title's episode length is unknown.</summary>
    public const int DefaultMinutesPerUnit = 24;

    /// <summary>A genre needs this many titles scored by both the user and MAL to appear in the taste rows.</summary>
    public const int MinTasteSamples = 3;

    public const int MaxRankedRows = 8;

    /// <summary>Release years spanning more than this are grouped into 5-year columns.</summary>
    public const int MaxYearColumns = 30;

    public static LibraryOverview ComputeOverview(
        IReadOnlyCollection<StatsEntry> entries, int completedStatusId, int inProgressStatusId)
    {
        var rated = entries.Where(e => e.Rating is > 0).ToList();
        var ratedWithMal = rated.Where(e => e.MalScore is > 0).ToList();

        double? meanScore = rated.Count > 0 ? rated.Average(e => (double)e.Rating!.Value) : null;
        double? malDelta = ratedWithMal.Count > 0
            ? ratedWithMal.Average(e => (double)e.Rating!.Value - e.MalScore!.Value)
            : null;

        return new LibraryOverview(
            TotalTitles: entries.Count,
            Completed: entries.Count(e => e.StatusId == completedStatusId),
            InProgress: entries.Count(e => e.StatusId == inProgressStatusId),
            UnitsDone: entries.Sum(e => e.UnitsDone),
            WatchMinutes: entries.Sum(TotalMinutes),
            MeanScore: meanScore,
            MalDelta: malDelta,
            Statuses: entries
                .GroupBy(e => e.StatusId)
                .Select(g => new StatusSlice(g.Key, g.Count()))
                .OrderByDescending(s => s.Count)
                .ToList(),
            Scores: Enumerable.Range(1, 10)
                .Select(s => new ScoreBucket(s, rated.Count(e => ScoreBucketOf(e.Rating!.Value) == s)))
                .ToList(),
            TopGenres: entries
                .SelectMany(e => e.Genres.Distinct(StringComparer.OrdinalIgnoreCase))
                .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
                .Select(g => new RankedValue(g.Key, g.Count()))
                .OrderByDescending(r => r.Count)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxRankedRows)
                .ToList(),
            Taste: ComputeTaste(ratedWithMal),
            ReleaseYears: ComputeReleaseYears(entries));
    }

    public static PeriodSummary ComputePeriod(IReadOnlyCollection<StatsEntry> entries, DateOnly start, DateOnly end)
    {
        var daily = new Dictionary<DateOnly, int>();
        var genreUnits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var monthUnits = new Dictionary<(int Year, int Month), int>();
        var units = 0;
        var minutes = 0.0;

        foreach (var e in entries)
        {
            var inPeriod = e.UnitDates.Count(d => d >= start && d <= end);
            if (inPeriod == 0) continue;

            units += inPeriod;
            minutes += inPeriod * (e.MinutesPerUnit ?? DefaultMinutesPerUnit);

            foreach (var d in e.UnitDates.Where(d => d >= start && d <= end))
            {
                daily[d] = daily.GetValueOrDefault(d) + 1;
                monthUnits[(d.Year, d.Month)] = monthUnits.GetValueOrDefault((d.Year, d.Month)) + 1;
            }

            foreach (var g in e.Genres.Distinct(StringComparer.OrdinalIgnoreCase))
                genreUnits[g] = genreUnits.GetValueOrDefault(g) + inPeriod;
        }

        string? topGenre = genreUnits.Count == 0
            ? null
            : genreUnits.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).First().Key;

        (int, int)? mostActive = monthUnits.Count == 0
            ? null
            : monthUnits.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;

        return new PeriodSummary(
            start, end, daily, units, minutes,
            CompletedInPeriod: entries.Count(e => e.CompletedAt is { } c && c >= start && c <= end),
            topGenre, mostActive);
    }

    /// <summary>Calendar years that have any dated activity or completion, newest first.</summary>
    public static IReadOnlyList<int> ActiveYears(IReadOnlyCollection<StatsEntry> entries) =>
        entries
            .SelectMany(e => e.UnitDates.Select(d => d.Year)
                .Concat(e.CompletedAt is { } c ? new[] { c.Year } : Array.Empty<int>()))
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

    /// <summary>
    /// Episodes watched plus full rewatches, each at the title's own episode length.
    /// A rewatch counts the whole series when its length is known, otherwise what was watched.
    /// </summary>
    private static double TotalMinutes(StatsEntry e)
    {
        var perRewatch = e.TotalUnits is > 0 ? e.TotalUnits.Value : e.UnitsDone;
        var episodes = e.UnitsDone + (long)Math.Max(0, e.RewatchCount) * perRewatch;
        return episodes * (double)(e.MinutesPerUnit ?? DefaultMinutesPerUnit);
    }

    private static int ScoreBucketOf(decimal rating) =>
        (int)Math.Clamp(Math.Round(rating, MidpointRounding.AwayFromZero), 1, 10);

    private static List<TasteRow> ComputeTaste(List<StatsEntry> ratedWithMal) =>
        ratedWithMal
            .SelectMany(e => e.Genres.Distinct(StringComparer.OrdinalIgnoreCase).Select(g => (Genre: g, Entry: e)))
            .GroupBy(x => x.Genre, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= MinTasteSamples)
            .Select(g => new TasteRow(
                g.Key,
                g.Average(x => (double)x.Entry.Rating!.Value),
                g.Average(x => x.Entry.MalScore!.Value),
                g.Count()))
            .OrderByDescending(t => t.YourMean)
            .ThenByDescending(t => t.Count)
            .Take(MaxRankedRows)
            .ToList();

    /// <summary>
    /// One column per year from the oldest to the newest title, gaps included so the axis is
    /// honest. Spans wider than <see cref="MaxYearColumns"/> years fold into 5-year columns.
    /// </summary>
    private static List<YearBucket> ComputeReleaseYears(IReadOnlyCollection<StatsEntry> entries)
    {
        var years = entries.Where(e => e.Year is > 0).Select(e => e.Year!.Value).ToList();
        if (years.Count == 0) return [];

        var min = years.Min();
        var max = years.Max();
        var width = max - min + 1 > MaxYearColumns ? 5 : 1;
        var first = width == 1 ? min : min - (min % width);

        var buckets = new List<YearBucket>();
        for (var from = first; from <= max; from += width)
        {
            var to = from + width - 1;
            buckets.Add(new YearBucket(from, to, years.Count(y => y >= from && y <= to)));
        }
        return buckets;
    }
}
