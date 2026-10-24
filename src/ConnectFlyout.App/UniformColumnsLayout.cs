using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ConnectFlyout.App;

/// <summary>
/// Lays items out in equal-width columns, up to <see cref="Columns"/> per row, wrapping to new
/// rows. Fewer items than columns share the full width. Every item is always realized.
/// </summary>
/// <remarks>
/// Used for the flyout's scene buttons instead of <see cref="UniformGridLayout"/>, which
/// virtualizes: while the rows above animate in, the repeater misjudges its viewport and drops
/// the last button until the next layout pass. A handful of buttons don't need virtualizing.
/// </remarks>
public sealed partial class UniformColumnsLayout : NonVirtualizingLayout
{
    /// <summary>
    /// The most items in a row.
    /// </summary>
    public int Columns { get; set; } = 3;

    public double ColumnSpacing { get; set; }

    public double RowSpacing { get; set; }

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        var children = context.Children;
        if (children.Count == 0)
        {
            return new Size(0, 0);
        }

        var columns = ColumnsFor(children.Count);
        var itemWidth = ItemWidth(availableSize.Width, columns);
        var rowHeight = 0.0;
        foreach (var child in children)
        {
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }

        var rows = (children.Count + columns - 1) / columns;
        return new Size(availableSize.Width, (rows * rowHeight) + ((rows - 1) * RowSpacing));
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var children = context.Children;
        var columns = ColumnsFor(children.Count);
        var itemWidth = ItemWidth(finalSize.Width, columns);
        var rowHeight = children.Count == 0 ? 0 : children.Max(child => child.DesiredSize.Height);

        for (var i = 0; i < children.Count; i++)
        {
            var x = (i % columns) * (itemWidth + ColumnSpacing);
            var y = (i / columns) * (rowHeight + RowSpacing);
            children[i].Arrange(new Rect(x, y, itemWidth, rowHeight));
        }
        return finalSize;
    }

    // Fewer items than Columns spread across the width; never fewer than one column
    private int ColumnsFor(int count) => Math.Clamp(count, 1, Math.Max(1, Columns));

    private double ItemWidth(double width, int columns) =>
        Math.Max(0, (width - ((columns - 1) * ColumnSpacing)) / columns);
}
