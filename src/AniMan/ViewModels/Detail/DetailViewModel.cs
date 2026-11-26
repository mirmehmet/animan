using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using AniMan.Core.Common;
using AniMan.Core.Domain;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AniMan.ViewModels.Detail;

// Not IDisposable on purpose: the container would then keep every transient instance alive
// until shutdown. Neither field needs disposing — the semaphore's wait handle is never
// requested and the token source has no timer.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "Transient view model; see the comment above.")]
public partial class DetailViewModel : ObservableObject
{
    private const int PreviewDecodeWidth = 200;

    private readonly ITrackingService _tracking;
    private readonly ISnapshotService _snapshot;
    private readonly ICatalogService _catalog;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DetailViewModel> _logger;
    private Task? _activeNoteSave;

    /// <summary>One progress write at a time for this title: row toggles and "mark up to here".</summary>
    private readonly SemaphoreSlim _progressGate = new(1, 1);

    /// <summary>Cancelled when the page is left or a new title is loaded into it.</summary>
    private CancellationTokenSource _loadCts = new();

    private int? _pendingStatusId;
    private bool _isSavingStatus;

    private int _previewMalId;
    private MediaType _previewMediaType;

    public int LibraryItemId { get; private set; }

    [ObservableProperty] private LibraryItem? _item;
    [ObservableProperty] private MediaSnapshot? _mediaSnapshot;
    [ObservableProperty] private BitmapImage? _coverImage;
    /// <summary>Episode rows (anime) or chapter rows (manga) of the title, one per number.</summary>
    [ObservableProperty] private ObservableCollection<ProgressRowViewModel> _rows = [];

    /// <summary>What the list shows of <see cref="Rows"/>: one 100-row range at a time.</summary>
    public ProgressPager<ProgressRowViewModel> Pager { get; } = new(r => r.Number, r => r.IsDone);
    [ObservableProperty] private int _watchedCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string _seriesNote = string.Empty;
    private string _lastSavedSeriesNote = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isPreviewMode;
    [ObservableProperty] private bool _isManga;
    [ObservableProperty] private string _backLabel = string.Empty;
    [ObservableProperty] private int _currentStatusId;
    [ObservableProperty] private decimal? _userRating;
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private IReadOnlyList<string> _genres = [];
    [ObservableProperty] private ObservableCollection<StreamingBadgeViewModel> _streamingPlatforms = [];

    public bool HasUnsavedSeriesNote => !IsPreviewMode && LibraryItemId > 0 && SeriesNote != _lastSavedSeriesNote;
    public bool HasStreamingPlatforms => StreamingPlatforms.Count > 0;
    /// <summary>Shown when there is a list to track: a known total, or rows built without one (B3).</summary>
    public bool HasProgress => TotalCount > 0 || Rows.Count > 0;

    /// <summary>
    /// The source knows how many episodes/chapters the title has. Ongoing series often do not:
    /// anime then list the episodes aired so far, manga the highest chapter read plus one.
    /// </summary>
    public bool IsTotalKnown =>
        (IsManga ? MediaSnapshot?.TotalChapters : MediaSnapshot?.TotalEpisodes) is > 0;

    /// <summary>"12", or "?" while the total is unknown.</summary>
    public string ProgressTotalText => TotalCount > 0
        ? TotalCount.ToString(Localization.LocalizationManager.Culture)
        : "?";

    /// <summary>The "read up to chapter" box: ongoing manga only, where no chapter count exists to list.</summary>
    public bool ShowChapterJump => IsManga && !IsPreviewMode && LibraryItemId > 0 && !IsTotalKnown;

    /// <summary>Text of the "read up to chapter" box.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(JumpToChapterCommand))]
    private string _chapterJumpText = string.Empty;

    /// <summary>Highest number the jump box accepts; keeps a typo from creating tens of thousands of rows.</summary>
    private const int MaxJumpChapter = 9999;
    public string ProgressListTitle => Localization.LocalizationManager.Get(IsManga ? "Detail_Chapters" : "Detail_Episodes");
    public string ProgressSuffix => IsManga
        ? Localization.LocalizationManager.Get("Detail_ChaptersSuffix")
        : Localization.LocalizationManager.Get("Detail_EpisodesSuffix");

