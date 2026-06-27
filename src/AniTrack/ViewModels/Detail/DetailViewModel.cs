using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using AniTrack.Core.Domain.Enums;
using AniTrack.Core.Domain.Models;
using AniTrack.Core.Interfaces;
using AniTrack.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AniTrack.ViewModels.Detail;

public partial class DetailViewModel : ObservableObject
{
    private readonly ITrackingService _tracking;
    private readonly ISnapshotService _snapshot;
    private readonly ICatalogService _catalog;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DetailViewModel> _logger;

    private int _previewMalId;
    private MediaType _previewMediaType;

    public int LibraryItemId { get; private set; }

    [ObservableProperty] private LibraryItem? _item;
    [ObservableProperty] private MediaSnapshot? _mediaSnapshot;
    [ObservableProperty] private BitmapImage? _coverImage;
    [ObservableProperty] private ObservableCollection<EpisodeRowViewModel> _episodes = [];
    [ObservableProperty] private ObservableCollection<ChapterRowViewModel> _chapters = [];
    [ObservableProperty] private int _watchedCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private string _seriesNote = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isPreviewMode;
    [ObservableProperty] private bool _isManga;
    [ObservableProperty] private string _backLabel = string.Empty;
    [ObservableProperty] private int _currentStatusId;
    [ObservableProperty] private decimal? _userRating;
    [ObservableProperty] private bool _isFavorite;

    public bool HasProgress => TotalCount > 0;
    public bool HasEpisodes => !IsManga && TotalCount > 0;
    public bool HasChapters => IsManga && TotalCount > 0;
    public string ProgressSuffix => IsManga
        ? Localization.LocalizationManager.Get("Detail_ChaptersSuffix")
        : Localization.LocalizationManager.Get("Detail_EpisodesSuffix");

    public event EventHandler<TrackingAutoCompleteEventArgs>? AutoCompleteRequested;
    public event EventHandler<(int MalId, MediaType MediaType)>? AddToLibraryFromPreviewRequested;
    public event EventHandler<NoteEditEventArgs>? NoteEditRequested;
    public event EventHandler<MediaType>? NavigateToFavoritesRequested;
    public event EventHandler? SoftDeleteCompleted;

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
        BackLabel = args.BackLabel;

        if (args.LibraryItemId.HasValue)
        {
            await InitializeAsync(args.LibraryItemId.Value);
            return;
        }

        // Check if already in library (navigated from Discover)
        var libraryResult = await _tracking.GetLibraryAsync(
            args.MediaType == MediaType.Anime ? "anime" : "manga");
        if (libraryResult.IsSuccess)
        {
            var found = libraryResult.Value!.FirstOrDefault(i => i.MalId == args.MalId);
            if (found != null)
            {
                await InitializeAsync(found.Id);
                return;
            }
        }

