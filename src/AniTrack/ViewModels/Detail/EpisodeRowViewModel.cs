using AniTrack.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniTrack.ViewModels.Detail;

public partial class EpisodeRowViewModel : ObservableObject
{
    private readonly ITrackingService _tracking;

    public int LibraryItemId { get; init; }
    public int EpisodeNumber { get; init; }
    public string? Title { get; init; }

    [ObservableProperty]
    private bool _isWatched;

    [ObservableProperty]
    private bool _hasNote;

    public event EventHandler<EpisodeRowViewModel>? WatchedToggled;
    public event EventHandler<EpisodeRowViewModel>? MarkUpToHereRequested;
    public event EventHandler<EpisodeRowViewModel>? NoteRequested;

    public EpisodeRowViewModel(ITrackingService tracking)
    {
        _tracking = tracking;
    }

    [RelayCommand]
    private async Task ToggleWatchedAsync()
    {
        var result = await _tracking.ToggleEpisodeAsync(LibraryItemId, EpisodeNumber);
        if (result.IsSuccess)
        {
            IsWatched = !IsWatched;
            WatchedToggled?.Invoke(this, this);
        }
    }

    [RelayCommand]
    private void MarkUpToHere() => MarkUpToHereRequested?.Invoke(this, this);

    [RelayCommand]
    private void OpenNote() => NoteRequested?.Invoke(this, this);
}
