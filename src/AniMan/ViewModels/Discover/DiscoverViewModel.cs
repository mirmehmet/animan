using System.Collections.ObjectModel;
using System.Net.Http;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniMan.ViewModels.Discover;

public enum DiscoverTab { Anime, Manga }

public partial class DiscoverViewModel : ObservableObject, IDisposable
{
    private readonly ICatalogService _catalog;
    private readonly ISnapshotService _snapshot;
    private readonly ITrackingService _tracking;
    private readonly IHttpClientFactory _httpClientFactory;

    private HashSet<(int MalId, MediaType MediaType)> _librarySet = [];
    private CancellationTokenSource? _coverCts;

    private int _currentPage = 1;
    private bool _hasMorePages = true;
    private bool _isSearchMode;

    [ObservableProperty] private ObservableCollection<DiscoverCardViewModel> _cards = [];
    [ObservableProperty] private DiscoverTab _activeTab = DiscoverTab.Anime;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoadingMore;
    [ObservableProperty] private string? _errorMessage;

    /// <summary>Scroll offset saved when leaving for a detail page; the page instance
    /// is transient, so the position must survive here on the singleton VM.</summary>
    public double SavedScrollOffset { get; set; }

    /// <summary>True while a DiscoverPage instance is loaded — lets MainWindow tell a
    /// re-click on the Discover nav item apart from navigation from another page.</summary>
    public bool IsPageActive { get; set; }

