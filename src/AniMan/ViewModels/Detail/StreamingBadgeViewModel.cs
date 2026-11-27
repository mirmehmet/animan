using System.Windows.Media;
using AniMan.Core.Common;

namespace AniMan.ViewModels.Detail;

public sealed class StreamingBadgeViewModel
{
    public string Name { get; init; } = "";
    public string? Url { get; init; }

    /// <summary>The user's own link (a streaming override), which may point into their LAN.</summary>
    public bool IsUserLink { get; init; }
    public SolidColorBrush Background { get; init; } = new(Colors.DimGray);

    /// <summary>
    /// Clicking would open something: the same check the browser launch makes. Without it the
    /// badge is a plain label, so it does not look clickable (AM-032).
    /// </summary>
    public bool HasLink => HttpUrlGuard.IsLaunchableWebUrl(Url, IsUserLink, out _);
}
