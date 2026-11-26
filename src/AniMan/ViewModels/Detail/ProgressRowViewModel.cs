using AniMan.Core.Common;
using AniMan.Core.Interfaces;
using AniMan.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AniMan.ViewModels.Detail;

/// <summary>
/// One row of the detail page's progress list: an episode (anime) or a chapter (manga).
/// "Done" means watched or read. Replaces the former episode and chapter row view models,
/// which were copies of each other (audit AM-100).
/// </summary>
public partial class ProgressRowViewModel : ObservableObject
{
    private readonly ITrackingService _tracking;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _progressGate;
    private readonly bool _isManga;
    private bool _suppress;
    private bool _isSaving;

    public int LibraryItemId { get; init; }

    /// <summary>Episode or chapter number.</summary>
    public int Number { get; init; }

    /// <summary>Localized "Ep N" / "Ch N" label (e.g. "Bölüm 3" in Turkish).</summary>
    public string Label =>
        string.Format(LocalizationManager.Get(_isManga ? "Detail_ChapterNumberFormat" : "Detail_EpisodeNumberFormat"), Number);

    /// <summary>Episode title, filled in after the row is shown — titles may need the network, rows never do. Always empty for chapters.</summary>
    [ObservableProperty]
    private string? _title;

    [ObservableProperty]
    private bool _isDone;

    [ObservableProperty]
    private bool _hasNote;

    /// <summary>UTC timestamp of when the episode was watched or the chapter read; null when not done.</summary>
    [ObservableProperty]
    private DateTime? _doneAt;

    /// <summary>Short local date in the UI language (not the Windows region) shown next to done rows.</summary>
    public string DoneAtDisplay => DoneAt is { } d
        ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime().ToString("d", LocalizationManager.Culture)
        : string.Empty;

    partial void OnDoneAtChanged(DateTime? value) => OnPropertyChanged(nameof(DoneAtDisplay));

    public event EventHandler<ProgressRowViewModel>? Toggled;
    public event EventHandler<ProgressRowViewModel>? MarkUpToHereRequested;
    public event EventHandler<ProgressRowViewModel>? NoteRequested;

    /// <summary>The change could not be saved; the checkbox has already been put back.</summary>
    public event EventHandler<ErrorKind>? SaveFailed;

    /// <param name="progressGate">
    /// Shared by every row of the page and by "mark up to here", so progress writes for one
    /// title never interleave and the database cannot end up different from the checkboxes.
    /// </param>
    /// <param name="isManga">Chapter row (read) instead of episode row (watched).</param>
    public ProgressRowViewModel(ITrackingService tracking, ILogger logger, SemaphoreSlim progressGate, bool isManga)
    {
        _tracking = tracking;
        _logger = logger;
        _progressGate = progressGate;
        _isManga = isManga;
    }

    /// <summary>Sets the done state and date without saving anything.</summary>
    public void InitDone(bool done, DateTime? doneAt)
    {
        _suppress = true;
        IsDone = done;
        _suppress = false;
        DoneAt = done ? doneAt : null;
    }

    private Task<Result<TrackingResult>> SaveAsync(bool done) => _isManga
        ? _tracking.SetChapterReadAsync(LibraryItemId, Number, done)
        : _tracking.SetEpisodeWatchedAsync(LibraryItemId, Number, done);

    // Driven by the CheckBox two-way binding. Coalesced per row on the UI thread
    // so rapid clicks persist the latest checkbox state in order without inverting DB vs UI state.
    async partial void OnIsDoneChanged(bool value)
    {
        if (_suppress || _isSaving) return;

        _isSaving = true;
        try
        {
            while (true)
            {
                var desired = IsDone;
                Result<TrackingResult> result;
                await _progressGate.WaitAsync();
                try
                {
                    result = await SaveAsync(desired);
                }
                finally
                {
                    _progressGate.Release();
                }

                if (!result.IsSuccess)
                {
                    _suppress = true;
                    IsDone = !desired;
                    _suppress = false;
                    SaveFailed?.Invoke(this, result.Kind);
                    return;
                }

                DoneAt = desired ? (DoneAt ?? DateTime.UtcNow) : null;
                Toggled?.Invoke(this, this);

                if (IsDone == desired)
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Progress toggle failed for item {Id} #{Number} (manga: {IsManga})",
                LibraryItemId, Number, _isManga);
            _suppress = true;
            IsDone = !value;
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
