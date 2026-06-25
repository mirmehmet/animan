using AniTrack.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Appearance;

namespace AniTrack.ViewModels.Settings;

public partial class SettingsViewModel(ISettingsService settingsService) : ObservableObject
{
    [ObservableProperty]
    private bool _isDarkTheme = true;

    public async Task InitializeAsync()
    {
        var theme = await settingsService.GetThemeAsync();
        IsDarkTheme = theme != "light";
    }

    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        IsDarkTheme = !IsDarkTheme;
        var themeName = IsDarkTheme ? "dark" : "light";
        await settingsService.SetThemeAsync(themeName);

        var appTheme = IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(appTheme);
    }
}
