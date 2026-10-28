using AniMan.Core.Domain;

namespace AniMan.Localization;

public static class TrackingStatusDisplay
{
    public static string GetDisplayName(int statusId, string? fallbackName = null) => statusId switch
    {
        TrackingStatusIds.Watching    => LocalizationManager.Get("Status_Watching"),
        TrackingStatusIds.Reading     => LocalizationManager.Get("Status_Reading"),
        TrackingStatusIds.Completed   => LocalizationManager.Get("Status_Completed"),
        TrackingStatusIds.OnHold      => LocalizationManager.Get("Status_OnHold"),
        TrackingStatusIds.Dropped     => LocalizationManager.Get("Status_Dropped"),
        TrackingStatusIds.PlanToWatch => LocalizationManager.Get("Status_PlanToWatch"),
        TrackingStatusIds.PlanToRead  => LocalizationManager.Get("Status_PlanToRead"),
        _ => !string.IsNullOrWhiteSpace(fallbackName) ? fallbackName : statusId.ToString()
    };
}

