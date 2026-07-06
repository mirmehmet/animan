namespace AniMan.Core.Interfaces;

public interface ISettingsService
{
    Task<string> GetAsync(string key, string defaultValue = "", CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task<string> GetThemeAsync(CancellationToken ct = default);
    Task SetThemeAsync(string theme, CancellationToken ct = default);
    Task<string> GetLanguageAsync(CancellationToken ct = default);
    Task SetLanguageAsync(string language, CancellationToken ct = default);
    Task<int> GetCacheRefreshDaysAsync(CancellationToken ct = default);
    Task<string> GetStartupPageAsync(CancellationToken ct = default);
    Task SetStartupPageAsync(string page, CancellationToken ct = default);
    Task<string> GetFontAsync(CancellationToken ct = default);
    Task SetFontAsync(string fontFamily, CancellationToken ct = default);
}