    /// <summary>Full reset to the initial Discover view: Anime tab, top list, empty search.</summary>
    public async Task ResetToInitialAsync()
    {
        ActiveTab = DiscoverTab.Anime;
        SearchQuery = string.Empty;
        SavedScrollOffset = 0;
        await LoadAsync();
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    public event EventHandler<AddToLibraryEventArgs>? AddToLibraryRequested;

    public DiscoverViewModel(ICatalogService catalog, ISnapshotService snapshot,
        ITrackingService tracking, IHttpClientFactory httpClientFactory)
    {
        _catalog = catalog;
        _snapshot = snapshot;
        _tracking = tracking;
        _httpClientFactory = httpClientFactory;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        _isSearchMode = false;
        await LoadLibrarySetAsync();
        await LoadTabFirstPageAsync();
    }

    [RelayCommand]
    private async Task SwitchTabAsync(DiscoverTab tab)
    {
        if (ActiveTab == tab && !_isSearchMode) return;
        ActiveTab = tab;
        _isSearchMode = false;
        SearchQuery = string.Empty;
        await LoadTabFirstPageAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;
        _isSearchMode = true;
        _hasMorePages = false;
        ErrorMessage = null;
        IsLoading = true;
        try
        {
            if (ActiveTab == DiscoverTab.Manga)
            {
                var result = await _catalog.SearchMangaAsync(SearchQuery);
                if (result.IsSuccess)
                    SetCards(result.Value!.Select(m => new DiscoverCardViewModel
                    {
                        MalId = m.Id, Title = m.Title,
                        CoverUrl = m.CoverMediumUrl ?? m.CoverLargeUrl, Score = m.Score,
                        Type = m.Type, MediaType = MediaType.Manga,
                        IsInLibrary = _librarySet.Contains((m.Id, MediaType.Manga))
                    }));
                else
                    ErrorMessage = result.Error;
            }
            else
            {
                var result = await _catalog.SearchAnimeAsync(SearchQuery);
                if (result.IsSuccess)
                    SetCards(result.Value!.Select(a => new DiscoverCardViewModel
                    {
                        MalId = a.Id, Title = a.Title,
                        CoverUrl = a.CoverMediumUrl ?? a.CoverLargeUrl, Score = a.Score,
                        Type = a.Type, MediaType = MediaType.Anime,
                        IsInLibrary = _librarySet.Contains((a.Id, MediaType.Anime))
                    }));
                else
                    ErrorMessage = result.Error;
            }
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!_hasMorePages || IsLoading || IsLoadingMore || _isSearchMode) return;
        IsLoadingMore = true;
        try
        {
            _currentPage++;
            if (ActiveTab == DiscoverTab.Manga)
            {
                var result = await _catalog.GetTopMangaAsync(_currentPage);
                if (result.IsSuccess && result.Value!.Count > 0)
                {
                    var newCards = result.Value!.Select(m => new DiscoverCardViewModel
                    {
                        MalId = m.Id, Title = m.Title,
                        CoverUrl = m.CoverMediumUrl ?? m.CoverLargeUrl, Score = m.Score,
                        Type = m.Type, MediaType = MediaType.Manga,
                        IsInLibrary = _librarySet.Contains((m.Id, MediaType.Manga))
                    }).ToList();

                    foreach (var card in newCards) Cards.Add(card);
                    _hasMorePages = result.Value!.Count >= 25;
                    _ = LoadCoversAsync(newCards);
                }
                else
                {
                    _hasMorePages = false;
                }
            }
            else
            {
                var result = await _catalog.GetTopAnimeAsync(_currentPage);
                if (result.IsSuccess && result.Value!.Count > 0)
                {
                    var newCards = result.Value!.Select(a => new DiscoverCardViewModel
                    {
                        MalId = a.Id, Title = a.Title,
                        CoverUrl = a.CoverMediumUrl ?? a.CoverLargeUrl, Score = a.Score,
                        Type = a.Type, MediaType = MediaType.Anime,
                        IsInLibrary = _librarySet.Contains((a.Id, MediaType.Anime))
                    }).ToList();

                    foreach (var card in newCards) Cards.Add(card);
                    _hasMorePages = result.Value!.Count >= 25;
                    _ = LoadCoversAsync(newCards);
                }
                else
                {
                    _hasMorePages = false;
                }
            }
        }
        finally { IsLoadingMore = false; }
    }

    /// <summary>Re-syncs the ✓ markers on existing cards without reloading the list —
    /// used when returning to a preserved Discover state (items may have been added
    /// to the library from the detail page in the meantime).</summary>
    public async Task RefreshLibraryFlagsAsync()
    {
        await LoadLibrarySetAsync();
        foreach (var card in Cards)
            card.IsInLibrary = _librarySet.Contains((card.MalId, card.MediaType));
    }

    [RelayCommand]
    private void RequestAdd(DiscoverCardViewModel card) =>
        AddToLibraryRequested?.Invoke(this, new AddToLibraryEventArgs(card));

    public async Task<bool> AddToLibraryAsync(int malId, MediaType mediaType, int statusId)
    {
        var result = await _snapshot.SnapshotAsync(malId, mediaType, statusId);
        if (!result.IsSuccess) return false;

        _librarySet.Add((malId, mediaType));
        var card = Cards.FirstOrDefault(c => c.MalId == malId && c.MediaType == mediaType);
        if (card is not null) card.IsInLibrary = true;

        return true;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task LoadTabFirstPageAsync()
    {
        _currentPage = 1;
        _hasMorePages = true;
        ErrorMessage = null;
        IsLoading = true;
        Cards = [];
        try
        {
            if (ActiveTab == DiscoverTab.Manga)
            {
                var result = await _catalog.GetTopMangaAsync(1);
                if (result.IsSuccess)
                {
                    SetCards(result.Value!.Select(m => new DiscoverCardViewModel
                    {
                        MalId = m.Id, Title = m.Title,
                        CoverUrl = m.CoverMediumUrl ?? m.CoverLargeUrl, Score = m.Score,
                        Type = m.Type, MediaType = MediaType.Manga,
                        IsInLibrary = _librarySet.Contains((m.Id, MediaType.Manga))
                    }));
                    _hasMorePages = result.Value!.Count >= 25;
                }
                else
                {
                    ErrorMessage = result.Error;
                }
            }
            else
            {
                var result = await _catalog.GetTopAnimeAsync(1);
                if (result.IsSuccess)
                {
                    SetCards(result.Value!.Select(a => new DiscoverCardViewModel
                    {
                        MalId = a.Id, Title = a.Title,
                        CoverUrl = a.CoverMediumUrl ?? a.CoverLargeUrl, Score = a.Score,
                        Type = a.Type, MediaType = MediaType.Anime,
                        IsInLibrary = _librarySet.Contains((a.Id, MediaType.Anime))
                    }));
                    _hasMorePages = result.Value!.Count >= 25;
                }
                else
                {
                    ErrorMessage = result.Error;
                }
            }
        }
        finally { IsLoading = false; }
    }

    private async Task LoadLibrarySetAsync()
    {
        var anime = await _tracking.GetLibraryAsync("anime");
        var manga = await _tracking.GetLibraryAsync("manga");

        _librarySet = [];
        if (anime.IsSuccess)
            foreach (var item in anime.Value!)
                _librarySet.Add((item.MalId, MediaType.Anime));
        if (manga.IsSuccess)
            foreach (var item in manga.Value!)
                _librarySet.Add((item.MalId, MediaType.Manga));
    }

    private void SetCards(IEnumerable<DiscoverCardViewModel> source)
    {
        var list = source.ToList();
        Cards = new ObservableCollection<DiscoverCardViewModel>(list);
        _ = LoadCoversAsync(list);
    }

    private async Task LoadCoversAsync(IReadOnlyList<DiscoverCardViewModel> cards)
    {
        var previous = _coverCts;
        previous?.Cancel();
        previous?.Dispose();
        var cts = _coverCts = new CancellationTokenSource();
        var ct = cts.Token;

        using var gate = new SemaphoreSlim(6);
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

    // Singleton VM: disposed with the service provider on shutdown.
    public void Dispose()
    {
        _coverCts?.Cancel();
        _coverCts?.Dispose();
        _coverCts = null;
    }
}

public record AddToLibraryEventArgs(DiscoverCardViewModel Card);
