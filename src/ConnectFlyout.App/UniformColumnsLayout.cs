using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ConnectFlyout.App;

/// <summary>
/// Lays items out in equal-width columns, wrapping to new rows. Every item is always realized.
/// </summary>
/// <remarks>
/// Used for the flyout's scene buttons instead of <see cref="UniformGridLayout"/>, which
/// virtualizes: while the rows above animate in, the repeater misjudges its viewport and drops
/// the last button until the next layout pass. A handful of buttons don't need virtualizing.
/// </remarks>
public sealed partial class UniformColumnsLayout : NonVirtualizingLayout
{
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

        var itemWidth = ItemWidth(availableSize.Width);
        var rowHeight = 0.0;
        foreach (var child in children)
        {
            child.Measure(new Size(itemWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }

        var rows = (children.Count + Columns - 1) / Columns;
        return new Size(availableSize.Width, (rows * rowHeight) + ((rows - 1) * RowSpacing));
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var children = context.Children;
        var itemWidth = ItemWidth(finalSize.Width);
        var rowHeight = children.Count == 0 ? 0 : children.Max(child => child.DesiredSize.Height);

        for (var i = 0; i < children.Count; i++)
        {
            var x = (i % Columns) * (itemWidth + ColumnSpacing);
            var y = (i / Columns) * (rowHeight + RowSpacing);
            children[i].Arrange(new Rect(x, y, itemWidth, rowHeight));
        }
        return finalSize;
    }

    private double ItemWidth(double width) =>
        Math.Max(0, (width - ((Columns - 1) * ColumnSpacing)) / Columns);
}
