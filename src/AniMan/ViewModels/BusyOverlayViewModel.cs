using AniMan.Localization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniMan.ViewModels;

/// <summary>
/// Drives the window-wide progress overlay in <see cref="MainWindow"/>: a modal panel with a
/// title, a status line and a percentage bar, shown while a long operation (backup import)
/// runs and the library must not be touched. Unlike a ContentDialog it has no close button
/// and ignores Esc, so it can't be dismissed while the work is still running.
/// </summary>
public sealed partial class BusyOverlayViewModel : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _message = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PercentText))]
    private double _percent;

    /// <summary>"%62" in Turkish, "62%" in English.</summary>
    public string PercentText =>
        string.Format(LocalizationManager.Get("Common_PercentFormat"), (int)Math.Floor(Percent));

    public void Show(string title, string message)
    {
        Title = title;
        Message = message;
        Percent = 0;
        IsBusy = true;
    }

    /// <param name="fraction">Overall completion, 0–1. The bar never moves backwards.</param>
    public void Update(string message, double fraction)
    {
        Message = message;
        Percent = Math.Max(Percent, Math.Clamp(fraction, 0, 1) * 100);
    }

    public void Hide() => IsBusy = false;
}
