using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ConnectFlyout.App.Interop;
using ConnectFlyout.Presentation.Settings;
using ConnectFlyout.Presentation.Shortcuts;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>
/// What Settings › Shortcuts needs: the registered shortcuts and the settings they're saved in.
/// </summary>
public sealed record ShortcutsContext(GlobalShortcuts Shortcuts, AppSettings Settings);

/// <summary>
/// Settings › Shortcuts: one PowerToys shortcut picker per noise mode, as in Leaf Calendar (press the new keys in its
/// dialog; Reset picks the default, Clear turns it off). A combination another app holds shows a warning under its row
/// and asks for another.
/// </summary>
public sealed partial class ShortcutsPage : Page
{
    private readonly Dictionary<ShortcutAction, (ShortcutControl Control, InfoBar Warning)> _rows = [];

    // Why a shortcut isn't what was picked (the new one was taken and the old one is back); cleared on the next change
    private readonly Dictionary<ShortcutAction, string> _notes = [];

    private ShortcutsContext _context = null!;

    // Closed() applies the shortcuts itself and loads once at the end; the Changed it raises is skipped meanwhile
    private bool _applying;

    // The shortcut when its dialog opened, to restore if the new one turns out taken
    private string? _before;

    public ShortcutsPage()
    {
        InitializeComponent();
        foreach (var action in ShortcutActions.All)
        {
            AddRow(action);
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        _context = (ShortcutsContext)e.Parameter;
        _context.Shortcuts.Changed += OnChanged;
        Unloaded += OnUnloaded;
        Load();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _context.Shortcuts.Changed -= OnChanged;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => CloseDialogs();

    /// <summary>
    /// Closes an open shortcut dialog, which lets go of the keyboard and registers the shortcuts again, and stops
    /// following changes. The settings window calls it when it closes, since WinUI may not unload the page then.
    /// </summary>
    public void CloseDialogs()
    {
        _context.Shortcuts.Changed -= OnChanged;
        foreach (var (control, _) in _rows.Values)
        {
            control.CloseDialog();
        }
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (!_applying)
        {
            Load();
        }
    }

    // A card with the name and the picker, and the warning under it
    private void AddRow(ShortcutAction action)
    {
        var name = ShortcutActions.NameFor(action);

        var title = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
        var control = new ShortcutControl();
        Grid.SetColumn(control, 1);

        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(title);
        grid.Children.Add(control);

        var warning = new InfoBar { Severity = InfoBarSeverity.Warning, IsClosable = false, Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(warning, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);

        Rows.Children.Add(new Border { Style = (Style)Application.Current.Resources["SettingsCardStyle"], Child = grid });
        Rows.Children.Add(warning);
        _rows[action] = (control, warning);
        Wire(control, action, name);
    }

    private void Load()
    {
        foreach (var (action, (control, warning)) in _rows)
        {
            Show(control, warning, _context.Settings.GetShortcut(action), action);
        }
    }

    // The shortcut as key caps; a taken one shows its caps in the warning state, and the warning under the row is
    // collapsed when there's nothing to say
    private void Show(ShortcutControl control, InfoBar warning, string shortcut, ShortcutAction action)
    {
        var shortcuts = _context.Shortcuts;
        control.HotkeySettings = Hotkey.TryParse(shortcut, out var hotkey) ? HotkeySettings.FromHotkey(hotkey) : new HotkeySettings();

        // Without the tray icon's window nothing can be registered, which isn't another app's doing
        var message = !shortcuts.IsAvailable ? "Shortcuts aren't available because the tray icon couldn't start."
            : _notes.TryGetValue(action, out var note) ? note
            : shortcuts.IsTaken(action) ? $"Another app is using {shortcut}. Pick a different shortcut."
            : null;
        control.HasConflict = shortcuts.IsAvailable && shortcuts.IsTaken(action);
        control.Tooltip = message;
        warning.Message = message ?? string.Empty;
        warning.IsOpen = message is not null;
        warning.Visibility = message is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    // Wires A Picker To Its Shortcut: the default for Reset, the conflict check, and saving
    private void Wire(ShortcutControl control, ShortcutAction action, string name)
    {
        control.ButtonName = name;
        control.DefaultHotkeySettings = Hotkey.TryParse(ShortcutActions.DefaultFor(action), out var fallback) ? HotkeySettings.FromHotkey(fallback) : new HotkeySettings();
        control.CheckConflict = hotkey => Conflict(action, hotkey);
        control.DialogOpening += (_, _) => Opening(action);
        control.DialogClosed += (_, _) => Closed(action);
        control.HotkeySettingsChanged += (_, _) =>
        {
            // A New Combination, The Default (Reset), Or "" (cleared)
            var picked = control.HotkeySettings is { } settings && !settings.IsEmpty() ? settings.ToHotkey()?.ToString() : "";
            if (picked is not null)
            {
                _context.Settings.SetShortcut(action, picked);
            }
        };
    }

    // Another noise mode has it, or Windows or another app holds it (a RegisterHotKey probe). Without the tray
    // icon's window nothing can be registered, which isn't another app's doing.
    private string? Conflict(ShortcutAction action, Hotkey hotkey)
    {
        var text = hotkey.ToString();
        var other = ShortcutActions.All.FirstOrDefault(each => each != action && _context.Settings.GetShortcut(each) == text);
        return !_context.Shortcuts.IsAvailable ? "Shortcuts aren't available because the tray icon couldn't start."
            : other != default ? $"“{ShortcutActions.NameFor(other)}” already uses {text}."
            : !_context.Shortcuts.IsFree(hotkey) ? $"Windows or another app is using {text}. Try a different one."
            : null;
    }

    // The app's own shortcuts let go while the dialog listens (so pressing them reaches it)
    private void Opening(ShortcutAction action)
    {
        _before = _context.Settings.GetShortcut(action);
        _notes.Remove(action);
        _context.Shortcuts.Suspend();
    }

    // ...and come back from the settings after, whichever way the dialog closed. A new combination another app took
    // since the dialog checked it gives way to the one that worked before.
    private void Closed(ShortcutAction action)
    {
        var shortcuts = _context.Shortcuts;
        _applying = true;
        try
        {
            shortcuts.Apply(_context.Settings);

            var saved = _context.Settings.GetShortcut(action);
            var previous = _before ?? saved;
            if (shortcuts.IsTaken(action) && saved != previous)
            {
                _notes[action] = $"{saved} was taken by another app, so {(previous.Length == 0 ? "no shortcut" : previous)} is back.";
                _context.Settings.SetShortcut(action, previous);
                shortcuts.Apply(_context.Settings);
            }
        }
        finally
        {
            _applying = false;
        }

        Load();
    }
}
