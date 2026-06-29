using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using ThemeMode = AniTrack.ViewModels.Settings.ThemeMode;

namespace AniTrack.Theming;

public static class AppThemeManager
{
    private static bool _watching;

    // Brand violet accent applied after every theme change.
    private static readonly Color BrandAccent = Color.FromRgb(0x8B, 0x5C, 0xF6);

    public static ThemeMode Parse(string? stored) => stored switch
    {
        "light"  => ThemeMode.Light,
        "system" => ThemeMode.System,
        _        => ThemeMode.System
    };

    public static string ToStored(ThemeMode mode) => mode switch
    {
        ThemeMode.Light  => "light",
        ThemeMode.System => "system",
        _                => "dark"
    };

    public static void Apply(ThemeMode mode)
    {
        var window = Application.Current?.MainWindow;

        switch (mode)
        {
            case ThemeMode.Light:
                StopWatching(window);
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;

            case ThemeMode.Dark:
                StopWatching(window);
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;

            case ThemeMode.System:
                ApplicationThemeManager.Apply(ResolveSystemTheme());
                StartWatching(window);
                break;
        }

        ApplyBrandAccent();
    }

    /// <summary>Applies a font family globally via the dynamic resource key AppFontFamily.</summary>
    public static void ApplyFont(string fontFamily)
    {
        if (Application.Current is null) return;
        Application.Current.Resources["AppFontFamily"] = new FontFamily(fontFamily);
    }

    private static void ApplyBrandAccent()
    {
        if (Application.Current is null) return;

        // Override Wpf.Ui accent brushes with our brand violet so all controls
        // (NavigationView selection indicator, primary buttons, etc.) use it.
        var accent      = new SolidColorBrush(BrandAccent);
        var accentLight = new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA));
        var accentDark  = new SolidColorBrush(Color.FromRgb(0x6D, 0x28, 0xD9));

        accent.Freeze();
        accentLight.Freeze();
        accentDark.Freeze();

        Application.Current.Resources["SystemAccentColor"]      = BrandAccent;
        Application.Current.Resources["SystemAccentColorBrush"] = accent;

        // Wpf.Ui layered accent brushes (used by NavigationView, Button primary, etc.)
        Application.Current.Resources["AccentFillColorDefaultBrush"]          = accent;
        Application.Current.Resources["AccentFillColorSecondaryBrush"]        = accentLight;
        Application.Current.Resources["AccentFillColorTertiaryBrush"]         = accentDark;
        Application.Current.Resources["AccentFillColorDisabledBrush"]         = accentDark;
        Application.Current.Resources["AccentTextFillColorPrimaryBrush"]      = accent;
        Application.Current.Resources["AccentTextFillColorSecondaryBrush"]    = accentLight;
        Application.Current.Resources["AccentButtonBackgroundBrush"]          = accent;
        Application.Current.Resources["AccentButtonBackgroundPointerOverBrush"] = accentLight;
        Application.Current.Resources["AccentButtonBackgroundPressedBrush"]   = accentDark;
    }

    private static ApplicationTheme ResolveSystemTheme() =>
        ApplicationThemeManager.GetSystemTheme() switch
        {
            SystemTheme.Light or SystemTheme.Glow or SystemTheme.CapturedMotion
                or SystemTheme.Sunrise or SystemTheme.Flow => ApplicationTheme.Light,
            _ => ApplicationTheme.Dark
        };

    /// <summary>
    /// Attaches SystemThemeWatcher after the window is shown (needs a HWND).
    /// Does NOT re-apply the theme — call this only after Apply() already ran.
    /// </summary>
    public static void AttachSystemWatcher(Window window)
    {
        if (_watching) return;
        SystemThemeWatcher.Watch(window);
        _watching = true;
    }

    private static void StartWatching(Window? window)
    {
        if (window is null || _watching) return;
        SystemThemeWatcher.Watch(window);
        _watching = true;
    }

    private static void StopWatching(Window? window)
    {
        if (window is null || !_watching) return;
        SystemThemeWatcher.UnWatch(window);
        _watching = false;
    }
}
