using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using ThemeMode = AniMan.ViewModels.Settings.ThemeMode;

namespace AniMan.Theming;

public static class AppThemeManager
{
    private static bool _watching;
    private static bool _changedHooked;

    // Brand violet accent applied after every theme change.
    private static readonly Color BrandAccent = Color.FromRgb(0x8B, 0x5C, 0xF6);

    public static ThemeMode Parse(string? stored) => stored?.Trim().ToLowerInvariant() switch
    {
        "light"  => ThemeMode.Light,
        "dark"   => ThemeMode.Dark,
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
        EnsureChangedHook();

        var window = Application.Current?.MainWindow;
        var applied = ApplicationTheme.Dark;

        switch (mode)
        {
            case ThemeMode.Light:
                StopWatching(window);
                applied = ApplicationTheme.Light;
                ApplicationThemeManager.Apply(applied, WindowBackdropType.None, updateAccent: false);
                break;

            case ThemeMode.Dark:
                StopWatching(window);
                applied = ApplicationTheme.Dark;
                ApplicationThemeManager.Apply(applied, WindowBackdropType.None, updateAccent: false);
                break;

            case ThemeMode.System:
                applied = ResolveSystemTheme();
                ApplicationThemeManager.Apply(applied, WindowBackdropType.None, updateAccent: false);
                StartWatching(window);
                break;
        }

        ApplyPalette(applied);
        ApplyBrandAccent();
        SyncWindowBackground(window);
    }

    /// <summary>Applies a font family globally via the dynamic resource key AppFontFamily.</summary>
    public static void ApplyFont(string fontFamily)
    {
        if (Application.Current is null) return;
        var font = new FontFamily(fontFamily);
        Application.Current.Resources["AppFontFamily"] = font;
        Application.Current.Resources["ContentControlThemeFontFamily"] = font;
    }

    private static void EnsureChangedHook()
    {
        if (_changedHooked) return;
        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
        _changedHooked = true;
    }

    private static void OnApplicationThemeChanged(ApplicationTheme currentApplicationTheme, Color systemAccent)
    {
        ApplyPalette(currentApplicationTheme);
        ApplyBrandAccent();
        SyncWindowBackground(Application.Current?.MainWindow);
    }

