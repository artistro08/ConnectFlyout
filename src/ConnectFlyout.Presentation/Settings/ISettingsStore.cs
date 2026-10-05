using System.Text.Json;
using Windows.Foundation.Collections;
using Windows.Storage;

namespace ConnectFlyout.Presentation.Settings;

/// <summary>
/// String key/value storage for app settings.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// The stored value, or null when the key isn't set.
    /// </summary>
    string? GetString(string key);

    /// <summary>
    /// Stores the value, or removes the key when <paramref name="value"/> is null.
    /// </summary>
    void SetString(string key, string? value);
}

/// <summary>
/// Settings kept in memory. Used by tests.
/// </summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, string> _values = [];

    public string? GetString(string key) => _values.GetValueOrDefault(key);

    public void SetString(string key, string? value)
    {
        if (value is null)
        {
            _values.Remove(key);
            return;
        }
        _values[key] = value;
    }
}

/// <summary>
/// Settings in a JSON file. Used by the classic (MSI) install, which has no package identity
/// and so no <see cref="LocalSettingsStore"/>.
/// </summary>
/// <remarks>
/// Written in full on every change, through a temporary file so a crash mid-write can't leave
/// half a file. A missing or unreadable file starts empty.
/// </remarks>
public sealed class JsonFileSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly Dictionary<string, string> _values;
    private readonly Lock _gate = new();

    /// <param name="path">Settings file, e.g. %LOCALAPPDATA%\ConnectFlyout\settings.json.</param>
    public JsonFileSettingsStore(string path)
    {
        _path = path;
        _values = Load(path);
    }

    public string? GetString(string key)
    {
        lock (_gate)
        {
            return _values.GetValueOrDefault(key);
        }
    }

    public void SetString(string key, string? value)
    {
        lock (_gate)
        {
            if (value is null)
            {
                _values.Remove(key);
            }
            else
            {
                _values[key] = value;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_values));
            File.Move(temporary, _path, overwrite: true);
        }
    }

    private static Dictionary<string, string> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>
/// Settings in the package's local app data. Needs package identity (the installed MSIX).
/// </summary>
public sealed class LocalSettingsStore : ISettingsStore
{
    private readonly IPropertySet _values = ApplicationData.Current.LocalSettings.Values;

    public string? GetString(string key) => _values.TryGetValue(key, out var value) ? value as string : null;

    public void SetString(string key, string? value)
    {
        if (value is null)
        {
            _values.Remove(key);
            return;
        }
        _values[key] = value;
    }
}
