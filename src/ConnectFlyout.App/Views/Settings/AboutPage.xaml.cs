namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// App name, installed version and credits.
/// </summary>
public sealed partial class AboutPage : SettingsPageBase
{
    public AboutPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// "Version 1.2.3.4", from the MSIX package or the classic install's executable.
    /// </summary>
    public static string VersionText => $"Version {AppIdentity.Version}";
}
