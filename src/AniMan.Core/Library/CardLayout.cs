namespace AniMan.Core.Library;

/// <summary>How much a library card shows besides its cover.</summary>
public enum CardDetail
{
    /// <summary>Cover and a thin progress line; title and status appear on hover.</summary>
    Compact,

    /// <summary>Cover, status badge, title, progress text and bar.</summary>
    Standard,

    /// <summary>Standard plus a two-line title and the MAL score.</summary>
    Detailed
}

/// <summary>
/// The library cover-size slider: a few fixed steps rather than free sizes, so covers are
/// decoded at a handful of resolutions and memory stays predictable.
/// </summary>
public static class CardSizes
{
    /// <summary>Card widths in device-independent pixels, smallest first.</summary>
    public static IReadOnlyList<int> Widths { get; } = [110, 130, 150, 180, 220];

    /// <summary>150 px: the size the library always had.</summary>
    public const int DefaultStep = 2;

    public static int MaxStep => Widths.Count - 1;

    public static int ClampStep(int step) => Math.Clamp(step, 0, MaxStep);

    public static int WidthOf(int step) => Widths[ClampStep(step)];

    /// <summary>Covers keep the library's 3:4 frame.</summary>
    public static double CoverHeightOf(double width) => Math.Round(width * 4 / 3);

    public static CardDetail DetailOf(int step) => ClampStep(step) switch
    {
        <= 1 => CardDetail.Compact,
        2 => CardDetail.Standard,
        _ => CardDetail.Detailed
    };
}

/// <summary>
/// Column count and spacing for a grid of fixed-width items that spans the full width:
/// as many columns as fit with at least <c>minGap</c> between them, with the leftover space
/// shared equally between the gaps so both edges line up.
/// </summary>
public static class JustifiedLayout
{
    public static (int Columns, double Gap) Compute(double availableWidth, double itemWidth, double minGap)
    {
        if (itemWidth <= 0 || double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) ||
            availableWidth < itemWidth * 2 + minGap)
        {
            return (1, 0);
        }

        var columns = (int)Math.Floor((availableWidth + minGap) / (itemWidth + minGap));
        var gap = (availableWidth - columns * itemWidth) / (columns - 1);
        return (columns, gap);
    }
}
