using System.Collections.ObjectModel;
using System.Net.Http;
using AniMan.Core.Common;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AniMan.Localization;

namespace AniMan.ViewModels.Discover;

public enum DiscoverTab { Anime, Manga }

/// <summary>
/// Backs the "Add" page (internally still Discover): search the catalogue per media type,
/// or page through the popular list, and add titles to the library.
/// <para>
/// Each tab owns a <see cref="DiscoverTabState"/> — its own query, results, page, and
/// in-flight request — so switching tabs never discards the other tab's work. The popular
/// list is paged (one page in memory at a time) rather than infinitely scrolled, which
/// kept every card and cover alive and grew without bound.
/// </para>
/// </summary>
public partial class DiscoverViewModel : ObservableObject, IDisposable
{
    /// <summary>Items per page returned by the source; a full page implies another may follow.</summary>
    private const int PageSize = 25;

    private readonly ICatalogService _catalog;
    private readonly ISnapshotService _snapshot;
    private readonly ITrackingService _tracking;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SemaphoreSlim _coverGate = new(6, 6);
    private readonly DiscoverTabState _anime = new(MediaType.Anime);
    private readonly DiscoverTabState _manga = new(MediaType.Manga);

    private HashSet<(int MalId, MediaType MediaType)> _librarySet = [];
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current))]
    private DiscoverTab _activeTab = DiscoverTab.Anime;

    /// <summary>State of the visible tab; every page binding goes through this.</summary>
    public DiscoverTabState Current => ActiveTab == DiscoverTab.Manga ? _manga : _anime;

    /// <summary>True while a DiscoverPage instance is loaded — lets MainWindow tell a
    /// re-click on the nav item apart from navigation from another page.</summary>
    public bool IsPageActive { get; set; }

    public event EventHandler<AddToLibraryEventArgs>? AddToLibraryRequested;

    /// <summary>Raised when the visible tab's cards were replaced (new page or search),
    /// so the view can scroll back to the top.</summary>
    public event EventHandler? ScrollToTopRequested;

    public DiscoverViewModel(ICatalogService catalog, ISnapshotService snapshot,
        ITrackingService tracking, IHttpClientFactory httpClientFactory)
    {
        _catalog = catalog;
        _snapshot = snapshot;
        _tracking = tracking;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Full reset to the initial view: Anime tab, first popular page, both searches cleared.</summary>
    public async Task ResetToInitialAsync()
    {
        _anime.Reset();
        _manga.Reset();
        ActiveTab = DiscoverTab.Anime;
        await LoadAsync();
    }

    /// <summary>Loads (or retries) the visible tab: its search if one is active, otherwise its current page.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        await LoadLibrarySetAsync(CancellationToken.None);

        var state = Current;
        if (state.IsSearchMode && !string.IsNullOrWhiteSpace(state.SearchQuery))
            await SearchCoreAsync(state);
        else
            await LoadPopularPageAsync(state, state.CurrentPage);
    }

    [RelayCommand]
    private async Task SwitchTabAsync(DiscoverTab tab)
    {
        if (ActiveTab == tab) return;
        ActiveTab = tab;

        var state = Current;
        if (state.NeedsLoad)
            await LoadPopularPageAsync(state, state.CurrentPage);
    }

    /// <summary>Searches the visible tab. An empty query leaves search mode and returns to the popular list.</summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        var state = Current;
        if (string.IsNullOrWhiteSpace(state.SearchQuery))
        {
            if (state.IsSearchMode)
                await LoadPopularPageAsync(state, state.CurrentPage);
            return;
        }

        await SearchCoreAsync(state);
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        var state = Current;
        if (state.CanGoNext)
            await LoadPopularPageAsync(state, state.CurrentPage + 1);
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        var state = Current;
        if (state.CanGoPrevious)
            await LoadPopularPageAsync(state, state.CurrentPage - 1);
    }

    /// <summary>Re-syncs the ✓ markers on existing cards without reloading the lists —
    /// used when returning to the page (items may have been added from the detail page).</summary>
    public async Task RefreshLibraryFlagsAsync()
    {
        await LoadLibrarySetAsync(CancellationToken.None);
        foreach (var card in _anime.Cards.Concat(_manga.Cards))
            card.IsInLibrary = _librarySet.Contains((card.MalId, card.MediaType));
    }

    [RelayCommand]
    private void RequestAdd(DiscoverCardViewModel card) =>
        AddToLibraryRequested?.Invoke(this, new AddToLibraryEventArgs(card));

    public async Task<Result> AddToLibraryAsync(int malId, MediaType mediaType, int statusId)
    {
        var result = await _snapshot.SnapshotAsync(malId, mediaType, statusId);
        if (!result.IsSuccess) return Result.Failure(result.Error!, result.Kind);

        _librarySet.Add((malId, mediaType));
        foreach (var card in _anime.Cards.Concat(_manga.Cards))
        {
            if (card.MalId == malId && card.MediaType == mediaType)
                card.IsInLibrary = true;
        }

        return Result.Success();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task LoadPopularPageAsync(DiscoverTabState state, int page)
    {
        var ct = state.BeginQuery(out var version);
        state.IsSearchMode = false;
        state.CurrentPage = page; // set up front so a retry after a failure re-requests this page
        state.ErrorMessage = null;
        state.IsLoading = true;
        try
        {
            if (state.MediaType == MediaType.Manga)
            {
                var result = await _catalog.GetTopMangaAsync(page, ct);
                if (version != state.QueryVersion) return;

                if (result.IsSuccess)
                {
                    state.HasNextPage = result.Value!.Count >= PageSize;
                    SetCards(state, result.Value!.Select(ToCard));
                }
                else
                {
                    state.ErrorMessage = ErrorText.For(result.Kind);
                }
            }
            else
            {
                var result = await _catalog.GetTopAnimeAsync(page, ct);
                if (version != state.QueryVersion) return;

                if (result.IsSuccess)
                {
                    state.HasNextPage = result.Value!.Count >= PageSize;
                    SetCards(state, result.Value!.Select(ToCard));
                }
                else
                {
                    state.ErrorMessage = ErrorText.For(result.Kind);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (version == state.QueryVersion)
                state.IsLoading = false;
        }
    }

    private async Task SearchCoreAsync(DiscoverTabState state)
    {
        var query = state.SearchQuery.Trim();
        var ct = state.BeginQuery(out var version);
        state.IsSearchMode = true;
        state.ErrorMessage = null;
        state.IsLoading = true;
        try
        {
            if (state.MediaType == MediaType.Manga)
            {
                var result = await _catalog.SearchMangaAsync(query, ct);
                if (version != state.QueryVersion) return;

                if (result.IsSuccess)
                    SetCards(state, result.Value!.Select(ToCard));
                else
                    state.ErrorMessage = ErrorText.For(result.Kind);
            }
            else
            {
                var result = await _catalog.SearchAnimeAsync(query, ct);
                if (version != state.QueryVersion) return;

                if (result.IsSuccess)
                    SetCards(state, result.Value!.Select(ToCard));
                else
                    state.ErrorMessage = ErrorText.For(result.Kind);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (version == state.QueryVersion)
                state.IsLoading = false;
        }
    }

    private DiscoverCardViewModel ToCard(CachedAnime a) => new()
    {
        MalId = a.Id, Title = a.Title,
        CoverUrl = a.CoverMediumUrl ?? a.CoverLargeUrl, Score = a.Score,
        Type = a.Type, MediaType = MediaType.Anime,
        IsInLibrary = _librarySet.Contains((a.Id, MediaType.Anime))
    };

    private DiscoverCardViewModel ToCard(CachedManga m) => new()
    {
        MalId = m.Id, Title = m.Title,
        CoverUrl = m.CoverMediumUrl ?? m.CoverLargeUrl, Score = m.Score,
        Type = m.Type, MediaType = MediaType.Manga,
        IsInLibrary = _librarySet.Contains((m.Id, MediaType.Manga))
    };

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

    /// <summary>Replaces a tab's cards (the previous page becomes garbage) and loads their covers.</summary>
    private void SetCards(DiscoverTabState state, IEnumerable<DiscoverCardViewModel> source)
    {
        var list = source.ToList();
        state.Cards = new ObservableCollection<DiscoverCardViewModel>(list);
        state.SavedScrollOffset = 0;
        if (ReferenceEquals(state, Current))
            ScrollToTopRequested?.Invoke(this, EventArgs.Empty);

        _ = LoadCoversAsync(list, state.BeginCoverLoad());
    }

    private async Task LoadCoversAsync(IReadOnlyList<DiscoverCardViewModel> cards, CancellationToken ct)
    {
        if (_disposed || cards.Count == 0) return;

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

        _anime.Dispose();
        _manga.Dispose();
        _coverGate.Dispose();
    }
}

public record AddToLibraryEventArgs(DiscoverCardViewModel Card);