        await InitializePreviewAsync(args.MalId, args.MediaType);
    }

    public async Task InitializeAsync(int libraryItemId)
    {
        LibraryItemId = libraryItemId;
        IsPreviewMode = false;
        IsLoading = true;
        try
        {
            var result = await _tracking.GetItemAsync(libraryItemId);
            if (!result.IsSuccess) return;

            Item = result.Value!;
            MediaSnapshot = Item.Snapshot;
            IsManga = Item.MediaType == MediaType.Manga;
            CurrentStatusId = Item.StatusId;
            UserRating = Item.UserRating;
            IsFavorite = Item.IsFavorite;

            LoadCoverImage();
            LoadSeriesNote();
            await LoadProgressAsync();
            if (IsManga)
                await LoadChaptersAsync();
            else
                await LoadEpisodesAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task AddToLibraryAndReloadAsync(int malId, MediaType mediaType, int statusId)
    {
        var result = await _snapshot.SnapshotAsync(malId, mediaType, statusId);
        if (!result.IsSuccess) return;

        BackLabel = BackLabel; // preserve
        await InitializeAsync(result.Value!.Id);
    }

    [RelayCommand]
    private void RequestAddFromPreview() =>
        AddToLibraryFromPreviewRequested?.Invoke(this, (_previewMalId, _previewMediaType));

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        var result = await _tracking.ToggleFavoriteAsync(LibraryItemId);
        if (result.IsSuccess && Item is not null)
        {
            Item.IsFavorite = !Item.IsFavorite;
            IsFavorite = Item.IsFavorite;
            OnPropertyChanged(nameof(Item));
            if (IsFavorite)
                NavigateToFavoritesRequested?.Invoke(this, Item.MediaType);
        }
    }

    [RelayCommand]
    private async Task UpdateScoreAsync(int? score)
    {
        var result = await _tracking.UpdateScoreAsync(LibraryItemId, score);
        if (!result.IsSuccess)
            _logger.LogWarning("Score update failed for item {Id}: {Error}", LibraryItemId, result.Error);
    }

    [RelayCommand]
    private async Task UpdateStatusAsync(int statusId)
    {
        var result = await _tracking.UpdateStatusAsync(LibraryItemId, statusId);
        if (result.IsSuccess)
            CurrentStatusId = statusId;
    }

    [RelayCommand]
    private async Task SetRatingAsync(string? ratingText)
    {
        if (string.IsNullOrWhiteSpace(ratingText))
        {
            var r = await _tracking.SetRatingAsync(LibraryItemId, null);
            if (r.IsSuccess) UserRating = null;
            return;
        }

        if (!decimal.TryParse(ratingText, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return;

        parsed = Math.Round(Math.Clamp(parsed, 0m, 10m), 1);
        var result = await _tracking.SetRatingAsync(LibraryItemId, parsed);
        if (result.IsSuccess) UserRating = parsed;
    }

    [RelayCommand]
    private async Task SoftDeleteAsync()
    {
        var result = await _tracking.SoftDeleteAsync(LibraryItemId);
        if (result.IsSuccess)
            SoftDeleteCompleted?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task IncrementRewatchAsync()
    {
        await _tracking.IncrementRewatchAsync(LibraryItemId, DateOnly.FromDateTime(DateTime.Today));
        if (Item is not null)
        {
            Item.RewatchCount++;
            OnPropertyChanged(nameof(Item));
        }
    }

    [RelayCommand]
    private async Task SaveSeriesNoteAsync()
    {
        await _tracking.SaveNoteAsync(LibraryItemId, null, null, SeriesNote);
    }

    [RelayCommand]
    private async Task ReSnapshotAsync()
    {
        var result = await _snapshot.ReSnapshotAsync(LibraryItemId);
        if (result.IsSuccess)
        {
            MediaSnapshot = result.Value;
            LoadCoverImage();
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task InitializePreviewAsync(int malId, MediaType mediaType)
    {
        IsPreviewMode = true;
        _previewMalId = malId;
        _previewMediaType = mediaType;
        IsLoading = true;
        string? coverUrl = null;

        try
        {
            if (mediaType == MediaType.Anime)
            {
                var r = await _catalog.GetAnimeAsync(malId);
                if (!r.IsSuccess) return;
                var a = r.Value!;
                MediaSnapshot = new MediaSnapshot
                {
                    Title = a.Title,
                    TitleJapanese = a.TitleJapanese,
                    Synopsis = a.Synopsis,
                    TotalEpisodes = a.TotalEpisodes,
                    MalScore = a.Score,
                    CoverOriginalUrl = a.CoverLargeUrl ?? a.CoverMediumUrl,
                    Genres = "[]",
                    SnapshotAt = DateTime.UtcNow
                };
                coverUrl = a.CoverLargeUrl ?? a.CoverMediumUrl;
            }
            else
            {
                var r = await _catalog.GetMangaAsync(malId);
                if (!r.IsSuccess) return;
                var m = r.Value!;
                MediaSnapshot = new MediaSnapshot
                {
                    Title = m.Title,
                    TitleJapanese = m.TitleJapanese,
                    Synopsis = m.Synopsis,
                    TotalChapters = m.TotalChapters,
                    TotalVolumes = m.TotalVolumes,
                    MalScore = m.Score,
                    CoverOriginalUrl = m.CoverLargeUrl ?? m.CoverMediumUrl,
                    Genres = "[]",
                    SnapshotAt = DateTime.UtcNow
                };
                coverUrl = m.CoverLargeUrl ?? m.CoverMediumUrl;
            }

            await LoadCoverFromUrlAsync(coverUrl);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void LoadCoverImage()
    {
        var path = MediaSnapshot?.CoverLocalPath;
        if (path is null || !File.Exists(path)) return;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            CoverImage = bmp;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Local cover load failed for item {Id}", LibraryItemId);
        }
    }

    private async Task LoadCoverFromUrlAsync(string? url)
    {
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            var http = _httpClientFactory.CreateClient("covers");
            var bytes = await http.GetByteArrayAsync(url);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.StreamSource = new MemoryStream(bytes);
                img.DecodePixelWidth = 200;
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.EndInit();
                img.Freeze();
                CoverImage = img;
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "URL cover load failed for preview {MalId}", _previewMalId);
        }
    }

    private void LoadSeriesNote()
    {
        var note = Item?.Notes.FirstOrDefault(n => n.EpisodeNumber is null && n.ChapterNumber is null);
        SeriesNote = note?.Content ?? string.Empty;
    }

    private async Task LoadProgressAsync()
    {
        var result = await _tracking.GetProgressAsync(LibraryItemId);
        if (!result.IsSuccess) return;
        WatchedCount = result.Value!.Watched;
        TotalCount = result.Value.Total;
    }

    private async Task LoadEpisodesAsync()
    {
        if (Item?.Snapshot is null) return;

        var episodeProgress = Item.EpisodeProgress
            .ToDictionary(ep => ep.EpisodeNumber, ep => ep);
        var episodeNotes = Item.Notes
            .Where(n => n.EpisodeNumber.HasValue)
            .Select(n => n.EpisodeNumber!.Value)
            .ToHashSet();

        int total = Item.Snapshot.TotalEpisodes ?? 0;

        // Episode titles (best-effort; list shows "Ep N" if unavailable).
        var titleMap = new Dictionary<int, string?>();
        var episodesResult = await _catalog.GetAnimeEpisodesAsync(Item.MalId);
        if (episodesResult.IsSuccess)
            foreach (var ce in episodesResult.Value!)
                titleMap[ce.EpisodeNumber] = ce.Title;

        var rows = new List<EpisodeRowViewModel>();
        for (int i = 1; i <= total; i++)
        {
            var row = new EpisodeRowViewModel(_tracking)
            {
                LibraryItemId = LibraryItemId,
                EpisodeNumber = i,
                Title = titleMap.GetValueOrDefault(i),
                HasNote = episodeNotes.Contains(i)
            };
            row.InitWatched(episodeProgress.TryGetValue(i, out var ep) && ep.IsWatched);

            row.WatchedToggled += OnEpisodeWatchedToggled;
            row.MarkUpToHereRequested += OnEpisodeMarkUpToHere;
            row.NoteRequested += OnEpisodeNoteRequested;
            rows.Add(row);
        }

        Episodes = new ObservableCollection<EpisodeRowViewModel>(rows);
    }

    private async Task LoadChaptersAsync()
    {
        if (Item?.Snapshot is null) return;

        var chapterProgress = Item.ChapterProgress
            .ToDictionary(cp => cp.ChapterNumber, cp => cp);
        var chapterNotes = Item.Notes
            .Where(n => n.ChapterNumber.HasValue)
            .Select(n => n.ChapterNumber!.Value)
            .ToHashSet();

        int total = Item.Snapshot.TotalChapters ?? 0;

        var rows = new List<ChapterRowViewModel>();
        for (int i = 1; i <= total; i++)
        {
            var row = new ChapterRowViewModel(_tracking)
            {
                LibraryItemId = LibraryItemId,
                ChapterNumber = i,
                HasNote = chapterNotes.Contains(i)
            };
            row.InitRead(chapterProgress.TryGetValue(i, out var cp) && cp.IsRead);

            row.ReadToggled += OnChapterReadToggled;
            row.MarkUpToHereRequested += OnChapterMarkUpToHere;
            row.NoteRequested += OnChapterNoteRequested;
            rows.Add(row);
        }

        Chapters = new ObservableCollection<ChapterRowViewModel>(rows);
    }

    // ── Episode handlers ──────────────────────────────────────────────────────

    private async void OnEpisodeWatchedToggled(object? sender, EpisodeRowViewModel row)
    {
        await LoadProgressAsync();

        if (WatchedCount == TotalCount && TotalCount > 0)
            AutoCompleteRequested?.Invoke(this, new TrackingAutoCompleteEventArgs(LibraryItemId, IsManga: false));
    }

    private async void OnEpisodeMarkUpToHere(object? sender, EpisodeRowViewModel row)
    {
        var result = await _tracking.MarkUpToHereAsync(LibraryItemId, row.EpisodeNumber);
        if (!result.IsSuccess) return;

        // Already persisted by the service — update UI without re-triggering writes.
        foreach (var ep in Episodes.Where(e => e.EpisodeNumber <= row.EpisodeNumber))
            ep.InitWatched(true);

        await LoadProgressAsync();

        if (result.Value!.AutoCompleteNeeded)
            AutoCompleteRequested?.Invoke(this, new TrackingAutoCompleteEventArgs(LibraryItemId, IsManga: false));
    }

    private void OnEpisodeNoteRequested(object? sender, EpisodeRowViewModel row)
    {
        var current = Item?.Notes
            .FirstOrDefault(n => n.EpisodeNumber == row.EpisodeNumber && n.ChapterNumber == null)?.Content
            ?? string.Empty;
        NoteEditRequested?.Invoke(this, new NoteEditEventArgs(row.EpisodeNumber, null, current));
    }

    // ── Chapter handlers ──────────────────────────────────────────────────────

    private async void OnChapterReadToggled(object? sender, ChapterRowViewModel row)
    {
        await LoadProgressAsync();

        if (WatchedCount == TotalCount && TotalCount > 0)
            AutoCompleteRequested?.Invoke(this, new TrackingAutoCompleteEventArgs(LibraryItemId, IsManga: true));
    }

    private async void OnChapterMarkUpToHere(object? sender, ChapterRowViewModel row)
    {
        var result = await _tracking.MarkChaptersUpToAsync(LibraryItemId, row.ChapterNumber);
        if (!result.IsSuccess) return;

        foreach (var ch in Chapters.Where(c => c.ChapterNumber <= row.ChapterNumber))
            ch.InitRead(true);

        await LoadProgressAsync();

        if (result.Value!.AutoCompleteNeeded)
            AutoCompleteRequested?.Invoke(this, new TrackingAutoCompleteEventArgs(LibraryItemId, IsManga: true));
    }

    private void OnChapterNoteRequested(object? sender, ChapterRowViewModel row)
    {
        var current = Item?.Notes
            .FirstOrDefault(n => n.ChapterNumber == row.ChapterNumber && n.EpisodeNumber == null)?.Content
            ?? string.Empty;
        NoteEditRequested?.Invoke(this, new NoteEditEventArgs(null, row.ChapterNumber, current));
    }

    // ── Note persistence (called back from the view after the dialog) ──────────

    public async Task SaveItemNoteAsync(int? episodeNumber, int? chapterNumber, string content)
    {
        var result = await _tracking.SaveNoteAsync(LibraryItemId, episodeNumber, chapterNumber, content);
        if (!result.IsSuccess) return;

        bool hasText = !string.IsNullOrWhiteSpace(content);
        if (episodeNumber is int en)
        {
            var row = Episodes.FirstOrDefault(e => e.EpisodeNumber == en);
            if (row is not null) row.HasNote = hasText;
        }
        else if (chapterNumber is int cn)
        {
            var row = Chapters.FirstOrDefault(c => c.ChapterNumber == cn);
            if (row is not null) row.HasNote = hasText;
        }

        // Keep the in-memory note cache in sync for subsequent edits.
        if (Item is not null)
        {
            var note = Item.Notes.FirstOrDefault(n =>
                n.EpisodeNumber == episodeNumber && n.ChapterNumber == chapterNumber);
            if (note is null)
                Item.Notes.Add(new Note
                {
                    LibraryItemId = LibraryItemId,
                    EpisodeNumber = episodeNumber,
                    ChapterNumber = chapterNumber,
                    Content = content
                });
            else
                note.Content = content;
        }
    }

    partial void OnTotalCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasProgress));
        OnPropertyChanged(nameof(HasEpisodes));
        OnPropertyChanged(nameof(HasChapters));
    }

    partial void OnIsMangaChanged(bool value)
    {
        OnPropertyChanged(nameof(HasEpisodes));
        OnPropertyChanged(nameof(HasChapters));
        OnPropertyChanged(nameof(ProgressSuffix));
    }
}

public record TrackingAutoCompleteEventArgs(int LibraryItemId, bool IsManga);

public record NoteEditEventArgs(int? EpisodeNumber, int? ChapterNumber, string CurrentText);