    // Theme-dependent background / surface / text / button palette. Overrides Wpf.Ui's
    // theme brushes at the top resource level so they win over the merged theme
    // dictionary, and is re-applied on every theme switch so it stays sticky.
    private static void ApplyPalette(ApplicationTheme theme)
    {
        if (Application.Current is null) return;

        var res = Application.Current.Resources;
        bool dark = theme != ApplicationTheme.Light;

        // Standard Fluent Dark (#202020 / #2B2B2B) & Fluent Light (#F3F3F3 / #FFFFFF)
        var appBg        = dark ? "#202020" : "#F3F3F3";
        var cardBg       = dark ? "#2B2B2B" : "#FFFFFF";
        var cardBg2      = dark ? "#262626" : "#F9F9FB";
        var ctrlFill     = dark ? "#353535" : "#EAECEF";
        var ctrlFill2    = dark ? "#404040" : "#DFE2E7";
        var ctrlPressed  = dark ? "#2D2D2D" : "#D4D8DF";
        var ctrlDisabled = dark ? "#282828" : "#F0F1F4";
        var inputActive  = dark ? "#1E1E1E" : "#FFFFFF";
        var stroke       = dark ? "#1FFFFFFF" : "#1F000000";
        var elevBorder   = dark ? "#2EFFFFFF" : "#29000000";
        var textPri      = dark ? "#FFFFFF" : "#1A1A1F";
        var textSec      = dark ? "#C5C5C5" : "#5C616B";
        var textTer      = dark ? "#8A8A8A" : "#858A94";
        var textDis      = dark ? "#5D5D5D" : "#A0A5AE";

        var appBgColor = Hex(appBg);
        var appBgBrush = Freeze(appBgColor);
        var cardBrush = Freeze(Hex(cardBg));
        var card2Brush = Freeze(Hex(cardBg2));
        var ctrlBrush = Freeze(Hex(ctrlFill));
        var ctrl2Brush = Freeze(Hex(ctrlFill2));
        var ctrlPressedBrush = Freeze(Hex(ctrlPressed));
        var ctrlDisabledBrush = Freeze(Hex(ctrlDisabled));
        var inputActiveBrush = Freeze(Hex(inputActive));
        var strokeBrush = Freeze(Hex(stroke));
        var elevBorderBrush = Freeze(Hex(elevBorder));
        var textPriBrush = Freeze(Hex(textPri));
        var textSecBrush = Freeze(Hex(textSec));
        var textTerBrush = Freeze(Hex(textTer));
        var textDisBrush = Freeze(Hex(textDis));

        res["ApplicationBackgroundColor"] = appBgColor;
        res["ApplicationBackgroundBrush"] = appBgBrush;

        // Cards & Surfaces
        res["CardBackgroundFillColorDefaultBrush"]   = cardBrush;
        res["CardBackgroundFillColorSecondaryBrush"] = card2Brush;
        res["CardStrokeColorDefaultBrush"]           = strokeBrush;
        res["ContentDialogBackground"]               = cardBrush;
        res["HairlineBorderBrush"]                   = strokeBrush;

        // General Controls
        res["ControlFillColorDefaultBrush"]     = ctrlBrush;
        res["ControlFillColorSecondaryBrush"]   = ctrl2Brush;
        res["ControlFillColorTertiaryBrush"]    = ctrlPressedBrush;
        res["ControlFillColorDisabledBrush"]    = ctrlDisabledBrush;
        res["ControlFillColorInputActiveBrush"] = inputActiveBrush;
        res["ControlStrokeColorDefaultBrush"]   = strokeBrush;
        res["ControlStrokeColorSecondaryBrush"] = elevBorderBrush;
        res["ControlElevationBorderBrush"]      = elevBorderBrush;

        // Secondary / Standard Buttons (ui:Button default Appearance="Secondary")
        res["ButtonBackground"]            = ctrlBrush;
        res["ButtonBackgroundPointerOver"] = ctrl2Brush;
        res["ButtonBackgroundPressed"]     = ctrlPressedBrush;
        res["ButtonBackgroundDisabled"]    = ctrlDisabledBrush;
        res["ButtonForeground"]            = textPriBrush;
        res["ButtonForegroundPointerOver"] = textPriBrush;
        res["ButtonForegroundPressed"]     = textSecBrush;
        res["ButtonForegroundDisabled"]    = textDisBrush;
        res["ButtonBorderBrush"]           = elevBorderBrush;
        res["ButtonBorderBrushPointerOver"] = elevBorderBrush;
        res["ButtonBorderBrushPressed"]    = strokeBrush;
        res["ButtonBorderBrushDisabled"]   = strokeBrush;

        // TextBox / Input Controls
        res["TextControlBackground"]            = ctrlBrush;
        res["TextControlBackgroundPointerOver"] = ctrl2Brush;
        res["TextControlBackgroundFocused"]     = inputActiveBrush;
        res["TextControlForeground"]            = textPriBrush;
        res["TextControlForegroundPointerOver"] = textPriBrush;
        res["TextControlForegroundFocused"]     = textPriBrush;
        res["TextControlBorderBrush"]           = elevBorderBrush;
        res["TextControlBorderBrushPointerOver"] = elevBorderBrush;

        // Typography & Icons
        res["TextFillColorPrimaryBrush"]   = textPriBrush;
        res["TextFillColorSecondaryBrush"] = textSecBrush;
        res["TextFillColorTertiaryBrush"]  = textTerBrush;
        res["TextFillColorDisabledBrush"]  = textDisBrush;

        // NavigationView LeftFluent
        res["NavigationViewItemForegroundLeftFluent"]            = textSecBrush;
        res["NavigationViewItemForegroundPointerOverLeftFluent"] = textPriBrush;
        res["NavigationViewItemBackgroundSelectedLeftFluent"]    = ctrlBrush;
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

        var res = Application.Current.Resources;

        // Override Wpf.Ui accent brushes with our brand violet so all controls
        // (NavigationView selection indicator, primary buttons, etc.) use it.
        var accent      = Freeze(BrandAccent);
        var accentLight = Freeze(Color.FromRgb(0xA7, 0x8B, 0xFA));
        var accentDark  = Freeze(Color.FromRgb(0x6D, 0x28, 0xD9));
        var white       = Freeze(Colors.White);

        res["SystemAccentColor"]          = BrandAccent;
        res["SystemAccentColorPrimary"]   = BrandAccent;
        res["SystemAccentColorSecondary"] = Color.FromRgb(0xA7, 0x8B, 0xFA);
        res["SystemAccentColorTertiary"]  = Color.FromRgb(0x6D, 0x28, 0xD9);
        res["SystemAccentColorBrush"]     = accent;

        // Wpf.Ui layered accent brushes (used by NavigationView, Button primary, etc.)
        res["AccentFillColorDefaultBrush"]              = accent;
        res["AccentFillColorSecondaryBrush"]            = accentLight;
        res["AccentFillColorTertiaryBrush"]             = accentDark;
        res["AccentFillColorDisabledBrush"]             = accentDark;
        res["AccentTextFillColorPrimaryBrush"]          = accent;
        res["AccentTextFillColorSecondaryBrush"]        = accentLight;
        res["AccentTextFillColorTertiaryBrush"]         = accentDark;
        res["NavigationViewSelectionIndicatorForeground"] = accent;

        // Wpf.Ui Primary Button keys (controls/button/button.baml)
        res["AccentButtonBackground"]            = accent;
        res["AccentButtonBackgroundPointerOver"] = accentLight;
        res["AccentButtonBackgroundPressed"]     = accentDark;
        res["AccentButtonForeground"]            = white;
        res["AccentButtonForegroundPointerOver"] = white;
        res["AccentButtonForegroundPressed"]     = white;
        res["TextOnAccentFillColorPrimaryBrush"]   = white;
        res["TextOnAccentFillColorSecondaryBrush"] = white;

        // Legacy alias keys
        res["AccentButtonBackgroundBrush"]            = accent;
        res["AccentButtonBackgroundPointerOverBrush"] = accentLight;
        res["AccentButtonBackgroundPressedBrush"]     = accentDark;
    }

