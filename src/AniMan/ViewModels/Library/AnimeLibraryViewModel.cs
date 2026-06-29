using System.Collections.ObjectModel;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Interfaces;
using AniMan.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniMan.ViewModels.Library;

public enum LibraryRatingSort { MyScoreHighToLow, MyScoreLowToHigh, MalScoreHighToLow, Alphabetical }

public partial class AnimeLibraryViewModel : ObservableObject
{
    private readonly ITrackingService _tracking;
    private List<LibraryCardViewModel> _allItems = [];

    [ObservableProperty]
    private ObservableCollection<LibraryCardViewModel> _items = [];

    [ObservableProperty]
    private int _activeStatusId; // 0=All, 98=Ratings, 99=Favorites

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isGridView = true;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private LibraryRatingSort _ratingSortMode = LibraryRatingSort.MyScoreHighToLow;

    [ObservableProperty]
    private int _ratingSortIndex; // ComboBox SelectedIndex → LibraryRatingSort enum

    public bool IsRatingsTab => ActiveStatusId == 98;

    public AnimeLibraryViewModel(ITrackingService tracking)
    {
        _tracking = tracking;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var result = await _tracking.GetLibraryAsync("anime");
            if (!result.IsSuccess) return;

            _allItems = result.Value!.Select(item => new LibraryCardViewModel
            {
                LibraryItemId = item.Id,
                MalId = item.MalId,
                MediaType = MediaType.Anime,
                Title = item.Snapshot?.Title ?? LocalizationManager.Get("Library_NoTitle"),
                CoverLocalPath = item.Snapshot?.CoverLocalPath,
                StatusId = item.StatusId,
                StatusName = item.Status?.Name ?? string.Empty,
                StatusColor = item.Status?.Color,
                Score = item.Score,
                UserRating = item.UserRating,
                MalScore = item.Snapshot?.MalScore,
                IsFavorite = item.IsFavorite,
                ProgressWatched = item.EpisodeProgress?.Count(e => e.IsWatched) ?? 0,
                ProgressTotal = item.Snapshot?.TotalEpisodes
            }).ToList();

            foreach (var card in _allItems)
                card.LoadCoverImage();

            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnActiveStatusIdChanged(int value)
    {
        OnPropertyChanged(nameof(IsRatingsTab));
        ApplyFilter();
    }
    partial void OnRatingSortModeChanged(LibraryRatingSort value) => ApplyFilter();
    partial void OnRatingSortIndexChanged(int value)
    {
        RatingSortMode = (LibraryRatingSort)value;
    }

    [RelayCommand]
    private void ToggleView() => IsGridView = !IsGridView;

    private void ApplyFilter()
    {
        var filtered = _allItems.AsEnumerable();

        if (ActiveStatusId == 99)
            filtered = filtered.Where(c => c.IsFavorite);
        else if (ActiveStatusId == 98)
            filtered = ApplyRatingSort(filtered);
        else if (ActiveStatusId != 0)
            filtered = filtered.Where(c => c.StatusId == ActiveStatusId);

        if (!string.IsNullOrWhiteSpace(SearchQuery))
            filtered = filtered.Where(c =>
                c.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));

        Items = new ObservableCollection<LibraryCardViewModel>(filtered);
    }

    private IEnumerable<LibraryCardViewModel> ApplyRatingSort(IEnumerable<LibraryCardViewModel> source) =>
        RatingSortMode switch
        {
            LibraryRatingSort.MyScoreHighToLow  => source.OrderByDescending(c => c.UserRating ?? 0m),
            LibraryRatingSort.MyScoreLowToHigh  => source.OrderBy(c => c.UserRating ?? 0m),
            LibraryRatingSort.MalScoreHighToLow => source.OrderByDescending(c => c.MalScore ?? 0.0),
            LibraryRatingSort.Alphabetical      => source.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase),
            _                                   => source
        };
}
