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
    private readonly SemaphoreSlim _coverGate = new(6, 6);

    private HashSet<(int MalId, MediaType MediaType)> _librarySet = [];
    private CancellationTokenSource? _queryCts;
    private CancellationTokenSource? _coverCts;
    private int _queryVersion;
    private bool _disposed;

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
    /// is singleton, and preserving offset here keeps navigation state unified.</summary>
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

    /// <summary>Empty state shown only for a completed search with zero hits —
    /// browse tabs that come back empty are an error case, not "no results".</summary>
    public bool HasNoResults => _isSearchMode && !IsLoading && !HasError && Cards.Count == 0;

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasNoResults));
    }

    partial void OnCardsChanged(ObservableCollection<DiscoverCardViewModel> value) =>
        OnPropertyChanged(nameof(HasNoResults));

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(HasNoResults));

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
        var ct = BeginNewQuery(out var version);
        IsLoadingMore = false;
        ErrorMessage = null;
        IsLoading = true;
        try
        {
            await LoadLibrarySetAsync(ct);
            if (version != _queryVersion) return;

            await LoadTabFirstPageCoreAsync(version, ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (version == _queryVersion)
                IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SwitchTabAsync(DiscoverTab tab)
    {
        if (ActiveTab == tab && !_isSearchMode && !HasError) return;
        ActiveTab = tab;
        _isSearchMode = false;
        SearchQuery = string.Empty;

        var ct = BeginNewQuery(out var version);
        IsLoadingMore = false;
        ErrorMessage = null;
        IsLoading = true;
        Cards = [];
        try
        {
            await LoadTabFirstPageCoreAsync(version, ct);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (version == _queryVersion)
                IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;
        var query = SearchQuery.Trim();

        _isSearchMode = true;
        _hasMorePages = false;
        var ct = BeginNewQuery(out var version);
        IsLoadingMore = false;
        ErrorMessage = null;
        IsLoading = true;
        try
        {
            if (ActiveTab == DiscoverTab.Manga)
            {
                var result = await _catalog.SearchMangaAsync(query, ct);
                if (version != _queryVersion) return;

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
                var result = await _catalog.SearchAnimeAsync(query, ct);
                if (version != _queryVersion) return;

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
        catch (OperationCanceledException) { }
        finally
        {
            if (version == _queryVersion)
                IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!_hasMorePages || IsLoading || IsLoadingMore || _isSearchMode) return;

        var version = _queryVersion;
        var ct = (_queryCts ??= new CancellationTokenSource()).Token;
        var nextPage = _currentPage + 1;

        IsLoadingMore = true;
        try
        {
            if (ActiveTab == DiscoverTab.Manga)
            {
                var result = await _catalog.GetTopMangaAsync(nextPage, ct);
                if (version != _queryVersion) return;

                if (result.IsSuccess && result.Value!.Count > 0)
                {
                    _currentPage = nextPage;
                    var newCards = result.Value!.Select(m => new DiscoverCardViewModel
                    {
                        MalId = m.Id, Title = m.Title,
                        CoverUrl = m.CoverMediumUrl ?? m.CoverLargeUrl, Score = m.Score,
                        Type = m.Type, MediaType = MediaType.Manga,
                        IsInLibrary = _librarySet.Contains((m.Id, MediaType.Manga))
                    }).ToList();

                    foreach (var card in newCards) Cards.Add(card);
                    _hasMorePages = result.Value!.Count >= 25;
                    _ = LoadCoversAsync(newCards, resetExisting: false);
                }
                else if (result.IsSuccess)
                {
                    _currentPage = nextPage;
                    _hasMorePages = false;
                }
            }
            else
            {
                var result = await _catalog.GetTopAnimeAsync(nextPage, ct);
                if (version != _queryVersion) return;

                if (result.IsSuccess && result.Value!.Count > 0)
                {
                    _currentPage = nextPage;
                    var newCards = result.Value!.Select(a => new DiscoverCardViewModel
                    {
                        MalId = a.Id, Title = a.Title,
                        CoverUrl = a.CoverMediumUrl ?? a.CoverLargeUrl, Score = a.Score,
                        Type = a.Type, MediaType = MediaType.Anime,
                        IsInLibrary = _librarySet.Contains((a.Id, MediaType.Anime))
                    }).ToList();

                    foreach (var card in newCards) Cards.Add(card);
                    _hasMorePages = result.Value!.Count >= 25;
                    _ = LoadCoversAsync(newCards, resetExisting: false);
                }
                else if (result.IsSuccess)
                {
                    _currentPage = nextPage;
                    _hasMorePages = false;
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (version == _queryVersion)
                IsLoadingMore = false;
        }
    }

    /// <summary>Re-syncs the ✓ markers on existing cards without reloading the list —
    /// used when returning to a preserved Discover state (items may have been added
    /// to the library from the detail page in the meantime).</summary>
    public async Task RefreshLibraryFlagsAsync()
    {
        await LoadLibrarySetAsync(CancellationToken.None);
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

    private CancellationToken BeginNewQuery(out int version)
    {
        var previous = _queryCts;
        previous?.Cancel();
        previous?.Dispose();
        var cts = _queryCts = new CancellationTokenSource();
        version = ++_queryVersion;
        return cts.Token;
    }

    private async Task LoadTabFirstPageCoreAsync(int version, CancellationToken ct)
    {
        _currentPage = 1;
        _hasMorePages = true;
        Cards = [];

        if (ActiveTab == DiscoverTab.Manga)
        {
            var result = await _catalog.GetTopMangaAsync(1, ct);
            if (version != _queryVersion) return;

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
            var result = await _catalog.GetTopAnimeAsync(1, ct);
            if (version != _queryVersion) return;

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

    private async Task LoadLibrarySetAsync(CancellationToken ct)
    {
        var anime = await _tracking.GetLibraryAsync("anime", ct: ct);
        var manga = await _tracking.GetLibraryAsync("manga", ct: ct);

        var set = new HashSet<(int MalId, MediaType MediaType)>();
        if (anime.IsSuccess)
            foreach (var item in anime.Value!)
                set.Add((item.MalId, MediaType.Anime));
        if (manga.IsSuccess)
            foreach (var item in manga.Value!)
                set.Add((item.MalId, MediaType.Manga));

        _librarySet = set;
    }

    private void SetCards(IEnumerable<DiscoverCardViewModel> source)
    {
        var list = source.ToList();
        Cards = new ObservableCollection<DiscoverCardViewModel>(list);
        _ = LoadCoversAsync(list, resetExisting: true);
    }

    private async Task LoadCoversAsync(IReadOnlyList<DiscoverCardViewModel> cards, bool resetExisting)
    {
        if (_disposed || cards.Count == 0) return;

        CancellationToken ct;
        if (resetExisting)
        {
            var previous = _coverCts;
            previous?.Cancel();
            previous?.Dispose();
            var cts = _coverCts = new CancellationTokenSource();
            ct = cts.Token;
        }
        else
        {
            var cts = _coverCts ??= new CancellationTokenSource();
            ct = cts.Token;
        }

        var tasks = cards.Select(async card =>
        {
            try
            {
                await _coverGate.WaitAsync(ct);
                try { await card.LoadCoverAsync(_httpClientFactory, ct); }
                finally { _coverGate.Release(); }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        });

        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }
    }

    // Singleton VM: disposed with the service provider on shutdown.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _queryCts?.Cancel();
        _queryCts?.Dispose();
        _queryCts = null;

        _coverCts?.Cancel();
        _coverCts?.Dispose();
        _coverCts = null;

        _coverGate.Dispose();
    }
}

public record AddToLibraryEventArgs(DiscoverCardViewModel Card);
