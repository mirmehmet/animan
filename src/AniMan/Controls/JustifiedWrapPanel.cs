using System.Windows;
using System.Windows.Controls;
using AniMan.Core.Library;

namespace AniMan.Controls;

/// <summary>
/// Wraps equally sized items into rows that span the full width: the column count comes
/// from the available width and the leftover space is shared between the gaps, so the grid
/// lines up with both edges instead of leaving a ragged right margin. The last row keeps the
/// same columns (left-aligned). Item width is taken from the widest child.
/// </summary>
public sealed class JustifiedWrapPanel : Panel
{
    public static readonly DependencyProperty MinGapProperty =
        DependencyProperty.Register(nameof(MinGap), typeof(double), typeof(JustifiedWrapPanel),
            new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowGapProperty =
        DependencyProperty.Register(nameof(RowGap), typeof(double), typeof(JustifiedWrapPanel),
            new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Smallest horizontal gap between items.</summary>
    public double MinGap { get => (double)GetValue(MinGapProperty); set => SetValue(MinGapProperty, value); }

    public double RowGap { get => (double)GetValue(RowGapProperty); set => SetValue(RowGapProperty, value); }

    private double _itemWidth;
    private int _columns = 1;
    private double _gap;

    protected override Size MeasureOverride(Size availableSize)
    {
        _itemWidth = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _itemWidth = Math.Max(_itemWidth, child.DesiredSize.Width);
        }

        (_columns, _gap) = JustifiedLayout.Compute(availableSize.Width, _itemWidth, MinGap);

        var height = 0.0;
        var rows = RowHeights().ToList();
        if (rows.Count > 0)
            height = rows.Sum() + RowGap * (rows.Count - 1);

        var width = double.IsInfinity(availableSize.Width) ? _itemWidth : availableSize.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        (_columns, _gap) = JustifiedLayout.Compute(finalSize.Width, _itemWidth, MinGap);

        var rowHeights = RowHeights().ToList();
        var y = 0.0;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var row = i / _columns;
            var column = i % _columns;
            if (column == 0 && row > 0)
                y += rowHeights[row - 1] + RowGap;

            var child = InternalChildren[i];
            var x = column * (_itemWidth + _gap);
            child.Arrange(new Rect(x, y, _itemWidth, rowHeights[row]));
        }
        return finalSize;
    }

    private IEnumerable<double> RowHeights()
    {
        for (var start = 0; start < InternalChildren.Count; start += _columns)
        {
            var max = 0.0;
            for (var i = start; i < Math.Min(start + _columns, InternalChildren.Count); i++)
                max = Math.Max(max, InternalChildren[i].DesiredSize.Height);
            yield return max;
        }
    }
}
