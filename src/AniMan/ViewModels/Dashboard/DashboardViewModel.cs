using System.Collections.ObjectModel;
using System.Net.Http;
using AniMan.Core.Domain;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Interfaces;
using AniMan.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniMan.ViewModels.Dashboard;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly ITrackingService _tracking;
    private readonly ICatalogService _catalog;
    private readonly IHttpClientFactory _httpClientFactory;
    private CancellationTokenSource? _seasonCoverCts;

    [ObservableProperty] private int _totalAnime;
    [ObservableProperty] private int _totalManga;
    [ObservableProperty] private int _watchingCount;
    [ObservableProperty] private int _completedCount;
    [ObservableProperty] private bool _isLoadingLibrary;
    [ObservableProperty] private bool _isLoadingSeason;
    [ObservableProperty] private string? _seasonErrorMessage;
    [ObservableProperty] private ObservableCollection<LibraryItemSummary> _continueItems = [];
    [ObservableProperty] private ObservableCollection<LibraryItemSummary> _continueWatchingItems = [];
    [ObservableProperty] private ObservableCollection<LibraryItemSummary> _continueReadingItems = [];
    [ObservableProperty] private ObservableCollection<SeasonCardViewModel> _thisSeasonItems = [];

    public bool HasContinueWatchingItems => ContinueWatchingItems.Count > 0;
    public bool HasContinueReadingItems => ContinueReadingItems.Count > 0;
    public bool HasContinueItems => HasContinueWatchingItems || HasContinueReadingItems;
    public LibraryItemSummary? HeroItem => ContinueWatchingItems.FirstOrDefault() ?? ContinueReadingItems.FirstOrDefault();
    public string HeroBadgeText => HeroItem?.MediaType == MediaType.Manga
        ? LocalizationManager.Get("Dashboard_ContinueReading")
        : LocalizationManager.Get("Dashboard_ContinueWatching");
    public bool HasSeasonItems => ThisSeasonItems.Count > 0;
    public bool HasSeasonError => !string.IsNullOrEmpty(SeasonErrorMessage);

    public event EventHandler<int>? NavigateToLibraryItemRequested;
    public event EventHandler? NavigateToDiscoverRequested;

    public DashboardViewModel(
        ITrackingService tracking,
        ICatalogService catalog,
        IHttpClientFactory httpClientFactory)
    {
        _tracking = tracking;
        _catalog = catalog;
        _httpClientFactory = httpClientFactory;
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
    private async Task RetrySeasonAsync() => await LoadSeasonAsync(force: true);

    private async Task LoadSeasonAsync(bool force = false)
    {
        if (!force && ThisSeasonItems.Count > 0) return;

        IsLoadingSeason = true;
        SeasonErrorMessage = null;
        OnPropertyChanged(nameof(HasSeasonError));
        try
        {
            var result = await _catalog.GetCurrentSeasonAsync();
            if (result.IsSuccess)
            {
                var cards = result.Value!
                    .Take(10)
                    .Select(a => new SeasonCardViewModel
                    {
                        Id = a.Id,
                        Title = a.Title,
                        CoverUrl = a.CoverMediumUrl ?? a.CoverLargeUrl
                    })
                    .ToList();

                ThisSeasonItems = new ObservableCollection<SeasonCardViewModel>(cards);
                OnPropertyChanged(nameof(HasSeasonItems));
                _ = LoadSeasonCoversAsync(cards);
            }
            else
            {
                SeasonErrorMessage = result.Error;
                OnPropertyChanged(nameof(HasSeasonError));
            }
        }
        finally { IsLoadingSeason = false; }
    }

    private async Task LoadSeasonCoversAsync(IReadOnlyList<SeasonCardViewModel> cards)
    {
        var previous = _seasonCoverCts;
        previous?.Cancel();
        previous?.Dispose();
        var cts = _seasonCoverCts = new CancellationTokenSource();
        var ct = cts.Token;

        using var gate = new SemaphoreSlim(4);
        var tasks = cards.Select(async card =>
        {
            await gate.WaitAsync(ct);
            try { await card.LoadCoverAsync(_httpClientFactory, ct); }
            catch (OperationCanceledException) { }
            finally { gate.Release(); }
        });

        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }
    }

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
            WatchingCount = animeItems.Count(i => i.StatusId == TrackingStatusIds.Watching)
                + mangaItems.Count(i => i.StatusId == TrackingStatusIds.Reading);
            CompletedCount = animeItems.Count(i => i.StatusId == TrackingStatusIds.Completed)
                + mangaItems.Count(i => i.StatusId == TrackingStatusIds.Completed);

            var watchingAnime = animeItems
                .Where(i => i.StatusId == TrackingStatusIds.Watching)
                .Select(i => new LibraryItemSummary
                {
                    Id = i.Id,
                    Title = i.Snapshot?.Title ?? LocalizationManager.Get("Library_NoTitle"),
                    CoverLocalPath = i.Snapshot?.CoverLocalPath,
                    MediaType = i.MediaType
                })
                .Take(10)
                .ToList();

            var readingManga = mangaItems
                .Where(i => i.StatusId == TrackingStatusIds.Reading)
                .Select(i => new LibraryItemSummary
                {
                    Id = i.Id,
                    Title = i.Snapshot?.Title ?? LocalizationManager.Get("Library_NoTitle"),
                    CoverLocalPath = i.Snapshot?.CoverLocalPath,
                    MediaType = i.MediaType
                })
                .Take(10)
                .ToList();

            foreach (var item in watchingAnime) item.LoadCoverImage();
            foreach (var item in readingManga) item.LoadCoverImage();

            ContinueWatchingItems = new ObservableCollection<LibraryItemSummary>(watchingAnime);
            ContinueReadingItems = new ObservableCollection<LibraryItemSummary>(readingManga);
            ContinueItems = new ObservableCollection<LibraryItemSummary>(watchingAnime.Concat(readingManga).Take(10));

            OnPropertyChanged(nameof(HasContinueWatchingItems));
            OnPropertyChanged(nameof(HasContinueReadingItems));
            OnPropertyChanged(nameof(HasContinueItems));
            OnPropertyChanged(nameof(HeroItem));
            OnPropertyChanged(nameof(HeroBadgeText));
        }
        finally { IsLoadingLibrary = false; }
    }

    partial void OnSeasonErrorMessageChanged(string? value) =>
        OnPropertyChanged(nameof(HasSeasonError));

    public void Dispose()
    {
        _seasonCoverCts?.Cancel();
        _seasonCoverCts?.Dispose();
        _seasonCoverCts = null;
    }
}

public class LibraryItemSummary
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? CoverLocalPath { get; init; }
    public MediaType MediaType { get; init; }
    public System.Windows.Media.Imaging.BitmapImage? CoverImage { get; private set; }

    public void LoadCoverImage() =>
        CoverImage = Imaging.CoverImageLoader.FromFile(CoverLocalPath);
}
