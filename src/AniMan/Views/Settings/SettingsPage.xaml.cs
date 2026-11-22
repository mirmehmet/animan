using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using AniMan.Core.Interfaces;
using AniMan.Localization;
using AniMan.ViewModels;
using AniMan.ViewModels.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace AniMan.Views.Settings;

public partial class SettingsPage : UserControl
{
    private readonly SettingsViewModel _viewModel;
    private readonly IContentDialogService _dialogService;
    private readonly BusyOverlayViewModel _busy;
    private readonly ILogger<SettingsPage> _logger;

    /// <summary>A very fast import would only flash the overlay; keep it up long enough to read.</summary>
    private static readonly TimeSpan MinimumOverlayTime = TimeSpan.FromMilliseconds(700);

    public SettingsPage(
        SettingsViewModel viewModel,
        IContentDialogService dialogService,
        BusyOverlayViewModel busy,
        ILogger<SettingsPage> logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _dialogService = dialogService;
        _busy = busy;
        _logger = logger;
        DataContext = viewModel;
        viewModel.LanguageChangeRequiresRestart += OnLanguageChangeRequiresRestart;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Settings page initialization failed");
        }
    }

    private async void OnLanguageChangeRequiresRestart(object? sender, EventArgs e)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = LocalizationManager.Get("Settings_RestartTitle"),
                Content = LocalizationManager.Get("Settings_RestartMessage"),
                CloseButtonText = LocalizationManager.Get("Common_Ok")
            };
            await _dialogService.ShowAsync(dialog, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Language restart notification dialog failed");
        }
    }

    // ── Backup / Restore ─────────────────────────────────────────────────────

    private async void OnExportClicked(object sender, RoutedEventArgs e)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Export workflow failed");
        }
    }

    private async void OnImportClicked(object sender, RoutedEventArgs e)
    {
        try
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

            var result = await RunImportWithProgressAsync(dlg.FileName, mode.Value);

            if (!result.IsSuccess)
            {
                await ShowMessageAsync(
                    LocalizationManager.Get("Settings_ImportErrorTitle"),
                    result.Error ?? string.Empty);
                return;
            }

            await ShowMessageAsync(
                LocalizationManager.Get("Settings_ImportRestartTitle"),
                BuildImportSummary(result.Value!));

            // Safely shut down, flush SQLite pools + Serilog, and launch the fresh instance.
            App.RequestRestart();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import workflow failed");
        }
    }

    /// <summary>
    /// Runs the import behind the window-wide progress overlay. Progress callbacks arrive
    /// on the UI thread (Progress&lt;T&gt; captures it here), so they can update the overlay directly.
    /// </summary>
    private async Task<Core.Common.Result<ImportSummary>> RunImportWithProgressAsync(string filePath, ImportMode mode)
    {
        var stopwatch = Stopwatch.StartNew();
        _busy.Show(
            LocalizationManager.Get("Settings_ImportProgressTitle"),
            LocalizationManager.Get("Settings_ImportStageReading"));
        try
        {
            var progress = new Progress<ImportProgress>(p => _busy.Update(DescribeStage(p), p.Fraction));
            var result = await _viewModel.ImportAsync(filePath, mode, progress);

            var remaining = MinimumOverlayTime - stopwatch.Elapsed;
            if (result.IsSuccess && remaining > TimeSpan.Zero)
                await Task.Delay(remaining);

            return result;
        }
        finally
        {
            _busy.Hide();
        }
    }

    private static string DescribeStage(ImportProgress p) => p.Stage switch
    {
        ImportStage.Reading => LocalizationManager.Get("Settings_ImportStageReading"),
        ImportStage.Saving => string.Format(LocalizationManager.Get("Settings_ImportStageSaving"), p.Done, p.Total),
        _ => string.Format(LocalizationManager.Get("Settings_ImportStageCovers"), p.Done, p.Total)
    };

    private static string BuildImportSummary(ImportSummary summary)
    {
        var lines = new List<string>
        {
            string.Format(LocalizationManager.Get("Settings_ImportSummaryImported"), summary.Imported)
        };
        if (summary.Skipped > 0)
            lines.Add(string.Format(LocalizationManager.Get("Settings_ImportSummarySkipped"), summary.Skipped));
        if (summary.CoversFailed > 0)
        {
            lines.Add(string.Format(LocalizationManager.Get("Settings_ImportSummaryCoversFailed"),
                summary.CoversFailed, LocalizationManager.Get("Detail_RefreshData")));
        }

        lines.Add(string.Empty);
        lines.Add(LocalizationManager.Get("Settings_ImportRestartContent"));
        return string.Join(Environment.NewLine, lines);
    }

    private async Task ShowMessageAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            // A bare string renders unwrapped and long Turkish lines get cut off.
            Content = new System.Windows.Controls.TextBlock { Text = content, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = LocalizationManager.Get("Common_Ok")
        };
        await _dialogService.ShowAsync(dialog, CancellationToken.None);
    }

    private async void OnEmptyTrashClicked(object sender, RoutedEventArgs e)
    {
        try
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
                await _viewModel.EmptyTrashCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Empty trash workflow failed");
        }
    }

    private async void OnDeleteAllDataClicked(object sender, RoutedEventArgs e)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delete all data workflow failed");
        }
    }
}
