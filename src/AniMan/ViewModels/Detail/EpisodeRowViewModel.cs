using AniMan.Core.Interfaces;
using AniMan.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniMan.ViewModels.Detail;

public partial class EpisodeRowViewModel : ObservableObject
{
    private readonly ITrackingService _tracking;
    private bool _suppress;

    public int LibraryItemId { get; init; }
    public int EpisodeNumber { get; init; }
    public string? Title { get; init; }

    /// <summary>Localized "Ep N" label (e.g. "Bölüm 3" in Turkish).</summary>
    public string EpisodeLabel =>
        string.Format(LocalizationManager.Get("Detail_EpisodeNumberFormat"), EpisodeNumber);

    [ObservableProperty]
    private bool _isWatched;

    [ObservableProperty]
    private bool _hasNote;

    /// <summary>UTC timestamp of when the episode was watched; null when unwatched.</summary>
    [ObservableProperty]
    private DateTime? _watchedAt;

    /// <summary>Short, culture-aware local date shown next to watched episodes.</summary>
    public string WatchedAtDisplay => WatchedAt is { } d
        ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime().ToString("d")
        : string.Empty;

    partial void OnWatchedAtChanged(DateTime? value) => OnPropertyChanged(nameof(WatchedAtDisplay));

    public event EventHandler<EpisodeRowViewModel>? WatchedToggled;
    public event EventHandler<EpisodeRowViewModel>? MarkUpToHereRequested;
    public event EventHandler<EpisodeRowViewModel>? NoteRequested;

    public EpisodeRowViewModel(ITrackingService tracking)
    {
        _tracking = tracking;
    }

    /// <summary>Sets the initial watched state and date without triggering persistence.</summary>
    public void InitWatched(bool watched, DateTime? watchedAt)
    {
        _suppress = true;
        IsWatched = watched;
        _suppress = false;
        WatchedAt = watched ? watchedAt : null;
    }

    // Driven by the CheckBox two-way binding. Persists the change, then notifies.
    // async void semantics: an escaping exception would crash the dispatcher.
    async partial void OnIsWatchedChanged(bool value)
    {
        if (_suppress) return;

        try
        {
            var result = await _tracking.SetEpisodeWatchedAsync(LibraryItemId, EpisodeNumber, value);
            if (result.IsSuccess)
            {
                WatchedAt = value ? DateTime.UtcNow : null;
                WatchedToggled?.Invoke(this, this);
                return;
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Episode watched toggle failed for item {Id} ep {Ep}",
                LibraryItemId, EpisodeNumber);
        }

        // Revert UI to stay consistent with the database.
        _suppress = true;
        IsWatched = !value;
        _suppress = false;
    }

    [RelayCommand]
    private void MarkUpToHere() => MarkUpToHereRequested?.Invoke(this, this);

    [RelayCommand]
    private void OpenNote() => NoteRequested?.Invoke(this, this);
}
