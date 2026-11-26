using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AniMan.Core.Interfaces;
using AniMan.Core.Library;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniMan.ViewModels.Library;

/// <summary>
/// The cover-size slider shared by the anime and manga library pages (one setting for both,
/// persisted). Exposes the card geometry and info level for the current step, and tells the
/// pages when covers should be re-decoded at a new resolution — debounced, so dragging the
/// slider through several steps reloads covers and saves the setting once, after it settles.
/// </summary>
public sealed partial class LibraryLayoutViewModel : ObservableObject
{
    private const string SettingKey = "LibraryCardSize";
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(300);

    private readonly ISettingsService _settings;
    private readonly DispatcherTimer _reloadTimer;
    private Task? _loadTask;
    private bool _loaded;
    private int _lastDecodeWidth;

    /// <summary>Slider value; whole steps 0..<see cref="CardSizes.MaxStep"/> (double for the Slider binding).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardWidth), nameof(CoverHeight), nameof(Detail))]
    private double _sizeStep = CardSizes.DefaultStep;

    public LibraryLayoutViewModel(ISettingsService settings)
    {
        _settings = settings;
        _reloadTimer = new DispatcherTimer { Interval = ReloadDelay };
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            _settings.SaveInBackground(SettingKey, Step.ToString(System.Globalization.CultureInfo.InvariantCulture));

            var width = CoverDecodeWidth;
            if (width == _lastDecodeWidth) return;
            _lastDecodeWidth = width;
            CoverResolutionChanged?.Invoke(this, width);
        };
    }

    public double MaxStep => CardSizes.MaxStep;

    private int Step => CardSizes.ClampStep((int)Math.Round(SizeStep));

    public double CardWidth => CardSizes.WidthOf(Step);
    public double CoverHeight => CardSizes.CoverHeightOf(CardWidth);
    public CardDetail Detail => CardSizes.DetailOf(Step);

    /// <summary>
    /// Pixel width covers are decoded at: the card width at the screen's scale, so a cover
    /// is never decoded larger than it is drawn (memory follows the chosen size).
    /// </summary>
    public int CoverDecodeWidth
    {
        get
        {
            var scale = Application.Current?.MainWindow is { } window
                ? VisualTreeHelper.GetDpi(window).DpiScaleX
                : 1.0;
            return (int)Math.Ceiling(CardWidth * Math.Max(1.0, scale));
        }
    }

    /// <summary>Raised (debounced) with the new decode width when covers should be reloaded.</summary>
    public event EventHandler<int>? CoverResolutionChanged;

    /// <summary>Reads the saved size once; both library pages await the same load.</summary>
    public Task EnsureLoadedAsync() => _loadTask ??= LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var saved = await _settings.GetAsync(SettingKey);
            if (int.TryParse(saved, out var step))
                SizeStep = CardSizes.ClampStep(step);
        }
        finally
        {
            _lastDecodeWidth = CoverDecodeWidth;
            _loaded = true;
        }
    }

    partial void OnSizeStepChanged(double value)
    {
        if (!_loaded) return;

        _reloadTimer.Stop();
        _reloadTimer.Start();
    }
}
