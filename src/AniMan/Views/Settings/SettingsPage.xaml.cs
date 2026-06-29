using AniMan.Localization;
using AniMan.ViewModels.Settings;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace AniMan.Views.Settings;

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

    private async void OnEmptyTrashClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = LocalizationManager.Get("Settings_EmptyTrashTitle"),
            Content = LocalizationManager.Get("Settings_EmptyTrashContent"),
            PrimaryButtonText = LocalizationManager.Get("Settings_EmptyTrashConfirm"),
            CloseButtonText = LocalizationManager.Get("Common_Cancel")
        };

        var result = await _dialogService.ShowAsync(dialog, CancellationToken.None);
        if (result == ContentDialogResult.Primary)
            _viewModel.EmptyTrashCommand.Execute(null);
    }

    private async void OnDeleteAllDataClicked(object sender, RoutedEventArgs e)
    {
        var word = ConfirmationWords.GetRandom();
        var content = new DeleteConfirmationContent(word);

        var dialog = new ContentDialog
        {
            Title = LocalizationManager.Get("Settings_DeleteAllDataTitle"),
            Content = content,
            PrimaryButtonText = LocalizationManager.Get("Settings_DeleteAllDataConfirm"),
            CloseButtonText = LocalizationManager.Get("Common_Cancel"),
            IsPrimaryButtonEnabled = false
        };

        content.ConfirmTextChanged += text =>
            dialog.IsPrimaryButtonEnabled = string.Equals(text, word, StringComparison.Ordinal);

        var result = await _dialogService.ShowAsync(dialog, CancellationToken.None);
        if (result != ContentDialogResult.Primary) return;

        await _viewModel.DeleteAllDataCommand.ExecuteAsync(null);

        var success = new ContentDialog
        {
            Title = LocalizationManager.Get("Settings_DeleteAllDataSuccessTitle"),
            Content = LocalizationManager.Get("Settings_DeleteAllDataSuccessContent"),
            CloseButtonText = LocalizationManager.Get("Common_Ok")
        };
        await _dialogService.ShowAsync(success, CancellationToken.None);
    }
}
