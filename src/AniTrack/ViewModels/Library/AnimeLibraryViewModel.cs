using System.Collections.ObjectModel;
using AniTrack.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniTrack.ViewModels.Library;

public partial class AnimeLibraryViewModel : ObservableObject
{
    private readonly ITrackingService _tracking;
    private List<LibraryCardViewModel> _allItems = [];

    [ObservableProperty]
    private ObservableCollection<LibraryCardViewModel> _items = [];

    [ObservableProperty]
    private int _activeStatusId; // 0 = All

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isGridView = true;

    [ObservableProperty]
    private bool _isLoading;

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
                Title = item.Snapshot?.Title ?? "(no title)",
                CoverLocalPath = item.Snapshot?.CoverLocalPath,
                StatusId = item.StatusId,
                StatusName = item.Status?.Name ?? string.Empty,
                Score = item.Score,
                IsFavorite = item.IsFavorite,
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
    partial void OnActiveStatusIdChanged(int value) => ApplyFilter();

    [RelayCommand]
    private void ToggleView() => IsGridView = !IsGridView;

    private void ApplyFilter()
    {
        var filtered = _allItems.AsEnumerable();

        if (ActiveStatusId != 0)
            filtered = filtered.Where(c => c.StatusId == ActiveStatusId);

        if (!string.IsNullOrWhiteSpace(SearchQuery))
            filtered = filtered.Where(c =>
                c.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));

        Items = new ObservableCollection<LibraryCardViewModel>(filtered);
    }
}
