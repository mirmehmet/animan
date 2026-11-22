using System.Windows.Media;
using AniMan.Core.Domain;
using MediaColor = System.Windows.Media.Color;

namespace AniMan.ViewModels.Stats;

/// <summary>A headline figure: big value, label, optional context line.</summary>
public sealed record StatTileViewModel(string Label, string Value, string? Sub)
{
    public bool HasSub => !string.IsNullOrEmpty(Sub);
}

/// <summary>One status block of the segmented bar; <see cref="Weight"/> is its title count.</summary>
public sealed record StatusSegmentViewModel(int StatusId, string Name, int Count, Brush Brush, string Tooltip)
{
    public double Weight => Count;
}

/// <summary>
/// A column of a column chart (scores, release years). <see cref="BarHeight"/> is in pixels;
/// a year column carries the actual year range it opens in the library.
/// </summary>
public sealed record ColumnViewModel(
    string Label, int Count, double BarHeight, bool IsEmphasis, string Tooltip,
    int YearFrom = 0, int YearTo = 0)
{
    public bool IsClickable => YearFrom > 0 && Count > 0;
}

/// <summary>A ranked horizontal bar: <see cref="Fill"/> + <see cref="Rest"/> = 1.</summary>
public sealed record RankedBarViewModel(string Name, int Count, double Fill, string Tooltip)
{
    public double Rest => 1 - Fill;
}

public sealed record TasteRowViewModel(
    string Genre, double You, double Mal, double AxisMin, double AxisMax, string YouText, string Tooltip);

/// <param name="IsInRange">Days outside the period pad the grid to whole weeks and stay invisible.</param>
/// <param name="Opacity">0 = no activity (only the empty cell shows), up to 1 for the busiest days.</param>
public sealed record HeatDayViewModel(bool IsInRange, double Opacity, string? Tooltip)
{
    public double CellOpacity => IsInRange ? 1 : 0;
}

/// <param name="MonthLabel">Month name on the week a month starts in; empty otherwise.</param>
public sealed record HeatWeekViewModel(string MonthLabel, IReadOnlyList<HeatDayViewModel> Days);

/// <summary>Period menu entry; <see cref="Year"/> 0 means "last 12 months".</summary>
public sealed record PeriodOption(int Year, string Label);

public sealed record PeriodFigureViewModel(string Label, string Value);

/// <summary>Status colors shared by the status chart: the DB color when set, else a fixed map.</summary>
public static class StatusColors
{
    public static Brush For(int statusId, string? hex)
    {
        MediaColor color;
        try
        {
            color = string.IsNullOrWhiteSpace(hex) ? Default(statusId) : (MediaColor)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            color = Default(statusId);
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Order statuses read in: in progress, done, paused, dropped, planned, custom.</summary>
    public static int DisplayRank(int statusId) => statusId switch
    {
        TrackingStatusIds.Watching or TrackingStatusIds.Reading => 0,
        TrackingStatusIds.Completed => 1,
        TrackingStatusIds.OnHold => 2,
        TrackingStatusIds.Dropped => 3,
        TrackingStatusIds.PlanToWatch or TrackingStatusIds.PlanToRead => 4,
        _ => 5
    };

    // Validated with the dataviz palette checker in both themes, in display order
    // (violet, aqua, yellow, red, blue): lightness band, chroma, colour-blind separation
    // >= 8 ΔE and normal-vision >= 15 ΔE between neighbours. Yellow sits at 2.99:1 on the
    // light surface, so the chart always shows the legend with names and counts.
    // These brushes do not switch with the theme, so one set has to pass both.
    private static MediaColor Default(int statusId) => statusId switch
    {
        TrackingStatusIds.Completed => MediaColor.FromRgb(0x1B, 0xA6, 0x75),   // aqua
        TrackingStatusIds.OnHold => MediaColor.FromRgb(0xC9, 0x85, 0x00),      // yellow
        TrackingStatusIds.Dropped => MediaColor.FromRgb(0xDC, 0x4F, 0x6E),     // red
        TrackingStatusIds.PlanToWatch or TrackingStatusIds.PlanToRead => MediaColor.FromRgb(0x31, 0x80, 0xDD), // blue
        _ => MediaColor.FromRgb(0x8B, 0x5C, 0xF6)                               // violet: in progress / custom
    };
}
