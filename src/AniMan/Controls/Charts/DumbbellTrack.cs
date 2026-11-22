using System.Windows;
using System.Windows.Media;

namespace AniMan.Controls.Charts;

/// <summary>
/// One row of the "you vs MAL" chart: a faint track spanning <see cref="Minimum"/>–
/// <see cref="Maximum"/>, a connector between the two scores, a filled dot for the user's
/// mean (<see cref="Value"/>) and a hollow dot for MyAnimeList's (<see cref="Reference"/>).
/// Drawn directly in OnRender — a handful of primitives per row, no visual tree.
/// </summary>
public sealed class DumbbellTrack : FrameworkElement
{
    private const double DotRadius = 5;

    public static readonly DependencyProperty MinimumProperty = Register(nameof(Minimum), 5.0);
    public static readonly DependencyProperty MaximumProperty = Register(nameof(Maximum), 10.0);
    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), 0.0);
    public static readonly DependencyProperty ReferenceProperty = Register(nameof(Reference), 0.0);

    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(DumbbellTrack),
            new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(DumbbellTrack),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Fill behind the hollow dot, so the track does not show through it.</summary>
    public static readonly DependencyProperty SurfaceBrushProperty =
        DependencyProperty.Register(nameof(SurfaceBrush), typeof(Brush), typeof(DumbbellTrack),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Reference { get => (double)GetValue(ReferenceProperty); set => SetValue(ReferenceProperty, value); }
    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush SurfaceBrush { get => (Brush)GetValue(SurfaceBrushProperty); set => SetValue(SurfaceBrushProperty, value); }

    private static DependencyProperty Register(string name, double defaultValue) =>
        DependencyProperty.Register(name, typeof(double), typeof(DumbbellTrack),
            new FrameworkPropertyMetadata(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender));

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, DotRadius * 2 + 4);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth - DotRadius * 2;
        if (width <= 0 || Maximum <= Minimum) return;

        var y = ActualHeight / 2;
        double X(double v) => DotRadius + width * Math.Clamp((v - Minimum) / (Maximum - Minimum), 0, 1);

        var track = new Pen(TrackBrush, 1);
        track.Freeze();
        drawingContext.DrawLine(track, new Point(DotRadius, y), new Point(DotRadius + width, y));

        var xYou = X(Value);
        var xMal = X(Reference);
        var connector = new Pen(AccentBrush, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        connector.Freeze();
        drawingContext.DrawLine(connector, new Point(xMal, y), new Point(xYou, y));

        var ring = new Pen(AccentBrush, 2);
        ring.Freeze();
        drawingContext.DrawEllipse(SurfaceBrush, ring, new Point(xMal, y), DotRadius - 1, DotRadius - 1);
        drawingContext.DrawEllipse(AccentBrush, null, new Point(xYou, y), DotRadius, DotRadius);
    }
}
