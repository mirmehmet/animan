using System.Windows.Media;

namespace AniMan.ViewModels.Detail;

public sealed class StreamingBadgeViewModel
{
    public string Name { get; init; } = "";
    public string? Url { get; init; }

    /// <summary>The user's own link (a streaming override), which may point into their LAN.</summary>
    public bool IsUserLink { get; init; }
    public SolidColorBrush Background { get; init; } = new(Colors.DimGray);
}
