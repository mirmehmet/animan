using System.Windows;
using System.Windows.Controls;

namespace AniMan.Controls.Charts;

/// <summary>
/// Lays children out left to right, each taking a share of the width proportional to its
/// <see cref="WeightProperty"/>, with an optional fixed gap between them. Used for the
/// segmented status bar (one segment per status) and for ranked bars (fill + remainder).
/// Children with weight 0 collapse to nothing.
/// </summary>
public sealed class ProportionalPanel : Panel
{
    public static readonly DependencyProperty WeightProperty =
        DependencyProperty.RegisterAttached(
            "Weight", typeof(double), typeof(ProportionalPanel),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static double GetWeight(DependencyObject element) => (double)element.GetValue(WeightProperty);
    public static void SetWeight(DependencyObject element, double value) => element.SetValue(WeightProperty, value);

    public static readonly DependencyProperty GapProperty =
        DependencyProperty.Register(
            nameof(Gap), typeof(double), typeof(ProportionalPanel),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>Space between visible children (the 2px surface gap between segments).</summary>
    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = InternalChildren.Cast<UIElement>()
            .Where(c => c.Visibility != Visibility.Collapsed && WeightOf(c) > 0)
            .ToList();
        var totalWeight = visible.Sum(WeightOf);
        var usable = Math.Max(0, finalSize.Width - Gap * Math.Max(0, visible.Count - 1));

        var x = 0.0;
        foreach (UIElement child in InternalChildren)
        {
            if (!visible.Contains(child) || totalWeight <= 0)
            {
                child.Arrange(new Rect(x, 0, 0, finalSize.Height));
                continue;
            }

            var w = usable * WeightOf(child) / totalWeight;
            child.Arrange(new Rect(x, 0, w, finalSize.Height));
            x += w + Gap;
        }
        return finalSize;
    }

    private static double WeightOf(UIElement e) => Math.Max(0, GetWeight(e));
}