    public string SeasonDisplay => MediaMetadataDisplay.Season(MediaSnapshot?.Season, MediaSnapshot?.Year);
    public string StatusDisplay => MediaMetadataDisplay.Status(MediaSnapshot?.Status);
    public string AiringDisplay => MediaMetadataDisplay.AiringRange(MediaSnapshot?.AiringStart, MediaSnapshot?.AiringEnd);
    public string StudioLabel => Localization.LocalizationManager.Get(IsManga ? "Detail_Publisher" : "Detail_Studio");

    // ── Extended metadata (empty strings/lists hide their rows) ──────────────
    public string EnglishTitleDisplay => MediaMetadataDisplay.EnglishTitle(MediaSnapshot?.Title, MediaSnapshot?.TitleEnglish);
    public bool HasEnglishTitle => EnglishTitleDisplay.Length > 0;
    public string DurationDisplay => MediaMetadataDisplay.Duration(MediaSnapshot?.DurationMinutes, MediaSnapshot?.TotalEpisodes);
    public string DemographicsDisplay => string.Join(", ", NameList.Parse(MediaSnapshot?.Demographics));
    public string AuthorsDisplay => string.Join(" · ", NameList.Parse(MediaSnapshot?.Authors));
    public IReadOnlyList<string> Themes => NameList.Parse(MediaSnapshot?.Themes);

    public event EventHandler<TrackingAutoCompleteEventArgs>? AutoCompleteRequested;
    public event EventHandler<(int MalId, MediaType MediaType)>? AddToLibraryFromPreviewRequested;
    public event EventHandler<NoteEditEventArgs>? NoteEditRequested;
    public event EventHandler? SoftDeleteCompleted;

    /// <summary>A single action failed; the page shows it (resx title key + reason).</summary>
    public event EventHandler<ActionFailedEventArgs>? ActionFailed;

    /// <summary>
    /// Asks the user before "mark up to here" clears marks after the chosen row. Set by the
    /// page; without it nothing is cleared, so a missing dialog can never lose progress.
    /// </summary>
    public Func<ClearLaterProgressRequest, Task<bool>>? ConfirmClearLaterProgress { get; set; }

