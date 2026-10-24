using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ConnectFlyout.App.Interop;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.Shortcuts;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// What a shortcut runs: a noise mode or the flyout (<see cref="Action"/>), or a scene (<see cref="SceneId"/>).
/// </summary>
public sealed record ShortcutTarget(ShortcutAction? Action, string? SceneId)
{
    public static ShortcutTarget For(ShortcutAction action) => new(action, null);

    public static ShortcutTarget ForScene(string sceneId) => new(null, sceneId);

    /// <summary>
    /// Every target: the actions, then the saved scenes.
    /// </summary>
    public static IEnumerable<ShortcutTarget> All(AppSettings settings) =>
        ShortcutActions.All.Select(For).Concat(settings.Scenes.Where(scene => scene.Id is not null).Select(scene => ForScene(scene.Id!)));

    /// <summary>The default combination; "" for a scene, which has none.</summary>
    public string Default => Action is { } action ? ShortcutActions.DefaultFor(action) : "";

    /// <summary>
    /// The row's name: the action's, with the other device's own name in "Play on …" when known, or the scene's.
    /// </summary>
    public string Name(ShortcutsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Action switch
        {
            ShortcutAction.PlaybackOther when context.OtherDeviceName() is { Length: > 0 } device => $"Play on {device}",
            { } action => ShortcutActions.NameFor(action),
            null => context.Settings.Scenes.FirstOrDefault(scene => scene.Id == SceneId)?.Name ?? "Scene",
        };
    }

    public string Get(AppSettings settings) => Action is { } action ? settings.GetShortcut(action) : settings.GetSceneShortcut(SceneId!);

    public void Set(AppSettings settings, string shortcut)
    {
        if (Action is { } action)
        {
            settings.SetShortcut(action, shortcut);
        }
        else
        {
            settings.SetSceneShortcut(SceneId!, shortcut);
        }
    }

    public bool IsTaken(GlobalShortcuts shortcuts) => Action is { } action ? shortcuts.IsTaken(action) : shortcuts.IsSceneTaken(SceneId!);
}

/// <summary>
/// Wires one PowerToys shortcut picker, and the warning under it, to a <see cref="ShortcutTarget"/>: the key caps
/// it shows, Reset's default, the conflict check, saving, and the rollback when a new combination turns out taken.
/// A page makes one per row and detaches it when the row goes.
/// </summary>
public sealed class ShortcutRow
{
    // Every live row, so the window can close any open dialog as it closes (an open dialog keeps its keyboard hook)
    private static readonly HashSet<ShortcutRow> Live = [];

    private readonly ShortcutsContext _context;
    private readonly ShortcutTarget _target;
    private readonly ShortcutControl _control;
    private readonly InfoBar _warning;
    private readonly TextBlock? _title;

    // Why the shortcut isn't what was picked (the new one was taken and the old one is back); cleared on the next change
    private string? _note;

    // The shortcut when its dialog opened, to restore if the new one turns out taken
    private string? _before;

    // Closed() applies the shortcuts itself and loads once at the end; the Changed it raises is skipped meanwhile
    private bool _applying;

    /// <param name="title">The row's title, kept in step with the name (a scene's rename, the other device appearing).</param>
    public ShortcutRow(ShortcutsContext context, ShortcutTarget target, ShortcutControl control, InfoBar warning, TextBlock? title = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(warning);

        _context = context;
        _target = target;
        _control = control;
        _warning = warning;
        _title = title;
        Live.Add(this);

        control.DefaultHotkeySettings = Hotkey.TryParse(target.Default, out var fallback) ? HotkeySettings.FromHotkey(fallback) : new HotkeySettings();
        control.CheckConflict = Conflict;
        control.DialogOpening += OnDialogOpening;
        control.DialogClosed += OnDialogClosed;
        control.HotkeySettingsChanged += OnHotkeySettingsChanged;
        context.Shortcuts.Changed += OnChanged;
        Load();
    }

    /// <summary>
    /// Closes every row's open dialog, which lets go of the keyboard and registers the shortcuts again.
    /// </summary>
    public static void CloseAll()
    {
        foreach (var row in Live.ToList())
        {
            row._control.CloseDialog();
        }
    }

