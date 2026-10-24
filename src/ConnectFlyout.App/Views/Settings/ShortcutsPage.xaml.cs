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
/// Settings › Shortcuts: one PowerToys shortcut picker per noise mode and per scene, as in Leaf Calendar (press the
/// new keys in its dialog; Reset picks the default, Clear turns it off; scenes have no default, so Reset clears). A
/// combination another app holds shows a warning under its row and asks for another.
/// </summary>
public sealed partial class ShortcutsPage : Page
{
    // One shortcut the page edits: where it's read and saved, its default ("" for none) and whether it's taken
    private sealed record Slot(string Name, string Glyph, string Default, Func<string> Get, Action<string> Set, Func<bool> IsTaken);

    private readonly Dictionary<Slot, (ShortcutControl Control, InfoBar Warning)> _rows = [];

    // Why a shortcut isn't what was picked (the new one was taken and the old one is back); cleared on the next change
    private readonly Dictionary<Slot, string> _notes = [];

    private ShortcutsContext _context = null!;

    // Closed() applies the shortcuts itself and loads once at the end; the Changed it raises is skipped meanwhile
    private bool _applying;

    // The shortcut when its dialog opened, to restore if the new one turns out taken
    private string? _before;

    public ShortcutsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        _context = (ShortcutsContext)e.Parameter;
        BuildRows();
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

    // The noise mode and flyout rows, then one per scene. Scenes change on their own page, so the
    // rows are built on each visit.
    private void BuildRows()
    {
        var settings = _context.Settings;
        var shortcuts = _context.Shortcuts;
        Rows.Children.Clear();
        SceneRows.Children.Clear();
        _rows.Clear();
        _notes.Clear();

        foreach (var action in ShortcutActions.All)
        {
            AddRow(Rows, new Slot(
                ShortcutActions.NameFor(action),
                "",
                ShortcutActions.DefaultFor(action),
                () => settings.GetShortcut(action),
                text => settings.SetShortcut(action, text),
                () => shortcuts.IsTaken(action)));
        }

        foreach (var scene in settings.Scenes)
        {
            if (scene.Id is not { } id)
            {
                continue;
            }
            AddRow(SceneRows, new Slot(
                scene.Name,
                scene.Glyph,
                "",
                () => settings.GetSceneShortcut(id),
                text => settings.SetSceneShortcut(id, text),
                () => shortcuts.IsSceneTaken(id)));
        }
        NoScenes.Visibility = SceneRows.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // A card with the icon (scenes), the name and the picker, and the warning under it
    private void AddRow(Panel panel, Slot slot)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        if (slot.Glyph.Length > 0)
        {
            grid.Children.Add(new FontIcon { Glyph = slot.Glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        }
        var title = new TextBlock { Text = slot.Name, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(title, 1);
        var control = new ShortcutControl();
        Grid.SetColumn(control, 2);
        grid.Children.Add(title);
        grid.Children.Add(control);

        var warning = new InfoBar { Severity = InfoBarSeverity.Warning, IsClosable = false, Visibility = Visibility.Collapsed };
        AutomationProperties.SetLiveSetting(warning, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);

        panel.Children.Add(new Border { Style = (Style)Application.Current.Resources["SettingsCardStyle"], Child = grid });
        panel.Children.Add(warning);
        _rows[slot] = (control, warning);
        Wire(control, slot);
    }

    private void Load()
    {
        foreach (var (slot, (control, warning)) in _rows)
        {
            Show(control, warning, slot);
        }
    }

    // The shortcut as key caps; a taken one shows its caps in the warning state, and the warning under the row is
    // collapsed when there's nothing to say
    private void Show(ShortcutControl control, InfoBar warning, Slot slot)
    {
        var shortcuts = _context.Shortcuts;
        var shortcut = slot.Get();
        control.HotkeySettings = Hotkey.TryParse(shortcut, out var hotkey) ? HotkeySettings.FromHotkey(hotkey) : new HotkeySettings();

        // Without the tray icon's window nothing can be registered, which isn't another app's doing
        var message = !shortcuts.IsAvailable ? "Shortcuts are not available because the tray icon did not start."
            : _notes.TryGetValue(slot, out var note) ? note
            : slot.IsTaken() ? $"A different app uses {shortcut}. Select a different shortcut."
            : null;
        control.HasConflict = shortcuts.IsAvailable && slot.IsTaken();
        control.Tooltip = message;
        warning.Message = message ?? string.Empty;
        warning.IsOpen = message is not null;
        warning.Visibility = message is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    // Wires A Picker To Its Shortcut: the default for Reset, the conflict check, and saving
    private void Wire(ShortcutControl control, Slot slot)
    {
        control.ButtonName = slot.Name;
        control.DefaultHotkeySettings = Hotkey.TryParse(slot.Default, out var fallback) ? HotkeySettings.FromHotkey(fallback) : new HotkeySettings();
        control.CheckConflict = hotkey => Conflict(slot, hotkey);
        control.DialogOpening += (_, _) => Opening(slot);
        control.DialogClosed += (_, _) => Closed(slot);
        control.HotkeySettingsChanged += (_, _) =>
        {
            // A New Combination, The Default (Reset), Or "" (cleared)
            var picked = control.HotkeySettings is { } settings && !settings.IsEmpty() ? settings.ToHotkey()?.ToString() : "";
            if (picked is not null)
            {
                slot.Set(picked);
            }
        };
    }

    // Another row has it, or Windows or another app holds it (a RegisterHotKey probe). Without the tray icon's
    // window nothing can be registered, which isn't another app's doing.
    private string? Conflict(Slot slot, Hotkey hotkey)
    {
        var text = hotkey.ToString();
        var other = _rows.Keys.FirstOrDefault(each => each != slot && each.Get() == text);
        return !_context.Shortcuts.IsAvailable ? "Shortcuts are not available because the tray icon did not start."
            : other is not null ? $"“{other.Name}” already uses {text}."
            : !_context.Shortcuts.IsFree(hotkey) ? $"Windows or a different app uses {text}. Select a different shortcut."
            : null;
    }

    // The app's own shortcuts let go while the dialog listens (so pressing them reaches it)
    private void Opening(Slot slot)
    {
        _before = slot.Get();
        _notes.Remove(slot);
        _context.Shortcuts.Suspend();
    }

    // ...and come back from the settings after, whichever way the dialog closed. A new combination another app took
    // since the dialog checked it gives way to the one that worked before.
    private void Closed(Slot slot)
    {
        var shortcuts = _context.Shortcuts;
        _applying = true;
        try
        {
            shortcuts.Apply(_context.Settings);

            var saved = slot.Get();
            var previous = _before ?? saved;
            if (slot.IsTaken() && saved != previous)
            {
                _notes[slot] = $"{saved} was taken by another app, so {(previous.Length == 0 ? "no shortcut" : previous)} is back.";
                slot.Set(previous);
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