    /// <summary>
    /// Wpf.Ui's WindowBackgroundManager.UpdateBackground calls WindowBackdrop.RemoveBackground(window),
    /// which sets window.Background = Transparent and CompositionTarget.BackgroundColor = Transparent (#000000).
    /// On a WindowBackdropType="None" window, that renders as pure pitch black.
    /// This method restores the solid theme background on both the Window and its HwndSource.
    /// </summary>
    public static void SyncWindowBackground(Window? window)
    {
        if (window is null || Application.Current is null) return;

        if (Application.Current.Resources["ApplicationBackgroundBrush"] is SolidColorBrush bgBrush)
        {
            window.Resources["ApplicationBackgroundBrush"] = bgBrush;
            window.SetCurrentValue(Control.BackgroundProperty, bgBrush);

            if (PresentationSource.FromVisual(window) is HwndSource { CompositionTarget: not null } hwndSource)
            {
                hwndSource.CompositionTarget.BackgroundColor = bgBrush.Color;
            }
        }
    }

    private static ApplicationTheme ResolveSystemTheme() =>
        ApplicationThemeManager.GetSystemTheme() switch
        {
            SystemTheme.Light or SystemTheme.Sunrise or SystemTheme.Flow => ApplicationTheme.Light,
            _ => ApplicationTheme.Dark
        };

    /// <summary>
    /// Attaches SystemThemeWatcher after the window is shown (needs a HWND)
    /// and ensures the solid theme background &amp; palette remain intact.
    /// </summary>
    public static void AttachSystemWatcher(Window window)
    {
        EnsureChangedHook();
        if (!_watching)
        {
            SystemThemeWatcher.Watch(window, WindowBackdropType.None, updateAccents: false);
            _watching = true;
        }

        var applied = ResolveSystemTheme();
        ApplyPalette(applied);
        ApplyBrandAccent();
        SyncWindowBackground(window);
    }

    private static void StartWatching(Window? window)
    {
        if (window is null || _watching) return;
        SystemThemeWatcher.Watch(window, WindowBackdropType.None, updateAccents: false);
        _watching = true;
    }

    private static void StopWatching(Window? window)
    {
        if (window is null || !_watching) return;
        SystemThemeWatcher.UnWatch(window);
        _watching = false;
    }
}
