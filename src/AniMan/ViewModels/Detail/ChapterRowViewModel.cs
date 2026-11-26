using AniMan.Core.Common;
using AniMan.Core.Interfaces;
using AniMan.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AniMan.ViewModels.Detail;

public partial class ChapterRowViewModel : ObservableObject
{
    private readonly ITrackingService _tracking;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _progressGate;
    private bool _suppress;
    private bool _isSaving;

    public int LibraryItemId { get; init; }
    public int ChapterNumber { get; init; }

    /// <summary>Localized "Ch N" label (e.g. "Bölüm 3" in Turkish).</summary>
    public string ChapterLabel =>
        string.Format(LocalizationManager.Get("Detail_ChapterNumberFormat"), ChapterNumber);

    [ObservableProperty]
    private bool _isRead;

    [ObservableProperty]
    private bool _hasNote;

    /// <summary>UTC timestamp of when the chapter was read; null when unread.</summary>
    [ObservableProperty]
    private DateTime? _readAt;

    /// <summary>Short local date in the UI language (not the Windows region) shown next to read chapters.</summary>
    public string ReadAtDisplay => ReadAt is { } d
        ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime().ToString("d", LocalizationManager.Culture)
        : string.Empty;

    partial void OnReadAtChanged(DateTime? value) => OnPropertyChanged(nameof(ReadAtDisplay));

    public event EventHandler<ChapterRowViewModel>? ReadToggled;
    public event EventHandler<ChapterRowViewModel>? MarkUpToHereRequested;
    public event EventHandler<ChapterRowViewModel>? NoteRequested;

    /// <summary>The change could not be saved; the checkbox has already been put back.</summary>
    public event EventHandler<ErrorKind>? SaveFailed;

    /// <param name="progressGate">
    /// Shared by every row of the page and by "mark up to here", so progress writes for one
    /// title never interleave and the database cannot end up different from the checkboxes.
    /// </param>
    public ChapterRowViewModel(ITrackingService tracking, ILogger logger, SemaphoreSlim progressGate)
    {
        _tracking = tracking;
        _logger = logger;
        _progressGate = progressGate;
    }

    /// <summary>Sets the initial read state and date without triggering persistence.</summary>
    public void InitRead(bool read, DateTime? readAt)
    {
        _suppress = true;
        IsRead = read;
        _suppress = false;
        ReadAt = read ? readAt : null;
    }

    // Driven by the CheckBox two-way binding. Coalesced per row on the UI thread
    // so rapid clicks persist the latest checkbox state in order without inverting DB vs UI state.
    async partial void OnIsReadChanged(bool value)
    {
        if (_suppress || _isSaving) return;

        _isSaving = true;
        try
        {
            while (true)
            {
                var desired = IsRead;
                Result<TrackingResult> result;
                await _progressGate.WaitAsync();
                try
                {
                    result = await _tracking.SetChapterReadAsync(LibraryItemId, ChapterNumber, desired);
                }
                finally
                {
                    _progressGate.Release();
                }

                if (!result.IsSuccess)
                {
                    _suppress = true;
                    IsRead = !desired;
                    _suppress = false;
                    SaveFailed?.Invoke(this, result.Kind);
                    return;
                }

                ReadAt = desired ? (ReadAt ?? DateTime.UtcNow) : null;
                ReadToggled?.Invoke(this, this);

                if (IsRead == desired)
                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chapter read failed for item {Id} ch {Ch}",
                LibraryItemId, ChapterNumber);
            _suppress = true;
            IsRead = !value;
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
