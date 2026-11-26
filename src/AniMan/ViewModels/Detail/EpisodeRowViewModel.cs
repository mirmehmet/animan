using AniMan.Core.Common;
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
    private readonly SemaphoreSlim _progressGate;
    private bool _suppress;
    private bool _isSaving;

    public int LibraryItemId { get; init; }
    public int EpisodeNumber { get; init; }

    /// <summary>Filled in after the row is shown — titles may need the network, rows never do.</summary>
    [ObservableProperty]
    private string? _title;

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

    /// <summary>Short local date in the UI language (not the Windows region) shown next to watched episodes.</summary>
    public string WatchedAtDisplay => WatchedAt is { } d
        ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime().ToString("d", LocalizationManager.Culture)
        : string.Empty;

    partial void OnWatchedAtChanged(DateTime? value) => OnPropertyChanged(nameof(WatchedAtDisplay));

    public event EventHandler<EpisodeRowViewModel>? WatchedToggled;
    public event EventHandler<EpisodeRowViewModel>? MarkUpToHereRequested;
    public event EventHandler<EpisodeRowViewModel>? NoteRequested;

    /// <summary>The change could not be saved; the checkbox has already been put back.</summary>
    public event EventHandler<ErrorKind>? SaveFailed;

    /// <param name="progressGate">
    /// Shared by every row of the page and by "mark up to here", so progress writes for one
    /// title never interleave and the database cannot end up different from the checkboxes.
    /// </param>
    public EpisodeRowViewModel(ITrackingService tracking, ILogger logger, SemaphoreSlim progressGate)
    {
        _tracking = tracking;
        _logger = logger;
        _progressGate = progressGate;
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
                Result<TrackingResult> result;
                await _progressGate.WaitAsync();
                try
                {
                    result = await _tracking.SetEpisodeWatchedAsync(LibraryItemId, EpisodeNumber, desired);
                }
                finally
                {
                    _progressGate.Release();
                }

                if (!result.IsSuccess)
                {
                    _suppress = true;
                    IsWatched = !desired;
                    _suppress = false;
                    SaveFailed?.Invoke(this, result.Kind);
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
            SaveFailed?.Invoke(this, ErrorKind.Unexpected);
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
