using System.Windows;
using Wpf.Ui.Appearance;
using ThemeMode = AniTrack.ViewModels.Settings.ThemeMode;

namespace AniTrack.Theming;

/// <summary>
/// Applies the selected <see cref="ThemeMode"/> and, for <see cref="ThemeMode.System"/>,
/// keeps the app theme live-synced with the Windows theme via <see cref="SystemThemeWatcher"/>.
/// </summary>
public static class AppThemeManager
{
    private static bool _watching;

    /// <summary>Maps a stored string ("light"/"dark"/"system") to a <see cref="ThemeMode"/>.</summary>
    public static ThemeMode Parse(string? stored) => stored switch
    {
        "light"  => ThemeMode.Light,
        "system" => ThemeMode.System,
        _        => ThemeMode.Dark
    };

    /// <summary>Serializes a <see cref="ThemeMode"/> to its stored string form.</summary>
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
    }

    private static ApplicationTheme ResolveSystemTheme() =>
        ApplicationThemeManager.GetSystemTheme() switch
        {
            SystemTheme.Light or SystemTheme.Glow or SystemTheme.CapturedMotion
                or SystemTheme.Sunrise or SystemTheme.Flow => ApplicationTheme.Light,
            _ => ApplicationTheme.Dark
        };

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
