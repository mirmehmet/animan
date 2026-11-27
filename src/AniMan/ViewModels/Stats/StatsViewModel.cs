using System.Globalization;
using AniMan.Core.Common;
using AniMan.Core.Domain;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Core.Statistics;
using AniMan.Localization;
using AniMan.Navigation;
using AniMan.ViewModels.Library;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniMan.ViewModels.Stats;

/// <summary>
/// The Stats dashboard for one media type at a time. Loads the library through
/// <see cref="ITrackingService"/>, hands it to the pure <see cref="LibraryStatsCalculator"/>
/// and turns the result into render-ready rows (pixel heights, weights, tooltip text).
/// Clicking a status, genre or year asks the page to open the library with that filter.
/// </summary>
public partial class StatsViewModel : ObservableObject
{
    /// <summary>Plot height of the column charts, in pixels.</summary>
    public const double ScoreChartHeight = 120;
    public const double YearChartHeight = 96;

    private static readonly double[] HeatOpacity = [0, 0.3, 0.5, 0.75, 1.0];

    private readonly ITrackingService _tracking;
    private readonly ILibraryVersion _libraryVersion;
    private readonly Dictionary<MediaType, LoadedLibrary> _cache = [];

    /// <summary>Library version the cache was filled at; -1 = never.</summary>
    private long _cacheVersion = -1;

    /// <summary>Localized load failure per media type; a failed load is not cached, so the next visit retries.</summary>
    private readonly Dictionary<MediaType, string> _loadErrors = [];
    private bool _suppressPeriod;

    /// <summary>What the page currently shows: built from this library version, for this type, on this day.</summary>
    private (long Version, MediaType Type, DateOnly Day)? _shown;

