using AniMan.Core.Interfaces;
using Serilog;

namespace AniMan.ViewModels;

/// <summary>
/// Saves a UI preference without making the caller wait. A failed write is logged instead of
/// disappearing as an unobserved task exception; the preference simply is not remembered.
/// </summary>
internal static class SettingWriter
{
    public static async void SaveInBackground(this ISettingsService settings, string key, string value)
    {
        try
        {
            await settings.SetAsync(key, value);
        }
        catch (Exception ex)
        {
            Log.ForContext(typeof(SettingWriter)).Warning(ex, "Could not save setting {Key}", key);
        }
    }
}
