using Microsoft.Win32;
using Windows.ApplicationModel;

namespace ConnectFlyout.Presentation.Settings;

/// <summary>
/// Launch-at-sign-in switch.
/// </summary>
public interface IStartupTaskService
{
    /// <summary>
    /// Whether the app currently starts at sign-in.
    /// </summary>
    Task<bool> IsEnabledAsync();

    /// <summary>
    /// Returns whether the task ended up enabled. Windows can refuse when the person or a policy
    /// turned it off in Settings.
    /// </summary>
    Task<bool> SetEnabledAsync(bool enabled);
}

/// <summary>
/// <see cref="IStartupTaskService"/> over the current user's Run key. Used by the classic (MSI)
/// install, which has no MSIX startup task.
/// </summary>
/// <remarks>
/// Windows starts everything listed under
/// HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run at sign-in:
/// https://learn.microsoft.com/windows/win32/setupapi/run-and-runonce-registry-keys
/// </remarks>
public sealed class RegistryStartupService : IStartupTaskService
{
    public const string ValueName = "ConnectFlyout";
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Passed by the Run key entry, so the app can tell a start at sign-in from being opened by hand.
    /// </summary>
    public const string StartupArgument = "--startup";

    private readonly string _executablePath;
    private readonly string _command;
    private readonly string _runKey;

    /// <param name="executablePath">Full path to ConnectFlyout.exe.</param>
    /// <param name="runKey">Key under HKEY_CURRENT_USER; tests pass a scratch key.</param>
    public RegistryStartupService(string executablePath, string runKey = RunKey)
    {
        _executablePath = executablePath;
        _command        = $"\"{executablePath}\" {StartupArgument}";
        _runKey         = runKey;
    }

    public Task<bool> IsEnabledAsync()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKey);
        return Task.FromResult(key?.GetValue(ValueName) is string);
    }

    /// <summary>
    /// Adds <see cref="StartupArgument"/> to an entry this same exe wrote before it existed. Run
    /// once at startup. Other installs' entries are left alone, and a key that can't be written
    /// stays as it is.
    /// </summary>
    /// <returns>True when the entry was updated.</returns>
    public bool UpgradeEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKey, writable: true);
            if (key?.GetValue(ValueName) is not string command
                || !string.Equals(command, $"\"{_executablePath}\"", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            key.SetValue(ValueName, _command);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Starts at sign-in without the argument, as before
            return false;
        }
    }

    public Task<bool> SetEnabledAsync(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_runKey);
        if (enabled)
        {
            key.SetValue(ValueName, _command);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        return Task.FromResult(enabled);
    }
}

/// <summary>
/// <see cref="IStartupTaskService"/> over the MSIX startup task declared in Package.appxmanifest.
/// </summary>
public sealed class StartupTaskService : IStartupTaskService
{
    public const string TaskId = "ConnectFlyoutStartup";

    public async Task<bool> IsEnabledAsync()
    {
        var task = await StartupTask.GetAsync(TaskId);
        return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        var task = await StartupTask.GetAsync(TaskId);
        if (!enabled)
        {
            task.Disable();
            return false;
        }

        var state = await task.RequestEnableAsync();
        return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }
}
