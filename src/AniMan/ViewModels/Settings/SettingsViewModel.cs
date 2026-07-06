using System.Collections.ObjectModel;
using AniMan.Core.Common;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Localization;
using AniMan.Theming;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniMan.ViewModels.Settings;

public enum ThemeMode { Light, Dark, System }

public partial class SettingsViewModel(
    ISettingsService settingsService,
    ITrackingService trackingService,
    IDataManagementService dataManagementService,
    IExportService exportService) : ObservableObject
{
    private bool _initialized;

    public event EventHandler? LanguageChangeRequiresRestart;

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

    // async void semantics below: a failed settings write must be logged, never
    // allowed to escape to the dispatcher.
    async partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (!_initialized || value is null) return;
        try
        {
            var current = await settingsService.GetLanguageAsync();
            if (current == value.Code) return;
            await settingsService.SetLanguageAsync(value.Code);
            LanguageChangeRequiresRestart?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save language setting");
        }
    }

    // ── Startup page ──────────────────────────────────────────────────────────

    async partial void OnSelectedStartupPageChanged(string value)
    {
        if (!_initialized) return;
        try
        {
            await settingsService.SetStartupPageAsync(value);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save startup page setting");
        }
    }

    async partial void OnSelectedFontChanged(string value)
    {
        if (!_initialized) return;
        try
        {
            AppThemeManager.ApplyFont(value);
            await settingsService.SetFontAsync(value);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save font setting");
        }
    }

    // ── Backup / Restore ──────────────────────────────────────────────────────

    public Task<Result> ExportAsync(string filePath) =>
        exportService.ExportAsync(filePath);

    public Task<Result> ImportAsync(string filePath, ImportMode mode) =>
        exportService.ImportAsync(filePath, mode);

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
