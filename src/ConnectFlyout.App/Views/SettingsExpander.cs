using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ConnectFlyout.App.Views;

/// <summary>
/// An Expander with Windows Settings' 16 px chevron. WinUI draws it at 12 px and reads the size
/// through a StaticResource, which an app can't override, so the icon is resized once loaded.
/// </summary>
public sealed partial class SettingsExpander : Expander
{
    private const double ChevronSize = 16;

    public SettingsExpander()
    {
        Loaded += (_, _) => ResizeChevron(this);
    }

    private static bool ResizeChevron(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Name: "ExpandCollapseChevron" } chevron)
            {
                chevron.Width = chevron.Height = ChevronSize;
                return true;
            }
            if (ResizeChevron(child))
            {
                return true;
            }
        }
        return false;
    }
}
