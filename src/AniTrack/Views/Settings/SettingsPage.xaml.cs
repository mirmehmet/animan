using AniTrack.Localization;
using AniTrack.ViewModels.Settings;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace AniTrack.Views.Settings;

public partial class SettingsPage : UserControl
{
    private readonly SettingsViewModel _viewModel;
    private readonly IContentDialogService _dialogService;

    public SettingsPage(SettingsViewModel viewModel, IContentDialogService dialogService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _dialogService = dialogService;
        DataContext = viewModel;
        viewModel.LanguageChangeRequiresRestart += OnLanguageChangeRequiresRestart;
        viewModel.ImportRequiresRestart += OnImportRequiresRestart;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) =>
        await _viewModel.InitializeAsync();

    private async void OnLanguageChangeRequiresRestart(object? sender, System.EventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizationManager.Get("Settings_RestartTitle"),
            Content = LocalizationManager.Get("Settings_RestartMessage"),
            CloseButtonText = LocalizationManager.Get("Common_Ok")
        };
        await _dialogService.ShowAsync(dialog, CancellationToken.None);
    }

    private async void OnImportRequiresRestart(object? sender, System.EventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizationManager.Get("Settings_ImportRestartTitle"),
            Content = LocalizationManager.Get("Settings_ImportRestartContent"),
            CloseButtonText = LocalizationManager.Get("Common_Ok")
        };
        await _dialogService.ShowAsync(dialog, CancellationToken.None);

        // Restart the application
        System.Windows.Application.Current.Shutdown();
        System.Diagnostics.Process.Start(
            System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!);
    }
}
