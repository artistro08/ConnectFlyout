using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace ConnectFlyout.App.UITests;

/// <summary>
/// Finds elements the way a screen reader would: by type and automation name.
/// </summary>
internal static class VisualTree
{
    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var grandchild in Descendants(child))
            {
                yield return grandchild;
            }
        }
    }

    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject => Descendants(root).OfType<T>();

    public static T Find<T>(DependencyObject root, string automationName) where T : DependencyObject =>
        TryFind<T>(root, automationName) ?? throw new AssertFailedException($"No {typeof(T).Name} named \"{automationName}\".");

    public static T? TryFind<T>(DependencyObject root, string automationName) where T : DependencyObject =>
        Descendants<T>(root).FirstOrDefault(each => AutomationProperties.GetName(each) == automationName);

    public static T FindByName<T>(DependencyObject root, string name) where T : FrameworkElement =>
        Descendants<T>(root).FirstOrDefault(each => each.Name == name) ?? throw new AssertFailedException($"No {typeof(T).Name} named {name}.");

    /// <summary>
    /// Clicks a button the way UI Automation does, so its Click and Command both run.
    /// </summary>
    public static void Invoke(ButtonBase button)
    {
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button) ?? throw new AssertFailedException("The button has no automation peer.");
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
    }
}
