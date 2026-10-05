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

    private readonly string _command;
    private readonly string _runKey;

    /// <param name="executablePath">Full path to ConnectFlyout.exe.</param>
    /// <param name="runKey">Key under HKEY_CURRENT_USER; tests pass a scratch key.</param>
    public RegistryStartupService(string executablePath, string runKey = RunKey)
    {
        _command = $"\"{executablePath}\"";
        _runKey = runKey;
    }

    public Task<bool> IsEnabledAsync()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKey);
        return Task.FromResult(key?.GetValue(ValueName) is string);
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
