using System.Collections.ObjectModel;
using AniTrack.Core.Domain.Enums;
using AniTrack.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniTrack.ViewModels.Discover;

public enum DiscoverTab { ThisSeason, Upcoming, TopAnime, TopManga }

public partial class DiscoverViewModel : ObservableObject
{
    private readonly ICatalogService _catalog;
    private readonly ISnapshotService _snapshot;
    private readonly ITrackingService _tracking;

    private HashSet<(int MalId, MediaType MediaType)> _librarySet = [];

    [ObservableProperty] private ObservableCollection<DiscoverCardViewModel> _cards = [];
    [ObservableProperty] private DiscoverTab _activeTab = DiscoverTab.ThisSeason;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private bool _searchManga;
    [ObservableProperty] private bool _isLoading;

    public event EventHandler<AddToLibraryEventArgs>? AddToLibraryRequested;

    public DiscoverViewModel(ICatalogService catalog, ISnapshotService snapshot, ITrackingService tracking)
    {
        _catalog = catalog;
        _snapshot = snapshot;
        _tracking = tracking;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await LoadLibrarySetAsync();
        await LoadTabAsync(ActiveTab);
    }

    [RelayCommand]
    private async Task SwitchTabAsync(DiscoverTab tab)
    {
        ActiveTab = tab;
        await LoadTabAsync(tab);
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return;
        IsLoading = true;
        try
        {
            if (SearchManga)
            {
                var result = await _catalog.SearchMangaAsync(SearchQuery);
                if (result.IsSuccess)
                    SetCards(result.Value!.Select(m => new DiscoverCardViewModel
                    {
                        MalId = m.Id, Title = m.Title,
                        CoverUrl = m.CoverMediumUrl, Score = m.Score,
                        Type = m.Type, MediaType = MediaType.Manga,
                        IsInLibrary = _librarySet.Contains((m.Id, MediaType.Manga))
                    }));
            }
            else
            {
                var result = await _catalog.SearchAnimeAsync(SearchQuery);
                if (result.IsSuccess)
                    SetCards(result.Value!.Select(a => new DiscoverCardViewModel
                    {
                        MalId = a.Id, Title = a.Title,
                        CoverUrl = a.CoverMediumUrl, Score = a.Score,
                        Type = a.Type, MediaType = MediaType.Anime,
                        IsInLibrary = _librarySet.Contains((a.Id, MediaType.Anime))
                    }));
            }
        }
        finally { IsLoading = false; }
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

    private async Task LoadTabAsync(DiscoverTab tab)
    {
        IsLoading = true;
        try
        {
            switch (tab)
            {
                case DiscoverTab.ThisSeason:
                    var season = await _catalog.GetCurrentSeasonAsync();
                    if (season.IsSuccess)
                        SetCards(season.Value!.Select(a => ToCard(a, MediaType.Anime)));
                    break;

                case DiscoverTab.Upcoming:
                    var upcoming = await _catalog.GetUpcomingSeasonAsync();
                    if (upcoming.IsSuccess)
                        SetCards(upcoming.Value!.Select(a => ToCard(a, MediaType.Anime)));
                    break;

                case DiscoverTab.TopAnime:
                    var topAnime = await _catalog.GetTopAnimeAsync();
                    if (topAnime.IsSuccess)
                        SetCards(topAnime.Value!.Select(a => ToCard(a, MediaType.Anime)));
                    break;

                case DiscoverTab.TopManga:
                    var topManga = await _catalog.GetTopMangaAsync();
                    if (topManga.IsSuccess)
                        SetCards(topManga.Value!.Select(m => new DiscoverCardViewModel
                        {
                            MalId = m.Id, Title = m.Title,
                            CoverUrl = m.CoverMediumUrl, Score = m.Score,
                            Type = m.Type, MediaType = MediaType.Manga,
                            IsInLibrary = _librarySet.Contains((m.Id, MediaType.Manga))
                        }));
                    break;
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

    private DiscoverCardViewModel ToCard(Core.Domain.Models.CachedAnime a, MediaType mt) =>
        new()
        {
            MalId = a.Id, Title = a.Title,
            CoverUrl = a.CoverMediumUrl, Score = a.Score,
            Type = a.Type, MediaType = mt,
            IsInLibrary = _librarySet.Contains((a.Id, mt))
        };

    private void SetCards(IEnumerable<DiscoverCardViewModel> source) =>
        Cards = new ObservableCollection<DiscoverCardViewModel>(source);
}

public record AddToLibraryEventArgs(DiscoverCardViewModel Card);
