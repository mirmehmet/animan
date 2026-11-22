using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Core.Library;
using AniMan.Localization;
using AniMan.ViewModels.Detail;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniMan.ViewModels.Library;

/// <summary>
/// Shared implementation of the anime/manga library pages: loading, status/favorite tabs,
/// title search, the filter panel and sorting. Subclasses supply only the media type and the
/// progress projection.
/// <para>
/// Filtering and sorting are pure in-memory work over the loaded list (see
/// <see cref="LibraryFilterEngine"/>). Filters live for the session — the VM is a singleton —
/// while the sort choice is persisted per media type.
/// </para>
/// </summary>
public abstract partial class LibraryViewModelBase : ObservableObject
{
    public const int FavoritesTabId = 99;
    public const int RatingsTabId = 98;

    private readonly ITrackingService _tracking;
    private readonly ISettingsService _settings;
    private readonly MediaType _mediaType;
    private List<LibraryCardViewModel> _allItems = [];
    private bool _sortLoaded;

    // Set while chips/years are rebuilt or cleared in bulk, so that one change does not
    // re-run the filter dozens of times.
    private bool _suppressFilter;

    [ObservableProperty]
    private ObservableCollection<LibraryCardViewModel> _items = [];

    [ObservableProperty]
    private int _activeStatusId; // 0=All, 98=Ratings, 99=Favorites

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isFilterPanelOpen;

    [ObservableProperty]
    private LibrarySort _sortMode = LibrarySort.RecentlyUpdated;

    [ObservableProperty]
    private int _yearFrom;

    [ObservableProperty]
    private int _yearTo;

    [ObservableProperty]
    private RatingFilter _ratingFilter;

    [ObservableProperty]
    private IReadOnlyList<YearOption> _yearOptions = [];

    public IReadOnlyList<SortOption> SortOptions { get; } =
    [
        new(LibrarySort.RecentlyUpdated, LocalizationManager.Get("Library_SortRecentlyUpdated")),
        new(LibrarySort.Title, LocalizationManager.Get("Library_SortTitle")),
        new(LibrarySort.MyRating, LocalizationManager.Get("Library_SortMyRating")),
        new(LibrarySort.MalScore, LocalizationManager.Get("Library_SortMalScore")),
        new(LibrarySort.Year, LocalizationManager.Get("Library_SortYear")),
        new(LibrarySort.RecentlyAdded, LocalizationManager.Get("Library_SortRecentlyAdded")),
        new(LibrarySort.Progress, LocalizationManager.Get("Library_SortProgress")),
    ];

    public IReadOnlyList<RatingOption> RatingOptions { get; } =
    [
        new(RatingFilter.Any, LocalizationManager.Get("Library_RatingAny")),
        new(RatingFilter.AtLeast9, "9+"),
        new(RatingFilter.AtLeast8, "8+"),
        new(RatingFilter.AtLeast7, "7+"),
        new(RatingFilter.AtLeast6, "6+"),
        new(RatingFilter.Unrated, LocalizationManager.Get("Library_RatingUnrated")),
    ];

    private readonly FilterGroupViewModel _formatGroup = new(LocalizationManager.Get("Library_FilterFormat"));
    private readonly FilterGroupViewModel _genreGroup = new(LocalizationManager.Get("Library_FilterGenre"));
    private readonly FilterGroupViewModel _themeGroup = new(LocalizationManager.Get("Library_FilterTheme"));
    private readonly FilterGroupViewModel _demographicGroup = new(LocalizationManager.Get("Library_FilterDemographic"));
    private readonly FilterGroupViewModel _statusGroup = new(LocalizationManager.Get("Library_FilterStatus"));
    private readonly FilterGroupViewModel _sourceGroup = new(LocalizationManager.Get("Library_FilterSource"));
    private readonly FilterGroupViewModel _ageRatingGroup = new(LocalizationManager.Get("Library_FilterAgeRating"));

    /// <summary>Chip groups in panel order.</summary>
    public IReadOnlyList<FilterGroupViewModel> FilterGroups { get; }

    public ObservableCollection<ActiveFilterViewModel> ActiveFilters { get; } = [];

    public bool IsRatingsTab => ActiveStatusId == RatingsTabId;

    public bool HasTotalItems => _allItems.Count > 0;
    public bool IsEmptyLibrary => !IsLoading && _allItems.Count == 0;
    public bool IsNoResults => !IsLoading && _allItems.Count > 0 && Items.Count == 0;
    public bool HasContent => !IsLoading && Items.Count > 0;

    public bool HasActiveFilters => ActiveFilters.Count > 0;
    public bool ShowActiveFilterBar => HasActiveFilters && !IsFilterPanelOpen;

    public string FilterButtonText => ActiveFilters.Count > 0
        ? string.Format(LocalizationManager.Get("Library_FiltersWithCount"), ActiveFilters.Count)
        : LocalizationManager.Get("Library_Filters");

    /// <summary>"42 / 180" while something narrows the list, otherwise just the total.</summary>
    public string ResultCountText => Items.Count == _allItems.Count
        ? _allItems.Count.ToString(CultureInfo.CurrentCulture)
        : string.Format(LocalizationManager.Get("Library_ResultCount"), Items.Count, _allItems.Count);

