using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ConnectFlyout.App;

/// <summary>
/// Desktop acrylic that stays see-through when the window isn't focused.
/// </summary>
/// <remarks>
/// The stock <see cref="DesktopAcrylicBackdrop"/> turns solid whenever its window is inactive.
/// Tray flyouts stay acrylic the whole time, including while they slide in before they get
/// focus, so this one always reports the window as active. Theme and high contrast
/// still follow the default configuration.
/// See https://learn.microsoft.com/windows/apps/windows-app-sdk/system-backdrop-controller
/// </remarks>
[SuppressMessage("Design", "CA1001", Justification = "The controller is disposed in OnTargetDisconnected, the backdrop's own teardown.")]
public sealed partial class ActiveAcrylicBackdrop : SystemBackdrop
{
    private readonly SystemBackdropConfiguration _configuration = new() { IsInputActive = true };
    private DesktopAcrylicController? _controller;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        _controller = new DesktopAcrylicController();
        _controller.SetSystemBackdropConfiguration(_configuration);
        _controller.AddSystemBackdropTarget(connectedTarget);
        OnDefaultSystemBackdropConfigurationChanged(connectedTarget, xamlRoot);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;

        base.OnTargetDisconnected(disconnectedTarget);
    }

    // Copy theme changes, but never the "inactive" state
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        var defaults = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        _configuration.Theme          = defaults.Theme;
        _configuration.IsHighContrast = defaults.IsHighContrast;
        _configuration.IsInputActive  = true;
    }
}
