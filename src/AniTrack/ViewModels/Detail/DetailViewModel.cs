using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using AniTrack.Core.Domain.Models;
using AniTrack.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace AniTrack.ViewModels.Detail;

public partial class DetailViewModel : ObservableObject
{
    private readonly ITrackingService _tracking;
    private readonly ISnapshotService _snapshot;
    private readonly IServiceProvider _services;

    public int LibraryItemId { get; private set; }

    [ObservableProperty]
    private LibraryItem? _item;

    [ObservableProperty]
    private MediaSnapshot? _mediaSnapshot;

    [ObservableProperty]
    private BitmapImage? _coverImage;

    [ObservableProperty]
    private ObservableCollection<EpisodeRowViewModel> _episodes = [];

    [ObservableProperty]
    private int _watchedCount;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private string _seriesNote = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public bool HasEpisodes => TotalCount > 0;

    public event EventHandler<TrackingAutoCompleteEventArgs>? AutoCompleteRequested;

    public DetailViewModel(ITrackingService tracking, ISnapshotService snapshot, IServiceProvider services)
    {
        _tracking = tracking;
        _snapshot = snapshot;
        _services = services;
    }

    public async Task InitializeAsync(int libraryItemId)
    {
        LibraryItemId = libraryItemId;
        IsLoading = true;
        try
        {
            var result = await _tracking.GetItemAsync(libraryItemId);
            if (!result.IsSuccess) return;

            Item = result.Value!;
            MediaSnapshot = Item.Snapshot;

            LoadCoverImage();
            LoadSeriesNote();
            await LoadProgressAsync();
            await LoadEpisodesAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        var result = await _tracking.ToggleFavoriteAsync(LibraryItemId);
        if (result.IsSuccess && Item is not null)
        {
            Item.IsFavorite = !Item.IsFavorite;
            OnPropertyChanged(nameof(Item));
        }
    }

    [RelayCommand]
    private async Task UpdateScoreAsync(int? score)
    {
        await _tracking.UpdateScoreAsync(LibraryItemId, score);
    }

    [RelayCommand]
    private async Task UpdateStatusAsync(int statusId)
    {
        await _tracking.UpdateStatusAsync(LibraryItemId, statusId);
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
        catch { /* ignore */ }
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
        OnPropertyChanged(nameof(HasEpisodes));
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

        var rows = new List<EpisodeRowViewModel>();
        int total = Item.Snapshot.TotalEpisodes ?? 0;

        for (int i = 1; i <= total; i++)
        {
            var row = new EpisodeRowViewModel(_tracking)
            {
                LibraryItemId = LibraryItemId,
                EpisodeNumber = i,
                IsWatched = episodeProgress.TryGetValue(i, out var ep) && ep.IsWatched,
                HasNote = episodeNotes.Contains(i)
            };

            row.WatchedToggled += OnEpisodeWatchedToggled;
            row.MarkUpToHereRequested += OnMarkUpToHereRequested;
            rows.Add(row);
        }

        Episodes = new ObservableCollection<EpisodeRowViewModel>(rows);
    }

    private async void OnEpisodeWatchedToggled(object? sender, EpisodeRowViewModel row)
    {
        await LoadProgressAsync();

        if (WatchedCount == TotalCount && TotalCount > 0)
            AutoCompleteRequested?.Invoke(this, new TrackingAutoCompleteEventArgs(LibraryItemId));
    }

    private async void OnMarkUpToHereRequested(object? sender, EpisodeRowViewModel row)
    {
        var result = await _tracking.MarkUpToHereAsync(LibraryItemId, row.EpisodeNumber);
        if (!result.IsSuccess) return;

        foreach (var ep in Episodes.Where(e => e.EpisodeNumber <= row.EpisodeNumber))
            ep.IsWatched = true;

        await LoadProgressAsync();

        if (result.Value!.AutoCompleteNeeded)
            AutoCompleteRequested?.Invoke(this, new TrackingAutoCompleteEventArgs(LibraryItemId));
    }
}

public record TrackingAutoCompleteEventArgs(int LibraryItemId);
