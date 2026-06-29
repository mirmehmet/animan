using System.Collections.ObjectModel;
using System.IO;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Localization;
using AniMan.Theming;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace AniMan.ViewModels.Settings;

public enum ThemeMode { Light, Dark, System }

public partial class SettingsViewModel(
    ISettingsService settingsService,
    ITrackingService trackingService,
    IDataManagementService dataManagementService) : ObservableObject
{
    private bool _initialized;
    private static readonly string LibraryDbPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AniMan", "library.db");

    public event EventHandler? LanguageChangeRequiresRestart;
    public event EventHandler? ImportRequiresRestart;

    // ── Appearance ────────────────────────────────────────────────────────────

    [ObservableProperty] private ThemeMode _selectedThemeMode = ThemeMode.System;
    public IReadOnlyList<LanguageOption> Languages => LocalizationManager.SupportedLanguages;
    [ObservableProperty] private LanguageOption? _selectedLanguage;

    // 10 telif-güvenli Windows/OFL fontları
    public IReadOnlyList<string> FontOptions { get; } =
    [
        "Segoe UI",
        "Segoe UI Variable",
        "Calibri",
        "Trebuchet MS",
        "Comic Sans MS",
        "Georgia",
        "Verdana",
        "Tahoma",
        "Arial",
        "Consolas"
    ];

    [ObservableProperty] private string _selectedFont = "Segoe UI";

    // ── Startup page ──────────────────────────────────────────────────────────

    public IReadOnlyList<string> StartupPageOptions { get; } =
        ["Dashboard", "Anime", "Manga", "Discover"];

    [ObservableProperty] private string _selectedStartupPage = "Dashboard";

    // ── Trash ─────────────────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<LibraryItem> _trashItems = [];
    [ObservableProperty] private bool _isTrashLoading;

    public bool HasTrashItems => TrashItems.Count > 0;

    // ── Init ──────────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        var theme = await settingsService.GetThemeAsync();
        SelectedThemeMode = AppThemeManager.Parse(theme);

        var language = await settingsService.GetLanguageAsync();
        SelectedLanguage = Languages.FirstOrDefault(l => l.Code == language) ?? Languages[0];

        SelectedStartupPage = await settingsService.GetStartupPageAsync();

        SelectedFont = await settingsService.GetFontAsync();

        await LoadTrashAsync();
        _initialized = true;
    }

    // ── Theme ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SetThemeModeAsync(ThemeMode mode)
    {
        if (!_initialized && mode == SelectedThemeMode) return;

        SelectedThemeMode = mode;
        AppThemeManager.Apply(mode);
        await settingsService.SetThemeAsync(AppThemeManager.ToStored(mode));
    }

    async partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (!_initialized || value is null) return;
        var current = await settingsService.GetLanguageAsync();
        if (current == value.Code) return;
        await settingsService.SetLanguageAsync(value.Code);
        LanguageChangeRequiresRestart?.Invoke(this, EventArgs.Empty);
    }

    // ── Startup page ──────────────────────────────────────────────────────────

    async partial void OnSelectedStartupPageChanged(string value)
    {
        if (!_initialized) return;
        await settingsService.SetStartupPageAsync(value);
    }

    async partial void OnSelectedFontChanged(string value)
    {
        if (!_initialized) return;
        AppThemeManager.ApplyFont(value);
        await settingsService.SetFontAsync(value);
    }

    // ── Backup / Restore ──────────────────────────────────────────────────────

    [RelayCommand]
    private void ExportData()
    {
        var dlg = new SaveFileDialog
        {
            Title = LocalizationManager.Get("Settings_Export"),
            Filter = "AniMan Backup (*.AniMan)|*.AniMan|SQLite DB (*.db)|*.db",
            FileName = $"AniMan-backup-{DateTime.Today:yyyy-MM-dd}"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            File.Copy(LibraryDbPath, dlg.FileName, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Export failed: {ex.Message}",
                "Export Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ImportData()
    {
        var dlg = new OpenFileDialog
        {
            Title = LocalizationManager.Get("Settings_Import"),
            Filter = "AniMan Backup (*.AniMan;*.db)|*.AniMan;*.db"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            File.Copy(dlg.FileName, LibraryDbPath, overwrite: true);
            ImportRequiresRestart?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Import failed: {ex.Message}",
                "Import Error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    // ── Trash management ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadTrashAsync()
    {
        IsTrashLoading = true;
        try
        {
            var result = await trackingService.GetTrashAsync();
            if (result.IsSuccess)
                TrashItems = new ObservableCollection<LibraryItem>(result.Value!);
        }
        finally { IsTrashLoading = false; }
    }

    [RelayCommand]
    private async Task RestoreItemAsync(LibraryItem item)
    {
        var result = await trackingService.RestoreFromTrashAsync(item.Id);
        if (result.IsSuccess)
        {
            TrashItems.Remove(item);
            OnPropertyChanged(nameof(HasTrashItems));
        }
    }

    [RelayCommand]
    private async Task EmptyTrashAsync()
    {
        var result = await trackingService.EmptyTrashAsync();
        if (result.IsSuccess)
        {
            TrashItems.Clear();
            OnPropertyChanged(nameof(HasTrashItems));
        }
    }

    partial void OnTrashItemsChanged(ObservableCollection<LibraryItem> value) =>
        OnPropertyChanged(nameof(HasTrashItems));

    // ── Data reset ────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task DeleteAllDataAsync()
    {
        await dataManagementService.ResetAllDataAsync();
        TrashItems.Clear();
        OnPropertyChanged(nameof(HasTrashItems));
    }
}
