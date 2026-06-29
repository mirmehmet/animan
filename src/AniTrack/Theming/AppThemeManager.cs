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
        var applied = ApplicationTheme.Dark;

        switch (mode)
        {
            case ThemeMode.Light:
                StopWatching(window);
                applied = ApplicationTheme.Light;
                ApplicationThemeManager.Apply(applied);
                break;

            case ThemeMode.Dark:
                StopWatching(window);
                applied = ApplicationTheme.Dark;
                ApplicationThemeManager.Apply(applied);
                break;

            case ThemeMode.System:
                applied = ResolveSystemTheme();
                ApplicationThemeManager.Apply(applied);
                StartWatching(window);
                break;
        }

        ApplyPalette(applied);
        ApplyBrandAccent();
    }

    /// <summary>Applies a font family globally via the dynamic resource key AppFontFamily.</summary>
    public static void ApplyFont(string fontFamily)
    {
        if (Application.Current is null) return;
        Application.Current.Resources["AppFontFamily"] = new FontFamily(fontFamily);
    }

    // Theme-dependent background / surface / text palette. Overrides Wpf.Ui's
    // theme brushes at the top resource level so they win over the merged theme
    // dictionary, and is re-applied on every theme switch so it stays sticky.
    private static void ApplyPalette(ApplicationTheme theme)
    {
        if (Application.Current is null) return;

        var res = Application.Current.Resources;
        bool dark = theme != ApplicationTheme.Light;

        // (background, card, controlFill, controlFillHover, stroke, textPrimary, textSecondary, textTertiary)
        var appBg     = dark ? "#0D0D10" : "#F4F5F7";
        var cardBg    = dark ? "#16161A" : "#FFFFFF";
        var ctrlFill  = dark ? "#1B1B21" : "#F0F1F3";
        var ctrlFill2 = dark ? "#232329" : "#E8EAED";
        var stroke    = dark ? "#14FFFFFF" : "#14000000";
        var textPri   = dark ? "#F5F5F7" : "#1A1A1F";
        var textSec   = dark ? "#9CA0A8" : "#5C616B";
        var textTer   = dark ? "#6B6F76" : "#8A8F99";

        var appBgColor = Hex(appBg);
        res["ApplicationBackgroundColor"] = appBgColor;
        res["ApplicationBackgroundBrush"] = Brush(appBgColor);

        res["CardBackgroundFillColorDefaultBrush"]    = Freeze(Hex(cardBg));
        res["ControlFillColorDefaultBrush"]           = Freeze(Hex(ctrlFill));
        res["ControlFillColorSecondaryBrush"]         = Freeze(Hex(ctrlFill2));
        res["ControlStrokeColorDefaultBrush"]         = Freeze(Hex(stroke));
        res["TextFillColorPrimaryBrush"]              = Freeze(Hex(textPri));
        res["TextFillColorSecondaryBrush"]            = Freeze(Hex(textSec));
        res["TextFillColorTertiaryBrush"]             = Freeze(Hex(textTer));
    }

    private static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

    private static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static SolidColorBrush Freeze(Color c) => Brush(c);

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
