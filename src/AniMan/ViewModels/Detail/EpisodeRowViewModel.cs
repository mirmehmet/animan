using AniMan.Core.Interfaces;
using AniMan.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AniMan.ViewModels.Detail;

public partial class EpisodeRowViewModel : ObservableObject
{
    private readonly ITrackingService _tracking;
    private readonly ILogger _logger;
    private bool _suppress;
    private bool _isSaving;

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

    public EpisodeRowViewModel(ITrackingService tracking, ILogger logger)
    {
        _tracking = tracking;
        _logger = logger;
    }

    /// <summary>Sets the initial watched state and date without triggering persistence.</summary>
    public void InitWatched(bool watched, DateTime? watchedAt)
    {
        _suppress = true;
        IsWatched = watched;
        _suppress = false;
        WatchedAt = watched ? watchedAt : null;
    }

    // Driven by the CheckBox two-way binding. Coalesced per row on the UI thread
    // so rapid clicks persist the latest checkbox state in order without inverting DB vs UI state.
    async partial void OnIsWatchedChanged(bool value)
    {
        if (_suppress || _isSaving) return;

        _isSaving = true;
        try
        {
            while (true)
            {
                var desired = IsWatched;
                var result = await _tracking.SetEpisodeWatchedAsync(LibraryItemId, EpisodeNumber, desired);
                if (!result.IsSuccess)
                {
                    _suppress = true;
                    IsWatched = !desired;
                    _suppress = false;
                    return;
                }

                WatchedAt = desired ? (WatchedAt ?? DateTime.UtcNow) : null;
                WatchedToggled?.Invoke(this, this);

                if (IsWatched == desired)
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Episode watched toggle failed for item {Id} ep {Ep}",
                LibraryItemId, EpisodeNumber);
            _suppress = true;
            IsWatched = !value;
            _suppress = false;
        }
        finally
        {
            _isSaving = false;
        }
    }

    [RelayCommand]
    private void MarkUpToHere() => MarkUpToHereRequested?.Invoke(this, this);

    [RelayCommand]
    private void OpenNote() => NoteRequested?.Invoke(this, this);
}
