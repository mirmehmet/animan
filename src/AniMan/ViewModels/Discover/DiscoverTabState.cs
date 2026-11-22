using System.Collections.ObjectModel;
using AniMan.Core.Domain.Enums;
using AniMan.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniMan.ViewModels.Discover;

/// <summary>
/// Everything one tab of the Add page remembers: its search text, the cards on screen,
/// the popular-list page, scroll position, and its own in-flight request. Requests are
/// versioned per tab, so a stale response never lands on top of a newer one.
/// </summary>
public sealed partial class DiscoverTabState(MediaType mediaType) : ObservableObject, IDisposable
{
    private CancellationTokenSource? _queryCts;
    private CancellationTokenSource? _coverCts;

    public MediaType MediaType { get; } = mediaType;

    public string SearchPlaceholder { get; } = LocalizationManager.Get(
        mediaType == MediaType.Manga ? "Discover_SearchMangaPlaceholder" : "Discover_SearchAnime");

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoResults), nameof(ShowPager))]
    private ObservableCollection<DiscoverCardViewModel> _cards = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoResults), nameof(CanGoPrevious), nameof(CanGoNext))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(HasNoResults), nameof(ShowPager))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoResults), nameof(ShowPager))]
    private bool _isSearchMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel), nameof(CanGoPrevious))]
    private int _currentPage = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private bool _hasNextPage;

    /// <summary>Vertical scroll offset to restore when the tab or page becomes visible again.</summary>
    public double SavedScrollOffset { get; set; }

    /// <summary>Incremented by <see cref="BeginQuery"/>; a response whose version no longer matches is discarded.</summary>
    public int QueryVersion { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Only a completed search can be "no results" — an empty popular list is an error.</summary>
    public bool HasNoResults => IsSearchMode && !IsLoading && !HasError && Cards.Count == 0;

    public bool ShowPager => !IsSearchMode && !HasError && Cards.Count > 0;

    public bool CanGoPrevious => !IsLoading && CurrentPage > 1;

    public bool CanGoNext => !IsLoading && HasNextPage;

    public string PageLabel => string.Format(LocalizationManager.Get("Discover_PageLabel"), CurrentPage);

    /// <summary>True when the tab has never shown anything and nothing is on its way.</summary>
    public bool NeedsLoad => !IsLoading && !HasError && !IsSearchMode && Cards.Count == 0;

    /// <summary>Cancels this tab's previous request and starts a new version.</summary>
    public CancellationToken BeginQuery(out int version)
    {
        CancelAndDispose(ref _queryCts);
        _queryCts = new CancellationTokenSource();
        version = ++QueryVersion;
        return _queryCts.Token;
    }

    /// <summary>Cancels cover downloads for this tab's previous cards.</summary>
    public CancellationToken BeginCoverLoad()
    {
        CancelAndDispose(ref _coverCts);
        _coverCts = new CancellationTokenSource();
        return _coverCts.Token;
    }

    /// <summary>Back to a never-loaded tab: first page, no search, nothing in flight.</summary>
    public void Reset()
    {
        CancelAndDispose(ref _queryCts);
        CancelAndDispose(ref _coverCts);
        QueryVersion++;

        SearchQuery = string.Empty;
        IsSearchMode = false;
        CurrentPage = 1;
        HasNextPage = false;
        ErrorMessage = null;
        IsLoading = false;
        Cards = [];
        SavedScrollOffset = 0;
    }

    public void Dispose()
    {
        CancelAndDispose(ref _queryCts);
        CancelAndDispose(ref _coverCts);
    }

    private static void CancelAndDispose(ref CancellationTokenSource? cts)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }
}
