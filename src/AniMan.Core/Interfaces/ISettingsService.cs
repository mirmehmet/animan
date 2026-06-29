namespace AniMan.Core.Interfaces;

public interface ISettingsService
{
    Task<string> GetAsync(string key, string defaultValue = "");
    Task SetAsync(string key, string value);
    Task<string> GetThemeAsync();
    Task SetThemeAsync(string theme);
    Task<string> GetLanguageAsync();
    Task SetLanguageAsync(string language);
    Task<int> GetCacheRefreshDaysAsync();
    Task<string> GetStartupPageAsync();
    Task SetStartupPageAsync(string page);
    Task<string> GetFontAsync();
    Task SetFontAsync(string fontFamily);
}