    private string SortSettingKey => $"LibrarySort.{_mediaType}";

    protected LibraryViewModelBase(ITrackingService tracking, ISettingsService settings, MediaType mediaType)
    {
        _tracking = tracking;
        _settings = settings;
        _mediaType = mediaType;
        FilterGroups =
        [
            _formatGroup, _genreGroup, _themeGroup, _demographicGroup,
            _statusGroup, _sourceGroup, _ageRatingGroup
        ];
    }

    /// <summary>Watched/read count and total for the given item, per media type.</summary>
    protected abstract (int Watched, int? Total) GetProgress(LibraryItem item);

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        OnStateChanged();
        try
        {
            await LoadSortPreferenceOnceAsync();

            var result = await _tracking.GetLibraryAsync(_mediaType.ToString().ToLowerInvariant());
            if (!result.IsSuccess) return;

            _allItems = result.Value!.Select(ToCard).ToList();

            foreach (var card in _allItems)
                card.LoadCoverImage();

            RebuildFilterOptions();
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
            OnStateChanged();
        }
    }

    [RelayCommand]
    private void ToggleFilterPanel() => IsFilterPanelOpen = !IsFilterPanelOpen;

    [RelayCommand]
    private void ClearFilters()
    {
        _suppressFilter = true;
        try
        {
            foreach (var chip in FilterGroups.SelectMany(g => g.Chips))
                chip.IsSelected = false;
            YearFrom = 0;
            YearTo = 0;
            RatingFilter = RatingFilter.Any;
        }
        finally
        {
            _suppressFilter = false;
        }
        ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    partial void OnActiveStatusIdChanged(int value)
    {
        OnPropertyChanged(nameof(IsRatingsTab));
        ApplyFilter();
    }

    partial void OnSortModeChanged(LibrarySort value)
    {
        ApplyFilter();
        if (_sortLoaded)
            _ = _settings.SetAsync(SortSettingKey, value.ToString());
    }

    partial void OnYearFromChanged(int value) => OnFilterChanged();
    partial void OnYearToChanged(int value) => OnFilterChanged();
    partial void OnRatingFilterChanged(RatingFilter value) => OnFilterChanged();
    partial void OnIsFilterPanelOpenChanged(bool value) => OnPropertyChanged(nameof(ShowActiveFilterBar));

    private void OnFilterChanged()
    {
        if (!_suppressFilter)
            ApplyFilter();
    }

    private async Task LoadSortPreferenceOnceAsync()
    {
        if (_sortLoaded) return;
        try
        {
            var saved = await _settings.GetAsync(SortSettingKey);
            if (Enum.TryParse<LibrarySort>(saved, out var sort) && Enum.IsDefined(sort))
                SortMode = sort;
        }
        finally
        {
            _sortLoaded = true; // later changes are the user's and get saved
        }
    }

    private LibraryCardViewModel ToCard(LibraryItem item)
    {
        var (watched, total) = GetProgress(item);
        var snap = item.Snapshot;
        return new LibraryCardViewModel
        {
            LibraryItemId = item.Id,
            MalId = item.MalId,
            MediaType = _mediaType,
            Title = snap?.Title ?? LocalizationManager.Get("Library_NoTitle"),
            CoverLocalPath = snap?.CoverLocalPath,
            StatusId = item.StatusId,
            StatusName = TrackingStatusDisplay.GetDisplayName(item.StatusId, item.Status?.Name),
            StatusColor = item.Status?.Color,
            Score = item.Score,
            UserRating = item.UserRating,
            MalScore = snap?.MalScore,
            IsFavorite = item.IsFavorite,
            ProgressWatched = watched,
            ProgressTotal = total,
            Format = NullIfBlank(snap?.Type),
            Year = ResolveYear(snap),
            Genres = ParseNames(snap?.Genres),
            Themes = ParseNames(snap?.Themes),
            Demographics = ParseNames(snap?.Demographics),
            ReleaseStatus = NullIfBlank(snap?.Status),
            Source = NullIfBlank(snap?.Source),
            AgeRating = AgeRatingCodes.Shorten(snap?.AgeRating),
            AddedAt = item.AddedAt,
            UpdatedAt = item.UpdatedAt
        };
    }

    /// <summary>
    /// Re-derives chip values and counts from the freshly loaded library, keeping whatever
    /// the user had selected if that value still exists.
    /// </summary>
    private void RebuildFilterOptions()
    {
        var options = LibraryFilterEngine.BuildOptions(_allItems);

        _suppressFilter = true;
        try
        {
            Fill(_formatGroup, options.Formats, v => v);
            Fill(_genreGroup, options.Genres, v => v);
            Fill(_themeGroup, options.Themes, v => v);
            Fill(_demographicGroup, options.Demographics, v => v);
            Fill(_statusGroup, options.ReleaseStatuses, MediaMetadataDisplay.Status);
            Fill(_sourceGroup, options.Sources, v => v);
            Fill(_ageRatingGroup, options.AgeRatings, v => v);

            YearOptions =
            [
                new YearOption(0, LocalizationManager.Get("Library_AnyYear")),
                .. options.Years.Select(y => new YearOption(y, y.ToString(CultureInfo.InvariantCulture)))
            ];
            if (!options.Years.Contains(YearFrom)) YearFrom = 0;
            if (!options.Years.Contains(YearTo)) YearTo = 0;
        }
        finally
        {
            _suppressFilter = false;
        }
    }

    private void Fill(FilterGroupViewModel group, IReadOnlyList<FilterOption> options, Func<string, string> label)
    {
        var previouslySelected = group.SelectedValues.ToHashSet(StringComparer.OrdinalIgnoreCase);
        group.Chips.Clear();
        foreach (var option in options)
        {
            group.Chips.Add(new FilterChipViewModel(option.Value, label(option.Value), option.Count, OnFilterChanged)
            {
                IsSelected = previouslySelected.Contains(option.Value)
            });
        }
    }

    private LibraryFilter BuildFilter()
    {
        var filter = new LibraryFilter
        {
            // Either order works: picking "2023 – 2015" means the same range as "2015 – 2023".
            YearFrom = YearFrom > 0 && YearTo > 0 ? Math.Min(YearFrom, YearTo) : YearFrom,
            YearTo = YearFrom > 0 && YearTo > 0 ? Math.Max(YearFrom, YearTo) : YearTo,
            Rating = RatingFilter
        };
        filter.Formats.UnionWith(_formatGroup.SelectedValues);
        filter.Genres.UnionWith(_genreGroup.SelectedValues);
        filter.Themes.UnionWith(_themeGroup.SelectedValues);
        filter.Demographics.UnionWith(_demographicGroup.SelectedValues);
        filter.ReleaseStatuses.UnionWith(_statusGroup.SelectedValues);
        filter.Sources.UnionWith(_sourceGroup.SelectedValues);
        filter.AgeRatings.UnionWith(_ageRatingGroup.SelectedValues);
        return filter;
    }

    private void ApplyFilter()
    {
        var filter = BuildFilter();
        var filtered = _allItems.AsEnumerable();

        if (ActiveStatusId == FavoritesTabId)
            filtered = filtered.Where(c => c.IsFavorite);
        else if (ActiveStatusId is not 0 and not RatingsTabId)
            filtered = filtered.Where(c => c.StatusId == ActiveStatusId);

        if (!string.IsNullOrWhiteSpace(SearchQuery))
            filtered = filtered.Where(c =>
                c.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));

        if (!filter.IsEmpty)
            filtered = filtered.Where(c => LibraryFilterEngine.Matches(c, filter));

        Items = new ObservableCollection<LibraryCardViewModel>(LibraryFilterEngine.Sort(filtered, SortMode));
        RebuildActiveFilters(filter);
        OnStateChanged();
    }

    private void RebuildActiveFilters(LibraryFilter filter)
    {
        ActiveFilters.Clear();

        foreach (var chip in FilterGroups.SelectMany(g => g.Chips).Where(c => c.IsSelected))
            ActiveFilters.Add(new ActiveFilterViewModel(chip.Label, () => chip.IsSelected = false));

        if (filter.HasYearRange)
        {
            var label = (filter.YearFrom, filter.YearTo) switch
            {
                (> 0, > 0) when filter.YearFrom == filter.YearTo => filter.YearFrom.ToString(CultureInfo.InvariantCulture),
                (> 0, > 0) => $"{filter.YearFrom}–{filter.YearTo}",
                (> 0, _) => $"≥ {filter.YearFrom}",
                _ => $"≤ {filter.YearTo}"
            };
            ActiveFilters.Add(new ActiveFilterViewModel(label, () =>
            {
                // Reset both, then filter once (one of them may already be 0 and not notify).
                _suppressFilter = true;
                YearFrom = 0;
                YearTo = 0;
                _suppressFilter = false;
                ApplyFilter();
            }));
        }

        if (filter.Rating != RatingFilter.Any)
        {
            var ratingLabel = RatingOptions.First(o => o.Value == filter.Rating).Label;
            ActiveFilters.Add(new ActiveFilterViewModel(
                string.Format(LocalizationManager.Get("Library_MyRatingChip"), ratingLabel),
                () => RatingFilter = RatingFilter.Any));
        }

        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(ShowActiveFilterBar));
        OnPropertyChanged(nameof(FilterButtonText));
    }

    private void OnStateChanged()
    {
        OnPropertyChanged(nameof(HasTotalItems));
        OnPropertyChanged(nameof(IsEmptyLibrary));
        OnPropertyChanged(nameof(IsNoResults));
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(ResultCountText));
    }

    // ── Snapshot → filter data ────────────────────────────────────────────────

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Season year when known; otherwise the year the title started airing/publishing.</summary>
    private static int? ResolveYear(MediaSnapshot? snap)
    {
        if (snap?.Year is > 0) return snap.Year;
        var start = snap?.AiringStart;
        return start is { Length: >= 4 } &&
               int.TryParse(start.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) &&
               year > 1900
            ? year
            : null;
    }

    private static IReadOnlyList<string> ParseNames(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
