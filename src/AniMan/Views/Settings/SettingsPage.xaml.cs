using AniMan.Core.Interfaces;
using AniMan.Localization;
using AniMan.ViewModels.Settings;
using Microsoft.Win32;
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

    // ── Backup / Restore ─────────────────────────────────────────────────────

    private async void OnExportClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = LocalizationManager.Get("Settings_Export"),
            Filter = "AniMan Backup (*.AniMan)|*.AniMan",
            DefaultExt = ".AniMan",
            AddExtension = true,
            FileName = $"AniMan-backup-{DateTime.Today:yyyy-MM-dd}"
        };
        if (dlg.ShowDialog() != true) return;

        var result = await _viewModel.ExportAsync(dlg.FileName);

        if (result.IsSuccess)
        {
            await ShowMessageAsync(
                LocalizationManager.Get("Settings_ExportSuccessTitle"),
                LocalizationManager.Get("Settings_ExportSuccessContent"));
        }
        else
        {
            await ShowMessageAsync(
                LocalizationManager.Get("Settings_ExportErrorTitle"),
                result.Error ?? string.Empty);
        }
    }

    private async void OnImportClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = LocalizationManager.Get("Settings_Import"),
            Filter = "AniMan Backup (*.AniMan)|*.AniMan"
        };
        if (dlg.ShowDialog() != true) return;

        var modeDialog = new ContentDialog
        {
            Title = LocalizationManager.Get("Settings_ImportModeTitle"),
            Content = LocalizationManager.Get("Settings_ImportModeContent"),
            PrimaryButtonText = LocalizationManager.Get("Settings_ImportModeMerge"),
            SecondaryButtonText = LocalizationManager.Get("Settings_ImportModeOverwrite"),
            CloseButtonText = LocalizationManager.Get("Common_Cancel")
        };

        var choice = await _dialogService.ShowAsync(modeDialog, CancellationToken.None);
        var mode = choice switch
        {
            ContentDialogResult.Primary => ImportMode.Merge,
            ContentDialogResult.Secondary => ImportMode.Overwrite,
            _ => (ImportMode?)null
        };
        if (mode is null) return;

        var result = await _viewModel.ImportAsync(dlg.FileName, mode.Value);

        if (!result.IsSuccess)
        {
            await ShowMessageAsync(
                LocalizationManager.Get("Settings_ImportErrorTitle"),
                result.Error ?? string.Empty);
            return;
        }

        await ShowMessageAsync(
            LocalizationManager.Get("Settings_ImportRestartTitle"),
            LocalizationManager.Get("Settings_ImportRestartContent"));

        // Restart so every open page reloads from the imported library.
        System.Windows.Application.Current.Shutdown();
        System.Diagnostics.Process.Start(
            System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!);
    }

    private async Task ShowMessageAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = LocalizationManager.Get("Common_Ok")
        };
        await _dialogService.ShowAsync(dialog, CancellationToken.None);
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
