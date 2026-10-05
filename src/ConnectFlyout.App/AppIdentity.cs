using System.Reflection;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.Storage;

namespace ConnectFlyout.App;

/// <summary>
/// Whether the app runs from the MSIX package or the classic (MSI) install, and what differs.
/// </summary>
/// <remarks>
/// The MSIX has package identity: its own settings store, startup task and data folder. The
/// classic install has none of those, so it keeps its data in %LOCALAPPDATA%\ConnectFlyout and
/// reads its version from the executable.
/// https://learn.microsoft.com/windows/apps/desktop/modernize/package-identity-overview
/// </remarks>
public static class AppIdentity
{
    /// <summary>
    /// App ID the classic install claims at launch and its Start menu shortcut carries, which
    /// is what lets Windows show its notifications.
    /// </summary>
    public const string ClassicAppUserModelId = "Artistro08.ConnectFlyout.Classic";

    private const int AppModelErrorNoPackage = 15700;

    /// <summary>
    /// True when running from the installed MSIX.
    /// </summary>
    public static bool IsPackaged { get; } = HasPackageIdentity();

    /// <summary>
    /// Folder for settings and logs.
    /// </summary>
    public static string DataFolder => IsPackaged
        ? ApplicationData.Current.LocalFolder.Path
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ConnectFlyout");

    /// <summary>
    /// "1.2.3.4", from the package or, for the classic install, the executable.
    /// </summary>
    public static string Version
    {
        get
        {
            if (IsPackaged)
            {
                var version = Package.Current.Id.Version;
                return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            }
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        }
    }

    /// <summary>
    /// Classic install only: names this process with <see cref="ClassicAppUserModelId"/>, so the
    /// taskbar and notifications group it under the installer's Start menu shortcut.
    /// </summary>
    public static void ClaimClassicAppId()
    {
        if (!IsPackaged)
        {
            _ = SetCurrentProcessExplicitAppUserModelID(ClassicAppUserModelId);
        }
    }

    private static bool HasPackageIdentity()
    {
        var length = 0;
        return GetCurrentPackageFullName(ref length, null) != AppModelErrorNoPackage;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