    private sealed record LoadedLibrary(
        List<StatsEntry> Entries, Dictionary<int, (string Name, string? Color)> Statuses);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnime), nameof(IsManga))]
    private MediaType _mediaType = MediaType.Anime;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string? _loadErrorMessage;

    [ObservableProperty] private IReadOnlyList<StatTileViewModel> _tiles = [];
    [ObservableProperty] private IReadOnlyList<StatusSegmentViewModel> _statusSegments = [];
    [ObservableProperty] private IReadOnlyList<ColumnViewModel> _scoreColumns = [];
    [ObservableProperty] private bool _hasScores;
    [ObservableProperty] private IReadOnlyList<RankedBarViewModel> _topGenres = [];
    [ObservableProperty] private IReadOnlyList<TasteRowViewModel> _tasteRows = [];
    [ObservableProperty] private string _tasteAxisMin = string.Empty;
    [ObservableProperty] private IReadOnlyList<ColumnViewModel> _yearColumns = [];

    [ObservableProperty] private IReadOnlyList<PeriodOption> _periodOptions = [];
    [ObservableProperty] private PeriodOption? _selectedPeriod;
    [ObservableProperty] private IReadOnlyList<HeatWeekViewModel> _heatWeeks = [];
    [ObservableProperty] private IReadOnlyList<PeriodFigureViewModel> _periodFigures = [];

    public bool IsAnime => MediaType == MediaType.Anime;
    public bool IsManga => MediaType == MediaType.Manga;

    public bool HasTaste => TasteRows.Count > 0;
    public bool HasGenres => TopGenres.Count > 0;
    public bool HasYears => YearColumns.Count > 0;

    /// <summary>Raised when a chart element was clicked; the page navigates to the library.</summary>
    public event EventHandler<LibraryFilterRequest>? OpenLibraryRequested;

    public StatsViewModel(ITrackingService tracking, ILibraryVersion libraryVersion)
    {
        _tracking = tracking;
        _libraryVersion = libraryVersion;
    }

    partial void OnTasteRowsChanged(IReadOnlyList<TasteRowViewModel> value) => OnPropertyChanged(nameof(HasTaste));
    partial void OnTopGenresChanged(IReadOnlyList<RankedBarViewModel> value) => OnPropertyChanged(nameof(HasGenres));
    partial void OnYearColumnsChanged(IReadOnlyList<ColumnViewModel> value) => OnPropertyChanged(nameof(HasYears));

    partial void OnSelectedPeriodChanged(PeriodOption? value)
    {
        if (!_suppressPeriod)
            RebuildPeriod();
    }

    /// <summary>
    /// Shows the statistics, reading the library again only if it changed since the last
    /// visit. A visit with nothing changed (same library, type and day) keeps the view as it
    /// is: rebuilding it recreated every chart and the ~370 calendar cells each time. A new
    /// day still rebuilds, so date-based parts (today, the last 12 months) stay current.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        // Read before loading: a write that lands during the load bumps the version
        // again and is picked up on the next visit.
        var version = _libraryVersion.Current;
        if (_shown == (version, MediaType, Today) && !_loadErrors.ContainsKey(MediaType))
            return;

        IsLoading = true;
        try
        {
            if (version != _cacheVersion)
            {
                _cache.Clear();
                _cacheVersion = version;
            }
            await EnsureLoadedAsync(MediaType);
            Rebuild();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SwitchMediaAsync(MediaType mediaType)
    {
        if (mediaType == MediaType) return;
        MediaType = mediaType;
        IsLoading = true;
        try
        {
            await EnsureLoadedAsync(mediaType);
            Rebuild();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenStatus(StatusSegmentViewModel segment) =>
        OpenLibraryRequested?.Invoke(this, new LibraryFilterRequest(MediaType, StatusId: segment.StatusId));

    [RelayCommand]
    private void OpenGenre(string genre) =>
        OpenLibraryRequested?.Invoke(this, new LibraryFilterRequest(MediaType, Genre: genre));

    [RelayCommand]
    private void OpenYears(ColumnViewModel column)
    {
        if (column.IsClickable)
            OpenLibraryRequested?.Invoke(this,
                new LibraryFilterRequest(MediaType, YearFrom: column.YearFrom, YearTo: column.YearTo));
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    private async Task EnsureLoadedAsync(MediaType mediaType)
    {
        if (_cache.ContainsKey(mediaType)) return;

        var result = await _tracking.GetLibraryAsync(mediaType);
        if (!result.IsSuccess)
        {
            // Not "no statistics yet": the library exists, it just could not be read.
            _loadErrors[mediaType] = ErrorText.For(result.Kind);
            return;
        }

        _loadErrors.Remove(mediaType);
        var items = result.Value!;

        var statuses = new Dictionary<int, (string Name, string? Color)>();
        foreach (var item in items)
        {
            statuses.TryAdd(item.StatusId,
                (TrackingStatusDisplay.GetDisplayName(item.StatusId, item.Status?.Name), item.Status?.Color));
        }

        _cache[mediaType] = new LoadedLibrary(items.Select(i => ToEntry(i, mediaType)).ToList(), statuses);
    }

    private static StatsEntry ToEntry(LibraryItem item, MediaType mediaType)
    {
        var snap = item.Snapshot;
        var anime = mediaType == MediaType.Anime;

        // Timestamps are stored in UTC; the calendar groups by the user's local day.
        var dates = anime
            ? item.EpisodeProgress.Where(e => e.IsWatched && e.WatchedAt.HasValue)
                .Select(e => DateOnly.FromDateTime(e.WatchedAt!.Value.ToLocalTime()))
            : item.ChapterProgress.Where(c => c.IsRead && c.ReadAt.HasValue)
                .Select(c => DateOnly.FromDateTime(c.ReadAt!.Value.ToLocalTime()));

        return new StatsEntry
        {
            StatusId = item.StatusId,
            Rating = item.UserRating ?? item.Score,
            MalScore = snap?.MalScore,
            MinutesPerUnit = anime ? snap?.DurationMinutes : null,
            TotalUnits = anime ? snap?.TotalEpisodes : snap?.TotalChapters,
            RewatchCount = anime ? item.RewatchCount : 0,
            UnitsDone = anime
                ? item.EpisodeProgress.Count(e => e.IsWatched)
                : item.ChapterProgress.Count(c => c.IsRead),
            UnitDates = dates.ToList(),
            CompletedAt = item.CompletedAt,
            Year = LibraryViewModelBase.ResolveYear(snap),
            Genres = NameList.Parse(snap?.Genres)
        };
    }

    // ── Overview ──────────────────────────────────────────────────────────────

    private void Rebuild()
    {
        var library = _cache.GetValueOrDefault(MediaType) ?? new LoadedLibrary([], []);
        var entries = library.Entries;

        LoadErrorMessage = _loadErrors.GetValueOrDefault(MediaType);
        HasData = entries.Count > 0;
        IsEmpty = !HasData && LoadErrorMessage is null;

        var inProgressId = IsAnime ? TrackingStatusIds.Watching : TrackingStatusIds.Reading;
        var o = LibraryStatsCalculator.ComputeOverview(entries, TrackingStatusIds.Completed, inProgressId);

        Tiles = BuildTiles(o);
        StatusSegments = BuildStatusSegments(o, library.Statuses);
        ScoreColumns = BuildScoreColumns(o);
        HasScores = o.Scores.Any(s => s.Count > 0);
        TopGenres = BuildTopGenres(o);
        TasteRows = BuildTaste(o, out var axisMin);
        TasteAxisMin = axisMin.ToString("0", UiCulture);
        YearColumns = BuildYearColumns(o, entries);

        var years = LibraryStatsCalculator.ActiveYears(entries);
        var options = new List<PeriodOption> { new(0, Text("Stats_PeriodLast12")) };
        options.AddRange(years.Select(y => new PeriodOption(y, y.ToString(CultureInfo.InvariantCulture))));

        _suppressPeriod = true;
        PeriodOptions = options;
        SelectedPeriod = options.FirstOrDefault(p => p.Year == SelectedPeriod?.Year) ?? options[0];
        _suppressPeriod = false;
        RebuildPeriod();

        _shown = (_cacheVersion, MediaType, Today);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private List<StatTileViewModel> BuildTiles(LibraryOverview o)
    {
        var meanTile = new StatTileViewModel(
            Text("Stats_MeanScore"),
            o.MeanScore is { } mean ? mean.ToString("0.0", UiCulture) : "—",
            MalComparison(o));
        var completedTile = new StatTileViewModel(
            Text("Stats_Completed"),
            o.Completed.ToString("N0", UiCulture),
            string.Format(Text("Stats_OfTitlesFormat"), o.TotalTitles.ToString("N0", UiCulture)));

        if (IsAnime)
        {
            return
            [
                new(Text("Stats_WatchTime"), FormatHours(o.WatchMinutes),
                    string.Format(Text("Stats_DaysFormat"), (o.WatchMinutes / 1440).ToString("N1", UiCulture))),
                new(Text("Stats_EpisodesWatched"), o.UnitsDone.ToString("N0", UiCulture), null),
                completedTile,
                meanTile
            ];
        }

        return
        [
            new(Text("Stats_ChaptersRead"), o.UnitsDone.ToString("N0", UiCulture), null),
            completedTile,
            new(Text("Stats_ReadingNow"), o.InProgress.ToString("N0", UiCulture), null),
            meanTile
        ];
    }

    private static string? MalComparison(LibraryOverview o)
    {
        if (o.MeanScore is null) return Text("Stats_NoRatings");
        if (o.MalDelta is not { } delta) return null;
        if (Math.Abs(delta) < 0.05) return Text("Stats_MalSame");

        var amount = Math.Abs(delta).ToString("0.0", UiCulture);
        return string.Format(Text(delta > 0 ? "Stats_MalMoreGenerous" : "Stats_MalHarsher"), amount);
    }

    private static List<StatusSegmentViewModel> BuildStatusSegments(
        LibraryOverview o, Dictionary<int, (string Name, string? Color)> statuses)
    {
        var total = o.Statuses.Sum(s => s.Count);
        if (total == 0) return [];

        return o.Statuses
            .OrderBy(s => StatusColors.DisplayRank(s.StatusId))
            .Select(s =>
            {
                var (name, color) = statuses.GetValueOrDefault(s.StatusId, (s.StatusId.ToString(CultureInfo.InvariantCulture), null));
                var share = ((double)s.Count / total).ToString("P0", UiCulture);
                return new StatusSegmentViewModel(
                    s.StatusId, name, s.Count, StatusColors.For(s.StatusId, color),
                    $"{name}: {s.Count} ({share})\n{Text("Stats_ClickToOpen")}");
            })
            .ToList();
    }

    private static List<ColumnViewModel> BuildScoreColumns(LibraryOverview o)
    {
        var max = o.Scores.Max(s => s.Count);
        // Emphasis form: the column the mean falls in is full accent, the rest quieter.
        var meanBucket = o.MeanScore is { } m ? (int)Math.Clamp(Math.Round(m, MidpointRounding.AwayFromZero), 1, 10) : 0;

        return o.Scores.Select(s => new ColumnViewModel(
            s.Score.ToString(CultureInfo.InvariantCulture),
            s.Count,
            max > 0 ? ScoreChartHeight * s.Count / max : 0,
            s.Score == meanBucket,
            string.Format(Text("Stats_TipScore"), s.Score, s.Count))).ToList();
    }

    private static List<RankedBarViewModel> BuildTopGenres(LibraryOverview o)
    {
        var max = o.TopGenres.Count > 0 ? o.TopGenres[0].Count : 0;
        return o.TopGenres.Select(g => new RankedBarViewModel(
            g.Name, g.Count, max > 0 ? (double)g.Count / max : 0,
            $"{string.Format(Text("Stats_TipTitles"), g.Name, g.Count)}\n{Text("Stats_ClickToOpen")}")).ToList();
    }

    private static List<TasteRowViewModel> BuildTaste(LibraryOverview o, out double axisMin)
    {
        const double axisMax = 10;
        axisMin = 5;
        if (o.Taste.Count == 0) return [];

        // Start the axis at the lowest whole score shown so differences are visible.
        var lowest = o.Taste.Min(t => Math.Min(t.YourMean, t.MalMean));
        axisMin = Math.Clamp(Math.Floor(lowest), 1, axisMax - 2);
        var min = axisMin;

        return o.Taste.Select(t => new TasteRowViewModel(
            t.Genre, t.YourMean, t.MalMean, min, axisMax,
            t.YourMean.ToString("0.0", UiCulture),
            string.Format(Text("Stats_TipTaste"), t.Genre,
                t.YourMean.ToString("0.0", UiCulture),
                t.MalMean.ToString("0.00", UiCulture),
                t.Count) + "\n" + Text("Stats_ClickToOpen"))).ToList();
    }

    private static List<ColumnViewModel> BuildYearColumns(LibraryOverview o, List<StatsEntry> entries)
    {
        if (o.ReleaseYears.Count == 0) return [];

        var max = o.ReleaseYears.Max(b => b.Count);
        // Label sparsely so long axes don't collide: every year, every 2nd, or every 5th.
        var step = o.ReleaseYears.Count > 15 ? 5 : o.ReleaseYears.Count > 8 ? 2 : 1;

        return o.ReleaseYears.Select((b, index) =>
        {
            var name = b.From == b.To ? b.From.ToString(CultureInfo.InvariantCulture) : $"{b.From}–{b.To}";
            var label = index % step == 0 || index == o.ReleaseYears.Count - 1
                ? b.From.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            // Open the library on the years actually present, so its year menus can show them.
            var present = entries.Where(e => e.Year >= b.From && e.Year <= b.To).Select(e => e.Year!.Value).ToList();
            var tooltip = string.Format(Text("Stats_TipTitles"), name, b.Count);
            if (b.Count > 0) tooltip += "\n" + Text("Stats_ClickToOpen");

            return new ColumnViewModel(
                label, b.Count, max > 0 ? YearChartHeight * b.Count / max : 0, false, tooltip,
                present.Count > 0 ? present.Min() : 0,
                present.Count > 0 ? present.Max() : 0);
        }).ToList();
    }

    // ── Period: activity calendar + summary ──────────────────────────────────

    private void RebuildPeriod()
    {
        var entries = _cache.GetValueOrDefault(MediaType)?.Entries ?? [];
        var today = Today;
        var year = SelectedPeriod?.Year ?? 0;

        var (start, end) = year == 0
            ? (today.AddDays(-364), today)
            : (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31) > today ? today : new DateOnly(year, 12, 31));

        var summary = LibraryStatsCalculator.ComputePeriod(entries, start, end);
        HeatWeeks = BuildHeatmap(summary, start, end);
        PeriodFigures = BuildPeriodFigures(summary);
    }

    private List<HeatWeekViewModel> BuildHeatmap(PeriodSummary summary, DateOnly start, DateOnly end)
    {
        var culture = UiCulture;
        var maxDaily = summary.DailyUnits.Count > 0 ? summary.DailyUnits.Values.Max() : 0;
        var tipKey = IsAnime ? "Stats_TipEpisodesOnDay" : "Stats_TipChaptersOnDay";

        // Weeks run Monday → Sunday; pad the first and last week outside the period.
        var gridStart = start.AddDays(-(((int)start.DayOfWeek + 6) % 7));
        var weeks = new List<HeatWeekViewModel>();
        for (var weekStart = gridStart; weekStart <= end; weekStart = weekStart.AddDays(7))
        {
            var days = new List<HeatDayViewModel>(7);

            // Label the column whose Monday is the month's first Monday, so the name sits over
            // that month's days (labelling the week holding the 1st put it over the previous month).
            var monthLabel = weekStart >= start && weekStart.Day <= 7
                ? culture.DateTimeFormat.GetAbbreviatedMonthName(weekStart.Month)
                : string.Empty;
            for (var i = 0; i < 7; i++)
            {
                var d = weekStart.AddDays(i);
                var inRange = d >= start && d <= end;
                if (!inRange)
                {
                    days.Add(new HeatDayViewModel(false, 0, null));
                    continue;
                }


                var count = summary.DailyUnits.GetValueOrDefault(d);
                var level = count == 0 || maxDaily == 0 ? 0 : (int)Math.Clamp(Math.Ceiling(4.0 * count / maxDaily), 1, 4);
                days.Add(new HeatDayViewModel(true, HeatOpacity[level],
                    string.Format(Text(tipKey), d.ToString("d MMM yyyy", culture), count)));
            }
            weeks.Add(new HeatWeekViewModel(monthLabel, days));
        }
        return weeks;
    }

    private List<PeriodFigureViewModel> BuildPeriodFigures(PeriodSummary s)
    {
        var culture = UiCulture;
        var figures = new List<PeriodFigureViewModel>
        {
            new(Text("Stats_Completed"), s.CompletedInPeriod.ToString("N0", culture)),
            new(Text(IsAnime ? "Stats_EpisodesWatched" : "Stats_ChaptersRead"), s.UnitsInPeriod.ToString("N0", culture))
        };
        if (IsAnime)
            figures.Add(new(Text("Stats_WatchTime"), FormatHours(s.MinutesInPeriod)));

        figures.Add(new(Text("Stats_PeriodTopGenre"), s.TopGenre ?? "—"));
        figures.Add(new(Text("Stats_PeriodBestMonth"), s.MostActiveMonth is { } m
            ? $"{culture.DateTimeFormat.GetMonthName(m.Month)} {m.Year}"
            : "—"));
        return figures;
    }

    private static string FormatHours(double minutes) =>
        string.Format(Text("Stats_HoursFormat"), Math.Round(minutes / 60).ToString("N0", UiCulture));

    private static string Text(string key) => LocalizationManager.Get(key);

    /// <summary>
    /// The app's UI language. Month names, dates and decimal marks follow it, as the rest of
    /// the app does (see MediaMetadataDisplay). CultureInfo.CurrentCulture can still be the
    /// Windows region here, which gave Turkish months and "8,4" under an English UI.
    /// </summary>
    private static CultureInfo UiCulture => LocalizationManager.Culture;
}
