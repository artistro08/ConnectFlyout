using Microsoft.Win32;

namespace ConnectFlyout.App;

/// <summary>
/// What Windhawk (a Windows customization tool) has changed that the app needs to know about.
/// </summary>
/// <remarks>
/// Windhawk lists its installed mods as keys under HKLM\SOFTWARE\Windhawk\Engine\Mods, named by mod ID, each with a
/// Disabled value. Read on every call, so turning a mod on or off applies without restarting the app.
/// </remarks>
/// <seealso href="https://windhawk.net/mods/taskbar-multi-tray"/>
internal static class Windhawk
{
    private const string ModsKey = @"SOFTWARE\Windhawk\Engine\Mods";

    // The Taskbar Multi Tray mod, or a fork of it ("local@taskbar-multi-tray-fork")
    private const string MultiTrayModId = "taskbar-multi-tray";

    /// <summary>
    /// True when a turned-on Taskbar Multi Tray mod puts the tray, and so the app's icon, on every taskbar. The icon's
    /// position then only says where one of them is.
    /// </summary>
    public static bool HasTrayOnEveryTaskbar()
    {
        try
        {
            using var mods = Registry.LocalMachine.OpenSubKey(ModsKey);
            if (mods is null)
            {
                return false;
            }

            foreach (var id in mods.GetSubKeyNames().Where(id => id.Contains(MultiTrayModId, StringComparison.OrdinalIgnoreCase)))
            {
                using var mod = mods.OpenSubKey(id);
                if (mod?.GetValue("Disabled") is not 1)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // Can't read it: behave as without the mod
        }

        return false;
    }
}