    public DetailViewModel(ITrackingService tracking, ISnapshotService snapshot,
        ICatalogService catalog, IHttpClientFactory httpClientFactory,
        ILogger<DetailViewModel> logger)
    {
        _tracking = tracking;
        _snapshot = snapshot;
        _catalog = catalog;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task InitializeFromArgsAsync(DetailNavigationArgs args)
    {
        BackLabel = args.BackLabel ?? string.Empty;
        var ct = BeginLoad();

        try
        {
            if (args.LibraryItemId.HasValue)
            {
                await InitializeAsync(args.LibraryItemId.Value, ct);
                return;
            }

            // Check if already in library (navigated from Discover)
            var found = await _tracking.FindItemIdAsync(args.MalId, args.MediaType, ct);
            if (found is { IsSuccess: true, Value: int itemId })
            {
                await InitializeAsync(itemId, ct);
                return;
            }

            await InitializePreviewAsync(args.MalId, args.MediaType, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The page was left before the title finished loading.
        }
    }

    /// <summary>
    /// Stops the loads still running for this page (catalog, episode titles, streaming, cover),
    /// so leaving the page does not keep the network and the rate limiter busy for nothing.
    /// </summary>
    public void CancelLoads() => _loadCts.Cancel();

    private CancellationToken BeginLoad()
    {
        _loadCts.Cancel();
        _loadCts.Dispose();
        _loadCts = new CancellationTokenSource();
        return _loadCts.Token;
    }

    private async Task InitializeAsync(int libraryItemId, CancellationToken ct)
    {
        LibraryItemId = libraryItemId;
        IsPreviewMode = false;
        IsLoading = true;
        try
        {
            var result = await _tracking.GetItemAsync(libraryItemId, ct);
            ct.ThrowIfCancellationRequested();
            if (!result.IsSuccess) return;

            Item = result.Value!;
            MediaSnapshot = Item.Snapshot;
            IsManga = Item.MediaType == MediaType.Manga;
            CurrentStatusId = Item.StatusId;
            UserRating = Item.UserRating;
            IsFavorite = Item.IsFavorite;

            LoadCoverImage();
            LoadSeriesNote();
            Genres = NameList.Parse(MediaSnapshot?.Genres);
            await LoadProgressAsync();
            if (IsManga)
                BuildRows();
            else
                await LoadEpisodesAndExtrasAsync(forceStreaming: false, ct);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>True while a preview title is being added; disables the add button.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestAddFromPreviewCommand))]
    private bool _isAddingToLibrary;

    public async Task<Result> AddToLibraryAndReloadAsync(int malId, MediaType mediaType, int statusId)
    {
        IsAddingToLibrary = true;
        try
        {
            var result = await _snapshot.SnapshotAsync(malId, mediaType, statusId);
            if (!result.IsSuccess) return Result.Failure(result.Error!, result.Kind);

            // The item is saved; reloading it is display work that leaving the page may cut short.
            var ct = BeginLoad();
            try
            {
                await InitializeAsync(result.Value!.Id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            return Result.Success();
        }
        finally
        {
            IsAddingToLibrary = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRequestAddFromPreview))]
    private void RequestAddFromPreview() =>
        AddToLibraryFromPreviewRequested?.Invoke(this, (_previewMalId, _previewMediaType));

    private bool CanRequestAddFromPreview() => !IsAddingToLibrary;

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        var result = await _tracking.ToggleFavoriteAsync(LibraryItemId);
        if (result.IsSuccess && Item is not null)
        {
            Item.IsFavorite = !Item.IsFavorite;
            IsFavorite = Item.IsFavorite;
            OnPropertyChanged(nameof(Item));
        }
    }

    /// <summary>
    /// Saves the chosen status. Quick successive clicks are coalesced: one save runs at a time
    /// and the last choice wins, so they can never land in the wrong order. Afterwards the pills
    /// are re-synced to what was actually saved — a failed save must not leave the clicked
    /// pill selected.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task UpdateStatusAsync(int statusId)
    {
        _pendingStatusId = statusId;
        if (_isSavingStatus) return; // the running save picks the newest choice up

        _isSavingStatus = true;
        try
        {
            while (_pendingStatusId is int desired)
            {
                _pendingStatusId = null;
                if (desired == CurrentStatusId) continue;

                var result = await _tracking.UpdateStatusAsync(LibraryItemId, desired);
                if (result.IsSuccess)
                {
                    CurrentStatusId = desired;
                    if (Item is not null)
                        Item.StatusId = desired;
                }
                else
                {
                    ActionFailed?.Invoke(this, new ActionFailedEventArgs("Notify_StatusUpdateFailed", result.Kind));
                }
            }
        }
        finally
        {
            _isSavingStatus = false;
            // The clicked pill checked itself; put the selection back on the saved status.
            OnPropertyChanged(nameof(CurrentStatusId));
        }
    }

    [RelayCommand]
    private async Task SetRatingAsync(string? ratingText)
    {
        if (!RatingParser.TryParse(ratingText, out var parsed))
            return;

        var result = await _tracking.SetRatingAsync(LibraryItemId, parsed);
        if (result.IsSuccess)
        {
            UserRating = parsed;
            if (Item is not null)
                Item.UserRating = parsed;
        }
    }

    [RelayCommand]
    private async Task SoftDeleteAsync()
    {
        var result = await _tracking.SoftDeleteAsync(LibraryItemId);
        if (result.IsSuccess)
            SoftDeleteCompleted?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task SaveSeriesNoteAsync()
    {
        if (_activeNoteSave is not null)
            await _activeNoteSave;

        if (IsPreviewMode || LibraryItemId <= 0 || SeriesNote == _lastSavedSeriesNote)
            return;

        var saveTask = RunSaveSeriesNoteLoopAsync();
        _activeNoteSave = saveTask;
        try
        {
            await saveTask;
        }
        finally
        {
            if (ReferenceEquals(_activeNoteSave, saveTask))
                _activeNoteSave = null;
        }
    }

    /// <summary>
    /// Saves the series note synchronously, for app shutdown (Windows sign-out or restart),
    /// where the asynchronous save would be cut off. Safe on the UI thread: the tracking
    /// service never resumes on the caller's context.
    /// </summary>
    public void SaveSeriesNoteBlocking()
    {
        if (!HasUnsavedSeriesNote) return;
        var snapshot = SeriesNote;
        var result = _tracking.SaveNoteAsync(LibraryItemId, null, null, snapshot).GetAwaiter().GetResult();
        if (result.IsSuccess)
            _lastSavedSeriesNote = snapshot;
    }

    /// <summary>Saves a rating synchronously, for app shutdown (see <see cref="SaveSeriesNoteBlocking"/>).</summary>
    public void SaveRatingBlocking(decimal? rating)
    {
        if (IsPreviewMode || LibraryItemId <= 0 || rating == UserRating) return;
        if (_tracking.SetRatingAsync(LibraryItemId, rating).GetAwaiter().GetResult().IsSuccess)
            UserRating = rating;
    }

    private async Task RunSaveSeriesNoteLoopAsync()
    {
        while (!IsPreviewMode && LibraryItemId > 0 && SeriesNote != _lastSavedSeriesNote)
        {
            var itemId = LibraryItemId;
            var snapshot = SeriesNote;
            var result = await _tracking.SaveNoteAsync(itemId, null, null, snapshot);
            if (!result.IsSuccess)
                break;

            _lastSavedSeriesNote = snapshot;
        }
    }

    [RelayCommand]
    private async Task ReSnapshotAsync()
    {
        var result = await _snapshot.ReSnapshotAsync(LibraryItemId);
        if (!result.IsSuccess) return;

        // Re-read the item: the rows are rebuilt from its progress and notes, and the copy
        // loaded when the page opened does not know about marks made since then.
        var fresh = await _tracking.GetItemAsync(LibraryItemId);
        if (fresh.IsSuccess)
            Item = fresh.Value!;
        if (Item is not null)
            Item.Snapshot = result.Value;

        MediaSnapshot = result.Value;
        Genres = NameList.Parse(MediaSnapshot?.Genres);
        LoadCoverImage();
        await LoadProgressAsync();

        if (IsManga)
        {
            BuildRows();
        }
        else if (Item is not null)
        {
            var ct = _loadCts.Token;
            try
            {
                await LoadEpisodesAndExtrasAsync(forceStreaming: true, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task InitializePreviewAsync(int malId, MediaType mediaType, CancellationToken ct)
    {
        IsPreviewMode = true;
        IsManga = mediaType == MediaType.Manga;
        _previewMalId = malId;
        _previewMediaType = mediaType;
        IsLoading = true;
        string? coverUrl = null;

        try
        {
            if (mediaType == MediaType.Anime)
            {
                var r = await _catalog.GetAnimeAsync(malId, ct: ct);
                ct.ThrowIfCancellationRequested();
                if (!r.IsSuccess) return;
                var a = r.Value!;
                MediaSnapshot = new MediaSnapshot
                {
                    Title = a.Title,
                    TitleJapanese = a.TitleJapanese,
                    Synopsis = a.Synopsis,
                    Type = a.Type,
                    Status = a.Status,
                    Studio = a.Studios,
                    TotalEpisodes = a.TotalEpisodes,
                    Season = a.Season,
                    Year = a.Year,
                    MalScore = a.Score,
                    CoverOriginalUrl = a.CoverLargeUrl ?? a.CoverMediumUrl,
                    Genres = "[]",
                    SnapshotAt = DateTime.UtcNow,
                    TitleEnglish = a.TitleEnglish,
                    Themes = a.Themes,
                    Demographics = a.Demographics,
                    DurationMinutes = a.DurationMinutes,
                    AgeRating = a.AgeRating,
                    Source = a.Source
                };
                coverUrl = a.CoverLargeUrl ?? a.CoverMediumUrl;
            }
            else
            {
                var r = await _catalog.GetMangaAsync(malId, ct: ct);
                ct.ThrowIfCancellationRequested();
                if (!r.IsSuccess) return;
                var m = r.Value!;
                MediaSnapshot = new MediaSnapshot
                {
                    Title = m.Title,
                    TitleJapanese = m.TitleJapanese,
                    Synopsis = m.Synopsis,
                    Type = m.Type,
                    Status = m.Status,
                    Studio = m.Serializations,
                    TotalChapters = m.TotalChapters,
                    TotalVolumes = m.TotalVolumes,
                    MalScore = m.Score,
                    CoverOriginalUrl = m.CoverLargeUrl ?? m.CoverMediumUrl,
                    Genres = "[]",
                    SnapshotAt = DateTime.UtcNow,
                    TitleEnglish = m.TitleEnglish,
                    Themes = m.Themes,
                    Demographics = m.Demographics,
                    Authors = m.Authors
                };
                coverUrl = m.CoverLargeUrl ?? m.CoverMediumUrl;
            }

            var genreResult = await _catalog.GetGenresAsync(malId,
                mediaType == MediaType.Anime ? "anime" : "manga", ct);
            ct.ThrowIfCancellationRequested();
            Genres = genreResult.IsSuccess ? genreResult.Value! : [];
            if (mediaType == MediaType.Anime)
                await LoadStreamingAsync(malId, force: false, ct);

            await LoadCoverFromUrlAsync(coverUrl, ct);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void OpenOnMal()
    {
        var type = IsManga ? "manga" : "anime";
        var id = IsPreviewMode ? _previewMalId : Item?.MalId;
        if (id is null or <= 0) return;
        SafeBrowserLauncher.TryOpen($"https://myanimelist.net/{type}/{id}");
    }


    private async Task LoadStreamingAsync(int malId, bool force, CancellationToken ct)
    {
        StreamingPlatforms.Clear();
        OnPropertyChanged(nameof(HasStreamingPlatforms));

        var badges = new List<StreamingBadgeViewModel>();
        var seenPlatforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!IsPreviewMode && Item?.StreamingOverrides is { Count: > 0 } overrides)
        {
            foreach (var o in overrides)
            {
                if (string.IsNullOrWhiteSpace(o.PlatformName))
                    continue;

                var safeUrl = HttpUrlGuard.SanitizeWebUrl(o.Url);
                if (!string.IsNullOrWhiteSpace(o.Url) && safeUrl is null)
                    continue;

                var name = o.PlatformName.Trim();
                if (seenPlatforms.Add(name))
                {
                    badges.Add(new StreamingBadgeViewModel
                    {
                        Name = name,
                        Url = safeUrl,
                        IsUserLink = true,
                        Background = StreamingPlatformColors.GetBrush(name)
                    });
                }
            }
        }

        var result = await _catalog.GetAnimeStreamingAsync(malId, force, ct);
        ct.ThrowIfCancellationRequested();
        if (result.IsSuccess && result.Value is { Count: > 0 } catalogPlatforms)
        {
            foreach (var s in catalogPlatforms)
            {
                if (string.IsNullOrWhiteSpace(s.PlatformName))
                    continue;

                // Cached rows may predate the mapper's LAN filter, so check again here.
                var safeUrl = HttpUrlGuard.SanitizeExternalWebUrl(s.Url);
                if (!string.IsNullOrWhiteSpace(s.Url) && safeUrl is null)
                    continue;

                if (seenPlatforms.Add(s.PlatformName))
                {
                    badges.Add(new StreamingBadgeViewModel
                    {
                        Name = s.PlatformName,
                        Url = safeUrl,
                        Background = StreamingPlatformColors.GetBrush(s.PlatformName)
                    });
                }
            }
        }

        if (badges.Count == 0) return;

        StreamingPlatforms = new ObservableCollection<StreamingBadgeViewModel>(badges);
        OnPropertyChanged(nameof(HasStreamingPlatforms));
    }

    private void LoadCoverImage()
    {
        var bmp = Imaging.CoverImageLoader.FromFile(MediaSnapshot?.CoverLocalPath, decodePixelWidth: 360);
        if (bmp is not null) CoverImage = bmp;
    }

    private async Task LoadCoverFromUrlAsync(string? url, CancellationToken ct)
    {
        try
        {
            if (await Imaging.CoverImageLoader.FromUrlAsync(_httpClientFactory, url, PreviewDecodeWidth, ct) is { } image)
                CoverImage = image;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private void LoadSeriesNote()
    {
        var note = Item?.Notes.FirstOrDefault(n =>
            n.EpisodeNumber is null &&
            n.ChapterNumber is null &&
            !string.IsNullOrWhiteSpace(n.Content));
        SeriesNote = note?.Content ?? string.Empty;
        _lastSavedSeriesNote = SeriesNote;
    }

    private async Task LoadProgressAsync()
    {
        var result = await _tracking.GetProgressAsync(LibraryItemId);
        if (!result.IsSuccess) return;
        WatchedCount = result.Value!.Watched;
        TotalCount = result.Value.Total;
    }

    /// <summary>
    /// Shows the episode rows straight from local data, then fills in episode titles and
    /// streaming platforms side by side. Neither of those may hold the list back: they can
    /// need the network, and offline that is a slow failure.
    /// </summary>
    private async Task LoadEpisodesAndExtrasAsync(bool forceStreaming, CancellationToken ct)
    {
        if (Item is null) return;
        var rows = BuildRows();
        await Task.WhenAll(
            LoadEpisodeTitlesAsync(Item.MalId, rows, ct),
            LoadStreamingAsync(Item.MalId, forceStreaming, ct));
    }

    private async Task LoadEpisodeTitlesAsync(int malId, IReadOnlyList<ProgressRowViewModel> rows, CancellationToken ct)
    {
        // With a known total and no rows there is nothing to title; with an unknown total the
        // episode list may still bring rows.
        if (rows.Count == 0 && IsTotalKnown) return;
        try
        {
            // Best-effort: without titles the rows keep their "Ep N" label.
            var result = await _catalog.GetAnimeEpisodesAsync(malId, ct);
            if (ct.IsCancellationRequested || !result.IsSuccess) return;

            var titles = new Dictionary<int, string?>();
            foreach (var ce in result.Value!)
                titles[ce.EpisodeNumber] = ce.Title;

            // Ongoing anime without a total: list every aired episode (B3).
            if (titles.Count > 0 && GrowRowsTo(titles.Keys.Max()))
                rows = Rows;

            foreach (var row in rows)
            {
                if (titles.TryGetValue(row.Number, out var title))
                    row.Title = title;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Episode titles could not be loaded for anime {MalId}", malId);
        }
    }

    /// <summary>One row per episode (anime) or chapter (manga), with its saved state, date and note mark.</summary>
    private List<ProgressRowViewModel> BuildRows()
    {
        if (Item?.Snapshot is null) return [];

        var manga = IsManga;
        var progress = manga
            ? Item.ChapterProgress
                .GroupBy(cp => cp.ChapterNumber)
                .ToDictionary(g => g.Key, g => (Done: g.First().IsRead, At: g.First().ReadAt))
            : Item.EpisodeProgress
                .GroupBy(ep => ep.EpisodeNumber)
                .ToDictionary(g => g.Key, g => (Done: g.First().IsWatched, At: g.First().WatchedAt));
        var withNotes = Item.Notes
            .Where(n => !string.IsNullOrWhiteSpace(n.Content))
            .Select(n => manga ? n.ChapterNumber : n.EpisodeNumber)
            .OfType<int>()
            .ToHashSet();

        // Unknown total (ongoing series, B3): list what is known for sure — everything up to
        // the highest mark — and for manga one more row for the next chapter. Anime grow to
        // the aired episodes once the episode list arrives (see LoadEpisodeTitlesAsync).
        var total = manga ? Item.Snapshot.TotalChapters : Item.Snapshot.TotalEpisodes;
        if (total is not > 0)
        {
            var highestDone = progress.Where(kv => kv.Value.Done).Select(kv => kv.Key).DefaultIfEmpty(0).Max();
            total = manga ? highestDone + 1 : highestDone;
        }

        _rowProgress = progress;
        _rowNotes = withNotes;
        var rows = new List<ProgressRowViewModel>();
        for (var i = 1; i <= total; i++)
            rows.Add(CreateRow(i));

        Rows = new ObservableCollection<ProgressRowViewModel>(rows);
        Pager.Reset(rows);
        return rows;
    }

    // Saved state the rows were built from, so rows added later (B3) start out right too.
    private Dictionary<int, (bool Done, DateTime? At)> _rowProgress = [];
    private HashSet<int> _rowNotes = [];

    private ProgressRowViewModel CreateRow(int number)
    {
        var row = new ProgressRowViewModel(_tracking, _logger, _progressGate, IsManga)
        {
            LibraryItemId = LibraryItemId,
            Number = number,
            HasNote = _rowNotes.Contains(number)
        };
        var done = _rowProgress.TryGetValue(number, out var p) && p.Done;
        row.InitDone(done, done ? p.At : null);

        row.Toggled += OnRowToggled;
        row.MarkUpToHereRequested += OnRowMarkUpToHere;
        row.NoteRequested += OnRowNoteRequested;
        row.SaveFailed += OnProgressSaveFailed;
        return row;
    }

    /// <summary>
    /// Adds rows up to <paramref name="count"/> when the total is unknown (B3): aired episodes
    /// that arrived with the episode list, or the next chapter after the last one read.
    /// Never removes rows. Returns whether any were added.
    /// </summary>
    private bool GrowRowsTo(int count)
    {
        if (IsTotalKnown || count <= Rows.Count) return false;

        for (var i = Rows.Count + 1; i <= count; i++)
            Rows.Add(CreateRow(i));
        Pager.Reset(Rows);
        OnPropertyChanged(nameof(HasProgress));
        return true;
    }

    /// <summary>Ongoing manga: keep one unread row after the highest chapter read.</summary>
    private void EnsureNextChapterRow()
    {
        if (!IsManga) return;
        var highestDone = Rows.Where(r => r.IsDone).Select(r => r.Number).DefaultIfEmpty(0).Max();
        GrowRowsTo(highestDone + 1);
    }

    private void OnProgressSaveFailed(object? sender, ErrorKind kind) =>
        ActionFailed?.Invoke(this, new ActionFailedEventArgs("Notify_ProgressSaveFailed", kind));

    /// <summary>Asks the page to offer "mark as completed" — unless the title already is.</summary>
    private void RequestAutoCompleteIfNeeded()
    {
        if (CurrentStatusId == TrackingStatusIds.Completed) return;
        AutoCompleteRequested?.Invoke(this, new TrackingAutoCompleteEventArgs(LibraryItemId, IsManga));
    }

    // ── Row handlers ──────────────────────────────────────────────────────────

    // async void: event handlers must never let an exception escape to the dispatcher.
    private async void OnRowToggled(object? sender, ProgressRowViewModel row)
    {
        try
        {
            EnsureNextChapterRow();
            await LoadProgressAsync();

            if (WatchedCount == TotalCount && TotalCount > 0)
                RequestAutoCompleteIfNeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Progress toggle handling failed for item {Id}", LibraryItemId);
        }
    }

    private async void OnRowMarkUpToHere(object? sender, ProgressRowViewModel row)
    {
        try
        {
            await MarkUpToAsync(row.Number);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mark-up-to-here failed for item {Id}", LibraryItemId);
            OnProgressSaveFailed(this, ErrorKind.Unexpected);
        }
    }

    /// <summary>"Read up to chapter N" for ongoing manga: the same as "mark up to here" on row N.</summary>
    [RelayCommand(CanExecute = nameof(CanJumpToChapter))]
    private async Task JumpToChapterAsync()
    {
        if (!TryParseJumpChapter(out var target)) return;
        try
        {
            if (await MarkUpToAsync(target))
                ChapterJumpText = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Jump to chapter failed for item {Id}", LibraryItemId);
            OnProgressSaveFailed(this, ErrorKind.Unexpected);
        }
    }

    private bool CanJumpToChapter() => TryParseJumpChapter(out _);

    private bool TryParseJumpChapter(out int chapter) =>
        int.TryParse(ChapterJumpText.Trim(), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out chapter)
        && chapter is > 0 and <= MaxJumpChapter;

    /// <summary>
    /// "Mark up to here" means exactly up to here: marks after the chosen number are removed,
    /// with their dates. When there are such marks the user confirms first. The write holds the
    /// progress gate, so it cannot interleave with a row toggle that is still saving.
    /// </summary>
    /// <returns><c>true</c> when saved; <c>false</c> when the user cancelled or the save failed.</returns>
    private async Task<bool> MarkUpToAsync(int target)
    {
        var later = Rows.Count(r => r.Number > target && r.IsDone);
        if (later > 0)
        {
            var confirmed = ConfirmClearLaterProgress is { } confirm
                            && await confirm(new ClearLaterProgressRequest(target, later, IsManga));
            if (!confirmed) return false;
        }

        TrackingResult saved;
        await _progressGate.WaitAsync();
        try
        {
            var result = IsManga
                ? await _tracking.MarkChaptersUpToAsync(LibraryItemId, target)
                : await _tracking.MarkUpToHereAsync(LibraryItemId, target);
            if (!result.IsSuccess)
            {
                OnProgressSaveFailed(this, result.Kind);
                return false;
            }
            saved = result.Value!;

            // A jump past the listed chapters (ongoing manga) needs their rows first.
            GrowRowsTo(target);

            // Already persisted by the service — sync the checkboxes both ways without
            // re-triggering writes: at/below the target done (dates kept), above it cleared.
            // Still under the gate, so a queued row toggle sees the final state.
            foreach (var r in Rows)
            {
                if (r.Number <= target)
                    r.InitDone(true, r.DoneAt ?? DateTime.UtcNow);
                else
                    r.InitDone(false, null);
            }
            EnsureNextChapterRow();
        }
        finally
        {
            _progressGate.Release();
        }

        await LoadProgressAsync();

        if (saved.AutoCompleteNeeded)
            RequestAutoCompleteIfNeeded();
        return true;
    }

    private void OnRowNoteRequested(object? sender, ProgressRowViewModel row)
    {
        int? episode = IsManga ? null : row.Number;
        int? chapter = IsManga ? row.Number : null;
        var current = Item?.Notes
            .FirstOrDefault(n => n.EpisodeNumber == episode && n.ChapterNumber == chapter)?.Content
            ?? string.Empty;
        NoteEditRequested?.Invoke(this, new NoteEditEventArgs(episode, chapter, current));
    }

    // ── Note persistence (called back from the view after the dialog) ──────────

    public async Task SaveItemNoteAsync(int? episodeNumber, int? chapterNumber, string content)
    {
        var result = await _tracking.SaveNoteAsync(LibraryItemId, episodeNumber, chapterNumber, content);
        if (!result.IsSuccess) return;

        bool hasText = !string.IsNullOrWhiteSpace(content);
        if ((episodeNumber ?? chapterNumber) is int number &&
            Rows.FirstOrDefault(r => r.Number == number) is { } noteRow)
        {
            noteRow.HasNote = hasText;
        }

        // Keep the in-memory note cache in sync for subsequent edits.
        if (Item is not null)
        {
            var note = Item.Notes.FirstOrDefault(n =>
                n.EpisodeNumber == episodeNumber && n.ChapterNumber == chapterNumber);
            if (!hasText)
            {
                if (note is not null)
                    Item.Notes.Remove(note);
            }
            else if (note is null)
            {
                Item.Notes.Add(new Note
                {
                    LibraryItemId = LibraryItemId,
                    EpisodeNumber = episodeNumber,
                    ChapterNumber = chapterNumber,
                    Content = content
                });
            }
            else
            {
                note.Content = content;
            }
        }
    }

    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasProgress));
        OnPropertyChanged(nameof(ProgressTotalText));
    }

    partial void OnIsMangaChanged(bool value)
    {
        OnPropertyChanged(nameof(ProgressListTitle));
        OnPropertyChanged(nameof(ProgressSuffix));
        OnPropertyChanged(nameof(IsTotalKnown));
        OnPropertyChanged(nameof(ShowChapterJump));
        OnPropertyChanged(nameof(StudioLabel));
    }

    partial void OnMediaSnapshotChanged(MediaSnapshot? value)
    {
        OnPropertyChanged(nameof(SeasonDisplay));
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(AiringDisplay));
        OnPropertyChanged(nameof(EnglishTitleDisplay));
        OnPropertyChanged(nameof(HasEnglishTitle));
        OnPropertyChanged(nameof(DurationDisplay));
        OnPropertyChanged(nameof(DemographicsDisplay));
        OnPropertyChanged(nameof(AuthorsDisplay));
        OnPropertyChanged(nameof(Themes));
        OnPropertyChanged(nameof(IsTotalKnown));
        OnPropertyChanged(nameof(ShowChapterJump));
    }

    partial void OnRowsChanged(ObservableCollection<ProgressRowViewModel> value) =>
        OnPropertyChanged(nameof(HasProgress));

    partial void OnIsPreviewModeChanged(bool value) => OnPropertyChanged(nameof(ShowChapterJump));
}

public record TrackingAutoCompleteEventArgs(int LibraryItemId, bool IsManga);

public record NoteEditEventArgs(int? EpisodeNumber, int? ChapterNumber, string CurrentText);

/// <summary>A failed single action: <paramref name="TitleKey"/> is the snackbar's resx title.</summary>
public record ActionFailedEventArgs(string TitleKey, ErrorKind Kind);

/// <summary>"Mark up to <paramref name="Number"/>" would clear <paramref name="LaterMarks"/> later marks.</summary>
public record ClearLaterProgressRequest(int Number, int LaterMarks, bool IsManga);
