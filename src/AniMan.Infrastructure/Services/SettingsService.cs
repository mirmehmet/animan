using AniMan.Core.Domain.Models;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure.Services;

public class SettingsService(
    IDbContextFactory<LibraryDbContext> dbFactory,
    ILogger<SettingsService> logger) : ISettingsService
{
    public async Task<string> GetAsync(string key, string defaultValue = "", CancellationToken ct = default)
    {
        // Read failures fall back to the default — settings reads run during
        // startup and must never prevent the app from launching.
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var setting = await db.Settings.FindAsync([key], ct).ConfigureAwait(false);
            return setting?.Value ?? defaultValue;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read setting {Key}, using default", key);
            return defaultValue;
        }
    }

    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var existing = await db.Settings.FindAsync([key], ct).ConfigureAwait(false);
        if (existing is null)
            db.Settings.Add(new AppSetting { Key = key, Value = value });
        else
            existing.Value = value;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public Task<string> GetThemeAsync(CancellationToken ct = default) => GetAsync("Theme", "system", ct);
    public Task SetThemeAsync(string theme, CancellationToken ct = default) => SetAsync("Theme", theme, ct);
    public Task<string> GetLanguageAsync(CancellationToken ct = default) => GetAsync("Language", "en", ct);
    public Task SetLanguageAsync(string language, CancellationToken ct = default) => SetAsync("Language", language, ct);
    public async Task<int> GetCacheRefreshDaysAsync(CancellationToken ct = default)
    {
        var val = await GetAsync("CacheRefreshDays", "7", ct).ConfigureAwait(false);
        return int.TryParse(val, out var days) ? days : 7;
    }

    public Task<string> GetStartupPageAsync(CancellationToken ct = default) => GetAsync("StartupPage", "Dashboard", ct);
    public Task SetStartupPageAsync(string page, CancellationToken ct = default) => SetAsync("StartupPage", page, ct);
    public Task<string> GetFontAsync(CancellationToken ct = default) => GetAsync("FontFamily", "Segoe UI", ct);
    public Task SetFontAsync(string fontFamily, CancellationToken ct = default) => SetAsync("FontFamily", fontFamily, ct);
}
