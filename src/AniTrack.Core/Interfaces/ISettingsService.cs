namespace AniTrack.Core.Interfaces;

public interface ISettingsService
{
    Task<string> GetAsync(string key, string defaultValue = "");
    Task SetAsync(string key, string value);
    Task<string> GetThemeAsync();
    Task SetThemeAsync(string theme);
    Task<string> GetLanguageAsync();
    Task<int> GetCacheRefreshDaysAsync();
}
