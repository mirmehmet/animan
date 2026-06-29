using System.Windows.Media;

namespace AniTrack.ViewModels.Detail;

public sealed class StreamingBadgeViewModel
{
    public string Name { get; init; } = "";
    public string? Url { get; init; }
    public SolidColorBrush Background { get; init; } = new(Colors.DimGray);
}