    /// <summary>
    /// Closes the dialog and stops following the picker and the shortcuts.
    /// </summary>
    public void Detach()
    {
        _context.Shortcuts.Changed -= OnChanged;
        _control.CloseDialog();
        _control.DialogOpening -= OnDialogOpening;
        _control.DialogClosed -= OnDialogClosed;
        _control.HotkeySettingsChanged -= OnHotkeySettingsChanged;
        Live.Remove(this);
    }

    // The shortcut as key caps; a taken one shows its caps in the warning state, and the warning under the row is
    // collapsed when there's nothing to say
    private void Load()
    {
        var shortcuts = _context.Shortcuts;
        var shortcut = _target.Get(_context.Settings);
        RefreshName();
        var taken = _target.IsTaken(shortcuts);
        _control.HotkeySettings = Hotkey.TryParse(shortcut, out var hotkey) ? HotkeySettings.FromHotkey(hotkey) : new HotkeySettings();

        // Without the tray icon's window nothing can be registered, which isn't another app's doing
        var message = !shortcuts.IsAvailable ? "Shortcuts are not available because the tray icon did not start."
            : _note is not null ? _note
            : taken ? $"A different app uses {shortcut}. Select a different shortcut."
            : null;
        _control.HasConflict = shortcuts.IsAvailable && taken;
        _control.Tooltip = message;
        _warning.Message = message ?? string.Empty;
        _warning.IsOpen = message is not null;
        _warning.Visibility = message is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    // The name can change while the page is open: a scene renamed, the other device appearing
    private void RefreshName()
    {
        var name = _target.Name(_context);
        _control.ButtonName = name;
        if (_title is not null)
        {
            _title.Text = name;
        }
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (!_applying)
        {
            Load();
        }
    }

    // A New Combination, The Default (Reset), Or "" (cleared)
    private void OnHotkeySettingsChanged(object? sender, EventArgs e)
    {
        var picked = _control.HotkeySettings is { } settings && !settings.IsEmpty() ? settings.ToHotkey()?.ToString() : "";
        if (picked is not null)
        {
            _target.Set(_context.Settings, picked);
        }
    }

    // Another shortcut has it, or Windows or another app holds it (a RegisterHotKey probe). Without the tray
    // icon's window nothing can be registered, which isn't another app's doing.
    private string? Conflict(Hotkey hotkey)
    {
        var settings = _context.Settings;
        var text = hotkey.ToString();
        var other = ShortcutTarget.All(settings).FirstOrDefault(each => each != _target && each.Get(settings) == text);
        return !_context.Shortcuts.IsAvailable ? "Shortcuts are not available because the tray icon did not start."
            : other is not null ? $"“{other.Name(_context)}” already uses {text}."
            : !_context.Shortcuts.IsFree(hotkey) ? "Windows or a different app uses " + text + ". Select a different shortcut."
            : null;
    }

    // The app's own shortcuts let go while the dialog listens (so pressing them reaches it)
    private void OnDialogOpening(object? sender, EventArgs e)
    {
        RefreshName();
        _before = _target.Get(_context.Settings);
        _note = null;
        _context.Shortcuts.Suspend();
    }

    // ...and come back from the settings after, whichever way the dialog closed. A new combination another app took
    // since the dialog checked it gives way to the one that worked before.
    private void OnDialogClosed(object? sender, EventArgs e)
    {
        var shortcuts = _context.Shortcuts;
        var settings = _context.Settings;
        _applying = true;
        try
        {
            shortcuts.Apply(settings);

            var saved = _target.Get(settings);
            var previous = _before ?? saved;
            if (_target.IsTaken(shortcuts) && saved != previous)
            {
                _note = $"A different app took {saved}, so {(previous.Length == 0 ? "no shortcut" : previous)} is back.";
                _target.Set(settings, previous);
                shortcuts.Apply(settings);
            }
        }
        finally
        {
            _applying = false;
        }

        Load();
    }
}
