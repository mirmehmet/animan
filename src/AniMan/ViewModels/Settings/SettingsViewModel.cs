using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using AniMan.Core.Common;
using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Localization;
using AniMan.Theming;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AniMan.ViewModels.Settings;

public enum ThemeMode { Light, Dark, System }

public record StartupPageOption(string Key, string Display);

public partial class SettingsViewModel(
    ISettingsService settingsService,
    ITrackingService trackingService,
    IDataManagementService dataManagementService,
    IExportService exportService,
    ILogger<SettingsViewModel> logger) : ObservableObject
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

    public IReadOnlyList<StartupPageOption> StartupPageOptions { get; } =
    [
        new("Dashboard", LocalizationManager.Get("StartupPage_Dashboard")),
        new("Anime", LocalizationManager.Get("StartupPage_Anime")),
        new("Manga", LocalizationManager.Get("StartupPage_Manga")),
        new("Discover", LocalizationManager.Get("StartupPage_Discover"))
    ];

    [ObservableProperty] private StartupPageOption? _selectedStartupPageOption;

    // ── About ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Version shown in Settings, read from the MinVer-generated informational
    /// version so tagging a release is enough to update the UI. Untagged builds
    /// therefore show a pre-release suffix (e.g. "v0.2.3-alpha.0.4"), which is
    /// accurate — they are not the tagged release.
    /// </summary>
    public string AppVersion { get; } = ResolveAppVersion();

    private static string ResolveAppVersion()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
            return "v0.0.0";

        // .NET appends "+{commit sha}" to the attribute; that is build metadata, not version.
        var metadata = informational.IndexOf('+', StringComparison.Ordinal);
        return "v" + (metadata >= 0 ? informational[..metadata] : informational);
    }

    // ── Trash ─────────────────────────────────────────────────────────────────

    [ObservableProperty] private ObservableCollection<LibraryItem> _trashItems = [];
    [ObservableProperty] private bool _isTrashLoading;

    public bool HasTrashItems => TrashItems.Count > 0;

    // ── Init ──────────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        _initialized = false;
        try
        {
            var theme = await settingsService.GetThemeAsync();
            SelectedThemeMode = AppThemeManager.Parse(theme);

            var language = await settingsService.GetLanguageAsync();
            SelectedLanguage = Languages.FirstOrDefault(l => l.Code == language) ?? Languages[0];

            var startupKey = await settingsService.GetStartupPageAsync();
            SelectedStartupPageOption = StartupPageOptions.FirstOrDefault(
                o => string.Equals(o.Key, startupKey, StringComparison.OrdinalIgnoreCase)) ?? StartupPageOptions[0];

            SelectedFont = await settingsService.GetFontAsync();

            await RefreshLastBackupAsync();
            await LoadTrashAsync();
        }
        finally
        {
            _initialized = true;
        }
    }

    // ── Backup ────────────────────────────────────────────────────────────────

    /// <summary>"Last backup: …" under the export button, in the UI language and local time.</summary>
    [ObservableProperty] private string _lastBackupText = string.Empty;

    /// <summary>Re-reads the time of the last export (written by the export service on success).</summary>
    public async Task RefreshLastBackupAsync()
    {
        var stored = await settingsService.GetAsync("LastBackupAt");
        LastBackupText = DateTime.TryParse(stored, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var at)
            ? string.Format(LocalizationManager.Culture, LocalizationManager.Get("Settings_LastBackup"),
                DateTime.SpecifyKind(at, DateTimeKind.Utc).ToLocalTime().ToString("g", LocalizationManager.Culture))
            : LocalizationManager.Get("Settings_LastBackupNever");
    }

    // ── Theme ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Only the theme buttons call this, so it runs on a click, never while the page is
    /// being filled. Clicking the theme already in use changes nothing (AM-069: the old
    /// check skipped it only before initialization, the wrong way round).
    /// </summary>
    [RelayCommand]
    private async Task SetThemeModeAsync(ThemeMode mode)
    {
        if (mode == SelectedThemeMode) return;

        SelectedThemeMode = mode;
        try
        {
            AppThemeManager.Apply(mode);
            await settingsService.SetThemeAsync(AppThemeManager.ToStored(mode));
        }
        catch (Exception ex)
        {
            // Like the font and language settings: log it; the theme still applies for this session.
            logger.LogError(ex, "Failed to apply or save theme {Mode}", mode);
        }
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
            logger.LogError(ex, "Failed to save language setting");
        }
    }

    // ── Startup page ──────────────────────────────────────────────────────────

    async partial void OnSelectedStartupPageOptionChanged(StartupPageOption? value)
    {
        if (!_initialized || value is null) return;
        try
        {
            await settingsService.SetStartupPageAsync(value.Key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save startup page setting");
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
            logger.LogError(ex, "Failed to save font setting");
        }
    }

    // ── Backup / Restore ──────────────────────────────────────────────────────

    public async Task<Result> ExportAsync(string filePath)
    {
        var result = await exportService.ExportAsync(filePath);
        if (result.IsSuccess)
            await RefreshLastBackupAsync();
        return result;
    }

    /// <summary>Every title a replace-import would delete: anime, manga and the trash.</summary>
    public async Task<int> CountLibraryItemsAsync()
    {
        var count = await trackingService.CountAllItemsAsync();
        return count.IsSuccess ? count.Value : 0;
    }

    public Task<Result<ImportSummary>> ImportAsync(
        string filePath, ImportMode mode, IProgress<ImportProgress>? progress = null) =>
        exportService.ImportAsync(filePath, mode, progress);

    // ── Trash management ─────────────────────────────────────────────────────

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

    /// <summary>Raised when restoring an item from the trash fails; the page tells the user why.</summary>
    public event EventHandler<ErrorKind>? RestoreFailed;

    [RelayCommand]
    private async Task RestoreItemAsync(LibraryItem item)
    {
        var result = await trackingService.RestoreFromTrashAsync(item.Id);
        if (result.IsSuccess)
        {
            TrashItems.Remove(item);
            OnPropertyChanged(nameof(HasTrashItems));
        }
        else
        {
            RestoreFailed?.Invoke(this, result.Kind);
        }
    }

    public async Task<Result> EmptyTrashAsync()
    {
        var result = await trackingService.EmptyTrashAsync();
        if (result.IsSuccess)
        {
            TrashItems.Clear();
            OnPropertyChanged(nameof(HasTrashItems));
        }
        return result;
    }

    partial void OnTrashItemsChanged(ObservableCollection<LibraryItem> value) =>
        OnPropertyChanged(nameof(HasTrashItems));

    // ── Data reset ────────────────────────────────────────────────────

    public async Task<Result> DeleteAllDataAsync()
    {
        var result = await dataManagementService.ResetAllDataAsync();
        if (result.IsSuccess)
        {
            TrashItems.Clear();
            OnPropertyChanged(nameof(HasTrashItems));
        }
        await RefreshLastBackupAsync(); // the reset clears the stored backup time
        return result;
    }
}
