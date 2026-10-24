using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ConnectFlyout.App;

/// <summary>
/// Lays items out in rows of up to <see cref="Columns"/>, each row's items sharing its full
/// width (so a lone item on the last row spans it). Every item is always realized.
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

    // Never fewer than one, whatever XAML asks for
    private int ColumnLimit => Math.Max(1, Columns);

    public double ColumnSpacing { get; set; }

    public double RowSpacing { get; set; }

    protected override Size MeasureOverride(NonVirtualizingLayoutContext context, Size availableSize)
    {
        var children = context.Children;
        if (children.Count == 0)
        {
            return new Size(0, 0);
        }

        var rowHeight = 0.0;
        foreach (var (child, width, _, _) in Cells(children, availableSize.Width))
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }

        var rows = (children.Count + ColumnLimit - 1) / ColumnLimit;
        return new Size(availableSize.Width, (rows * rowHeight) + ((rows - 1) * RowSpacing));
    }

    protected override Size ArrangeOverride(NonVirtualizingLayoutContext context, Size finalSize)
    {
        var children = context.Children;
        var rowHeight = children.Count == 0 ? 0 : children.Max(child => child.DesiredSize.Height);

        foreach (var (child, width, x, row) in Cells(children, finalSize.Width))
        {
            child.Arrange(new Rect(x, row * (rowHeight + RowSpacing), width, rowHeight));
        }
        return finalSize;
    }

    // Each child's width, left edge and row. A row holds up to Columns children, and the ones
    // it has share its full width, so a short last row spreads out instead of leaving a gap.
    private IEnumerable<(UIElement Child, double Width, double X, int Row)> Cells(IReadOnlyList<UIElement> children, double width)
    {
        var columns = ColumnLimit;
        for (var i = 0; i < children.Count; i++)
        {
            var row = i / columns;
            var inRow = Math.Min(columns, children.Count - (row * columns));
            var itemWidth = Math.Max(0, (width - ((inRow - 1) * ColumnSpacing)) / inRow);
            yield return (children[i], itemWidth, (i % columns) * (itemWidth + ColumnSpacing), row);
        }
    }
}
