using System.Collections.ObjectModel;
using System.Globalization;
using AniMan.Core.Common;
using AniMan.Core.Domain;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Core.Library;
using AniMan.Imaging;
using AniMan.Localization;
using AniMan.Navigation;
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
    private readonly ILibraryVersion _libraryVersion;
    private readonly MediaType _mediaType;
    private List<LibraryCardViewModel> _allItems = [];

    /// <summary>Library version the cards were built at; -1 = never loaded successfully.</summary>
    private long _loadedVersion = -1;

    /// <summary>
    /// Above this many moves/inserts a filter change replaces the list instead of editing it
    /// (see <see cref="CollectionSync.TryApply{T}"/>).
    /// </summary>
    private const int MaxInPlaceChanges = 60;
    private int _coverLoadVersion;
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
    private readonly FilterGroupViewModel _genreGroup;
    private readonly FilterGroupViewModel _themeGroup;
    private readonly FilterGroupViewModel _demographicGroup = new(LocalizationManager.Get("Library_FilterDemographic"));
    private readonly FilterGroupViewModel _statusGroup = new(LocalizationManager.Get("Library_FilterStatus"));
    private readonly FilterGroupViewModel _sourceGroup = new(LocalizationManager.Get("Library_FilterSource"));
    private readonly FilterGroupViewModel _ageRatingGroup = new(LocalizationManager.Get("Library_FilterAgeRating"));

    /// <summary>Chip groups in panel order.</summary>
    public IReadOnlyList<FilterGroupViewModel> FilterGroups { get; }

    public ObservableCollection<ActiveFilterViewModel> ActiveFilters { get; } = [];

    public bool IsRatingsTab => ActiveStatusId == RatingsTabId;

    /// <summary>Localized reason the last load failed; null after a successful load.</summary>
    public string? LoadErrorMessage { get; private set; }

    public bool HasTotalItems => _allItems.Count > 0;
    public bool IsEmptyLibrary => !IsLoading && _allItems.Count == 0 && LoadErrorMessage is null;
    public bool IsNoResults => !IsLoading && _allItems.Count > 0 && Items.Count == 0;
    // Not hidden while a reload runs: the previous list stays on screen (and keeps its scroll
    // position) until the new one is ready.
    public bool HasContent => Items.Count > 0;

    public bool HasActiveFilters => ActiveFilters.Count > 0;
    public bool ShowActiveFilterBar => HasActiveFilters && !IsFilterPanelOpen;

    public string FilterButtonText => ActiveFilters.Count > 0
        ? string.Format(LocalizationManager.Get("Library_FiltersWithCount"), ActiveFilters.Count)
        : LocalizationManager.Get("Library_Filters");

    /// <summary>"42 / 180" while something narrows the list, otherwise just the total.</summary>
    public string ResultCountText => Items.Count == _allItems.Count
        ? _allItems.Count.ToString(LocalizationManager.Culture)
        : string.Format(LocalizationManager.Get("Library_ResultCount"), Items.Count, _allItems.Count);

    private string SortSettingKey => $"LibrarySort.{_mediaType}";

    /// <summary>Cover-size slider state, shared by the anime and manga pages.</summary>
    public LibraryLayoutViewModel Layout { get; }

    // ── What differs between the anime and manga page (the view is shared) ───────

    public MediaType MediaType => _mediaType;
    private bool IsAnime => _mediaType == MediaType.Anime;

    /// <summary>Resource key of the page name: the heading, and the detail page's back button.</summary>
    public string PageTitleKey => IsAnime ? "Nav_Anime" : "Nav_Manga";
    public string PageTitle => LocalizationManager.Get(PageTitleKey);
    public string SearchPlaceholder => LocalizationManager.Get(IsAnime ? "Library_SearchAnime" : "Library_SearchManga");
    public string TabGroupName => $"{_mediaType}LibraryFilter";
    public Wpf.Ui.Controls.SymbolRegular EmptyIcon =>
        IsAnime ? Wpf.Ui.Controls.SymbolRegular.MoviesAndTv24 : Wpf.Ui.Controls.SymbolRegular.BookOpen24;

    private int InProgressStatusId => IsAnime ? TrackingStatusIds.Watching : TrackingStatusIds.Reading;
    private int PlannedStatusId => IsAnime ? TrackingStatusIds.PlanToWatch : TrackingStatusIds.PlanToRead;
    public string InProgressTabLabel => LocalizationManager.Get(IsAnime ? "Status_Watching" : "Status_Reading");
    public string PlannedTabLabel => LocalizationManager.Get(IsAnime ? "Status_PlanToWatch" : "Status_PlanToRead");

    /// <summary>"Watching"/"Reading" tab; two-way for its radio button.</summary>
    public bool IsInProgressTab
    {
        get => ActiveStatusId == InProgressStatusId;
        set { if (value) ActiveStatusId = InProgressStatusId; }
    }

    /// <summary>"Plan to watch"/"Plan to read" tab; two-way for its radio button.</summary>
    public bool IsPlannedTab
    {
        get => ActiveStatusId == PlannedStatusId;
        set { if (value) ActiveStatusId = PlannedStatusId; }
    }

    protected LibraryViewModelBase(
        ITrackingService tracking, ISettingsService settings, LibraryLayoutViewModel layout,
        ILibraryVersion libraryVersion, MediaType mediaType)
    {
        _tracking = tracking;
        _settings = settings;
        _libraryVersion = libraryVersion;
        _mediaType = mediaType;
        Layout = layout;
        Layout.CoverResolutionChanged += (_, width) => _ = LoadCoversAsync(width);
        // Genres and themes can match "all selected" (default) or "any selected".
        _genreGroup = new(LocalizationManager.Get("Library_FilterGenre"), supportsMatchMode: true, OnFilterChanged);
        _themeGroup = new(LocalizationManager.Get("Library_FilterTheme"), supportsMatchMode: true, OnFilterChanged);
        FilterGroups =
        [
            _formatGroup, _genreGroup, _themeGroup, _demographicGroup,
            _statusGroup, _sourceGroup, _ageRatingGroup
        ];
    }

    /// <summary>Total episodes (anime) or chapters (manga) from the snapshot.</summary>
    protected abstract int? GetTotal(MediaSnapshot? snapshot);

    /// <summary>
    /// Loads the library — or does nothing when it has not changed since the last load, so
    /// coming back from a detail page keeps the list, its covers and the scroll position.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        // Read before loading: a write that lands during the load bumps the version again
        // and is picked up on the next visit.
        var version = _libraryVersion.Current;
        if (version == _loadedVersion && LoadErrorMessage is null)
            return;

        IsLoading = true;
        LoadErrorMessage = null; // a retry hides the old error while it runs
        OnStateChanged();
        try
        {
            await LoadSortPreferenceOnceAsync();
            await Layout.EnsureLoadedAsync();

            var result = await _tracking.GetLibraryOverviewAsync(_mediaType);
            if (!result.IsSuccess)
            {
                // An error screen only when there is nothing to show: a failed reload keeps
                // the list from the previous load on screen instead of hiding it.
                LoadErrorMessage = _allItems.Count == 0 ? ErrorText.For(result.Kind) : null;
                return;
            }

            LoadErrorMessage = null;
            _loadedVersion = version;
            _allItems = result.Value!.Select(ToCard).ToList();

            _ = LoadCoversAsync(Layout.CoverDecodeWidth);

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
            foreach (var group in FilterGroups)
                group.MatchAny = false;
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

    /// <summary>
    /// Replaces the current tab, search and filters with a request from another page
    /// (Stats → "show me my Action titles"). Call after the library has loaded so the
    /// requested chip exists. The panel stays closed; the active-filter chips show what applies.
    /// </summary>
    public void ApplyExternalFilter(LibraryFilterRequest request)
    {
        _suppressFilter = true;
        try
        {
            foreach (var chip in FilterGroups.SelectMany(g => g.Chips))
                chip.IsSelected = false;
            foreach (var group in FilterGroups)
                group.MatchAny = false;
            RatingFilter = RatingFilter.Any;
            YearFrom = request.YearFrom;
            YearTo = request.YearTo;
            SearchQuery = string.Empty;
            IsFilterPanelOpen = false;

            if (request.Genre is { } genre &&
                _genreGroup.Chips.FirstOrDefault(c => string.Equals(c.Value, genre, StringComparison.OrdinalIgnoreCase)) is { } match)
            {
                match.IsSelected = true;
            }

            ActiveStatusId = request.StatusId;
        }
        finally
        {
            _suppressFilter = false;
        }
        ApplyFilter();
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (!_suppressFilter)
            ApplyFilter();
    }

    partial void OnActiveStatusIdChanged(int value)
    {
        OnPropertyChanged(nameof(IsRatingsTab));
        OnPropertyChanged(nameof(IsInProgressTab));
        OnPropertyChanged(nameof(IsPlannedTab));
        if (!_suppressFilter)
            ApplyFilter();
    }

    partial void OnSortModeChanged(LibrarySort value)
    {
        ApplyFilter();
        if (_sortLoaded)
            _settings.SaveInBackground(SortSettingKey, value.ToString());
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

    private LibraryCardViewModel ToCard(LibraryOverviewItem entry)
    {
        var item = entry.Item;
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
            UserRating = item.UserRating,
            MalScore = snap?.MalScore,
            IsFavorite = item.IsFavorite,
            ProgressWatched = entry.ProgressDone,
            ProgressTotal = GetTotal(snap),
            Format = NullIfBlank(snap?.Type),
            Year = ResolveYear(snap),
            Genres = NameList.Parse(snap?.Genres),
            Themes = NameList.Parse(snap?.Themes),
            Demographics = NameList.Parse(snap?.Demographics),
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
            Rating = RatingFilter,
            GenresMatchAny = _genreGroup.MatchAny,
            ThemesMatchAny = _themeGroup.MatchAny
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
            filtered = filtered.Where(c => TitleText.Matches(c.SearchKey, SearchQuery));

        if (!filter.IsEmpty)
            filtered = filtered.Where(c => LibraryFilterEngine.Matches(c, filter));

        // Edit the bound list in place when the change is small (typing in the search box,
        // ticking a chip): cards that stay keep their containers. A large change replaces it.
        var result = LibraryFilterEngine.Sort(filtered, SortMode, LocalizationManager.Culture).ToList();
        if (!CollectionSync.TryApply(Items, result, MaxInPlaceChanges))
            Items = new ObservableCollection<LibraryCardViewModel>(result);
        foreach (var group in FilterGroups)
            group.RefreshSelectionState();
        RebuildActiveFilters(filter);
        OnStateChanged();
    }

    private void RebuildActiveFilters(LibraryFilter filter)
    {
        ActiveFilters.Clear();

        foreach (var group in FilterGroups)
        {
            var selected = group.Chips.Where(c => c.IsSelected).ToList();

            // "Any of" selections read as one alternative set: a single "Action / Drama" chip
            // whose ✕ clears the whole set. "All of" selections stay one chip per value.
            if (group.MatchAny && selected.Count >= 2)
            {
                ActiveFilters.Add(new ActiveFilterViewModel(
                    string.Join(" / ", selected.Select(c => c.Label)),
                    () => DeselectAll(selected)));
                continue;
            }

            foreach (var chip in selected)
                ActiveFilters.Add(new ActiveFilterViewModel(chip.Label, () => chip.IsSelected = false));
        }

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

    /// <summary>
    /// Decodes covers off the UI thread, a batch at a time, at the given pixel width. Runs on
    /// load and again when the cover-size slider settles; a newer request abandons an older one.
    /// Frozen bitmaps (see CoverImageLoader) are safe to hand back to the UI thread.
    /// </summary>
    private async Task LoadCoversAsync(int decodeWidth)
    {
        var version = ++_coverLoadVersion;
        foreach (var batch in _allItems.ToList().Chunk(24))
        {
            var decoded = await Task.Run(() => batch
                .Select(card => (Card: card, Image: CoverImageLoader.FromFile(card.CoverLocalPath, decodeWidth)))
                .ToList());

            if (version != _coverLoadVersion) return;
            foreach (var (card, image) in decoded)
            {
                if (image is not null)
                    card.CoverImage = image;
            }
        }
    }

    private void DeselectAll(IEnumerable<FilterChipViewModel> chips)
    {
        _suppressFilter = true;
        try
        {
            foreach (var chip in chips)
                chip.IsSelected = false;
        }
        finally
        {
            _suppressFilter = false;
        }
        ApplyFilter();
    }

    private void OnStateChanged()
    {
        OnPropertyChanged(nameof(LoadErrorMessage));
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
    internal static int? ResolveYear(MediaSnapshot? snap)
    {
        if (snap?.Year is > 0) return snap.Year;
        var start = snap?.AiringStart;
        return start is { Length: >= 4 } &&
               int.TryParse(start.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year) &&
               year > 1900
            ? year
            : null;
    }

}
