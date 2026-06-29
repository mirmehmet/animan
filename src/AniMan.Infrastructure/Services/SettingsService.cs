using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AniMan.Infrastructure.Services;

public class SettingsService(IDbContextFactory<LibraryDbContext> dbFactory) : ISettingsService
{
    public async Task<string> GetAsync(string key, string defaultValue = "")
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var setting = await db.Settings.FindAsync(key);
        return setting?.Value ?? defaultValue;
    }

    public async Task SetAsync(string key, string value)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var existing = await db.Settings.FindAsync(key);
        if (existing is null)
            db.Settings.Add(new AppSetting { Key = key, Value = value });
        else
            existing.Value = value;
        await db.SaveChangesAsync();
    }

    public Task<string> GetThemeAsync() => GetAsync("Theme", "system");
    public Task SetThemeAsync(string theme) => SetAsync("Theme", theme);
    public Task<string> GetLanguageAsync() => GetAsync("Language", "en");
    public Task SetLanguageAsync(string language) => SetAsync("Language", language);
    public async Task<int> GetCacheRefreshDaysAsync()
    {
        var val = await GetAsync("CacheRefreshDays", "7");
        return int.TryParse(val, out var days) ? days : 7;
    }

    public Task<string> GetStartupPageAsync() => GetAsync("StartupPage", "Dashboard");
    public Task SetStartupPageAsync(string page) => SetAsync("StartupPage", page);
    public Task<string> GetFontAsync() => GetAsync("FontFamily", "Segoe UI");
    public Task SetFontAsync(string fontFamily) => SetAsync("FontFamily", fontFamily);
}
