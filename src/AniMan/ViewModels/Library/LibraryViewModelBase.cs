using System.Collections.ObjectModel;
using AniMan.Core.Domain.Enums;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniMan.ViewModels.Library;

public enum LibraryRatingSort { MyScoreHighToLow, MyScoreLowToHigh, MalScoreHighToLow, Alphabetical }

/// <summary>
/// Shared implementation of the anime/manga library pages: loading, status/favorite
/// filtering, search and rating sort. Subclasses supply only the media type and the
/// progress projection.
/// </summary>
public abstract partial class LibraryViewModelBase : ObservableObject
{
    private readonly ITrackingService _tracking;
    private readonly MediaType _mediaType;
    private List<LibraryCardViewModel> _allItems = [];

    [ObservableProperty]
    private ObservableCollection<LibraryCardViewModel> _items = [];

    [ObservableProperty]
    private int _activeStatusId; // 0=All, 98=Ratings, 99=Favorites

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private LibraryRatingSort _ratingSortMode = LibraryRatingSort.MyScoreHighToLow;

    [ObservableProperty]
    private int _ratingSortIndex; // ComboBox SelectedIndex → LibraryRatingSort enum

    public bool IsRatingsTab => ActiveStatusId == 98;

    protected LibraryViewModelBase(ITrackingService tracking, MediaType mediaType)
    {
        _tracking = tracking;
        _mediaType = mediaType;
    }

    /// <summary>Watched/read count and total for the given item, per media type.</summary>
    protected abstract (int Watched, int? Total) GetProgress(LibraryItem item);

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var result = await _tracking.GetLibraryAsync(_mediaType.ToString().ToLowerInvariant());
            if (!result.IsSuccess) return;

            _allItems = result.Value!.Select(item =>
            {
                var (watched, total) = GetProgress(item);
                return new LibraryCardViewModel
                {
                    LibraryItemId = item.Id,
                    MalId = item.MalId,
                    MediaType = _mediaType,
                    Title = item.Snapshot?.Title ?? LocalizationManager.Get("Library_NoTitle"),
                    CoverLocalPath = item.Snapshot?.CoverLocalPath,
                    StatusId = item.StatusId,
                    StatusName = item.Status?.Name ?? string.Empty,
                    StatusColor = item.Status?.Color,
                    Score = item.Score,
                    UserRating = item.UserRating,
                    MalScore = item.Snapshot?.MalScore,
                    IsFavorite = item.IsFavorite,
                    ProgressWatched = watched,
                    ProgressTotal = total
                };
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

    // Unrated items always sort to the end, regardless of direction.
    private IEnumerable<LibraryCardViewModel> ApplyRatingSort(IEnumerable<LibraryCardViewModel> source) =>
        RatingSortMode switch
        {
            LibraryRatingSort.MyScoreHighToLow  => source.OrderBy(c => c.UserRating is null).ThenByDescending(c => c.UserRating),
            LibraryRatingSort.MyScoreLowToHigh  => source.OrderBy(c => c.UserRating is null).ThenBy(c => c.UserRating),
            LibraryRatingSort.MalScoreHighToLow => source.OrderBy(c => c.MalScore is null).ThenByDescending(c => c.MalScore),
            LibraryRatingSort.Alphabetical      => source.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase),
            _                                   => source
        };
}
