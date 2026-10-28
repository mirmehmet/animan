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

    /// <summary>Short, culture-aware local date shown next to read chapters.</summary>
    public string ReadAtDisplay => ReadAt is { } d
        ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime().ToString("d")
        : string.Empty;

    partial void OnReadAtChanged(DateTime? value) => OnPropertyChanged(nameof(ReadAtDisplay));

    public event EventHandler<ChapterRowViewModel>? ReadToggled;
    public event EventHandler<ChapterRowViewModel>? MarkUpToHereRequested;
    public event EventHandler<ChapterRowViewModel>? NoteRequested;

    public ChapterRowViewModel(ITrackingService tracking, ILogger logger)
    {
        _tracking = tracking;
        _logger = logger;
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
                var result = await _tracking.SetChapterReadAsync(LibraryItemId, ChapterNumber, desired);
                if (!result.IsSuccess)
                {
                    _suppress = true;
                    IsRead = !desired;
                    _suppress = false;
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
