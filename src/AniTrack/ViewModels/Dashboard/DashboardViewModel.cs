using System.Collections.ObjectModel;
using AniTrack.Core.Domain.Enums;
using AniTrack.Core.Domain.Models;
using AniTrack.Core.Interfaces;
using AniTrack.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniTrack.ViewModels.Dashboard;

public partial class DashboardViewModel : ObservableObject
{
    private readonly ITrackingService _tracking;
    private readonly ICatalogService _catalog;

    [ObservableProperty] private int _totalAnime;
    [ObservableProperty] private int _totalManga;
    [ObservableProperty] private int _watchingCount;
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private bool _isLoadingLibrary;
    [ObservableProperty] private bool _isLoadingSeason;
    [ObservableProperty] private string? _seasonErrorMessage;
    [ObservableProperty] private ObservableCollection<LibraryItemSummary> _continueItems = [];
    [ObservableProperty] private ObservableCollection<CachedAnime> _thisSeasonItems = [];

    public bool HasContinueItems => ContinueItems.Count > 0;
    public bool HasSeasonItems => ThisSeasonItems.Count > 0;
    public bool HasSeasonError => !string.IsNullOrEmpty(SeasonErrorMessage);

    public event EventHandler<int>? NavigateToLibraryItemRequested;
    public event EventHandler? NavigateToDiscoverRequested;

    public DashboardViewModel(ITrackingService tracking, ICatalogService catalog)
    {
        _tracking = tracking;
        _catalog = catalog;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await Task.WhenAll(LoadLibraryAsync(), LoadSeasonAsync());
    }

    [RelayCommand]
    private void OpenLibraryItem(int libraryItemId) =>
        NavigateToLibraryItemRequested?.Invoke(this, libraryItemId);

    [RelayCommand]
    private void GoToDiscover() =>
        NavigateToDiscoverRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task RetrySeasonAsync() => await LoadSeasonAsync();

    private async Task LoadLibraryAsync()
    {
        IsLoadingLibrary = true;
        try
        {
            var animeResult = await _tracking.GetLibraryAsync("anime");
            var mangaResult = await _tracking.GetLibraryAsync("manga");

            var animeItems = animeResult.IsSuccess ? animeResult.Value! : [];
            var mangaItems = mangaResult.IsSuccess ? mangaResult.Value! : [];

            TotalAnime = animeItems.Count;
            TotalManga = mangaItems.Count;
            WatchingCount = animeItems.Count(i => i.StatusId == 1) + mangaItems.Count(i => i.StatusId == 2);
            CompletedCount = animeItems.Count(i => i.StatusId == 3) + mangaItems.Count(i => i.StatusId == 3);

            var inProgress = animeItems
                .Where(i => i.StatusId == 1)
                .Concat(mangaItems.Where(i => i.StatusId == 2))
                .Select(i => new LibraryItemSummary
                {
                    Id = i.Id,
                    Title = i.Snapshot?.Title ?? LocalizationManager.Get("Library_NoTitle"),
                    CoverLocalPath = i.Snapshot?.CoverLocalPath,
                    MediaType = i.MediaType
                })
                .Take(10)
                .ToList();

            foreach (var item in inProgress) item.LoadCoverImage();
            ContinueItems = new ObservableCollection<LibraryItemSummary>(inProgress);
            OnPropertyChanged(nameof(HasContinueItems));
        }
        finally { IsLoadingLibrary = false; }
    }

    private async Task LoadSeasonAsync()
    {
        IsLoadingSeason = true;
        SeasonErrorMessage = null;
        OnPropertyChanged(nameof(HasSeasonError));
        try
        {
            var result = await _catalog.GetCurrentSeasonAsync();
            if (result.IsSuccess)
            {
                ThisSeasonItems = new ObservableCollection<CachedAnime>(
                    result.Value!.Take(10));
                OnPropertyChanged(nameof(HasSeasonItems));
            }
            else
            {
                SeasonErrorMessage = result.Error;
                OnPropertyChanged(nameof(HasSeasonError));
            }
        }
        finally { IsLoadingSeason = false; }
    }

    partial void OnSeasonErrorMessageChanged(string? value) =>
        OnPropertyChanged(nameof(HasSeasonError));
}

public class LibraryItemSummary
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? CoverLocalPath { get; init; }
    public MediaType MediaType { get; init; }
    public System.Windows.Media.Imaging.BitmapImage? CoverImage { get; private set; }

    public void LoadCoverImage()
    {
        if (CoverLocalPath is null || !System.IO.File.Exists(CoverLocalPath)) return;
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(CoverLocalPath, UriKind.Absolute);
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            CoverImage = bmp;
        }
        catch { /* non-critical */ }
    }
}
