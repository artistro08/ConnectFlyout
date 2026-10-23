// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
//
// Ported from PowerToys: src/settings-ui/Settings.UI/SettingsXAML/Controls/ShortcutControl/ShortcutDialogContentControl.xaml.cs
// (https://github.com/microsoft/PowerToys), by way of Leaf Calendar. Leaf changes: Keys is a plain property whose setter
// draws the key caps (a list of boxed values can't go to a WinRT ItemsSource under Native AOT), with KeysName for
// screen readers and tests; no ignore-conflict or "Learn more" parts.

using ConnectFlyout.App.Controls;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace ConnectFlyout.App.Views.Settings;

/// <summary>The shortcut dialog's content: the tip, the pressed keys as key caps (or a prompt), Reset and Clear, and the problems and warnings.</summary>
public sealed partial class ShortcutDialogContentControl : UserControl
{
    /// <summary>Identifies <see cref="IsError"/>.</summary>
    public static readonly DependencyProperty IsErrorProperty = DependencyProperty.Register("IsError", typeof(bool), typeof(ShortcutDialogContentControl), new PropertyMetadata(false, OnIsErrorChanged));
    /// <summary>Identifies <see cref="IsWarningAltGr"/>.</summary>
    public static readonly DependencyProperty IsWarningAltGrProperty = DependencyProperty.Register("IsWarningAltGr", typeof(bool), typeof(ShortcutDialogContentControl), new PropertyMetadata(false));
    /// <summary>Identifies <see cref="HasConflict"/>.</summary>
    public static readonly DependencyProperty HasConflictProperty = DependencyProperty.Register("HasConflict", typeof(bool), typeof(ShortcutDialogContentControl), new PropertyMetadata(false, OnIsErrorChanged));
    /// <summary>Identifies <see cref="ConflictMessage"/>.</summary>
    public static readonly DependencyProperty ConflictMessageProperty = DependencyProperty.Register("ConflictMessage", typeof(string), typeof(ShortcutDialogContentControl), new PropertyMetadata(string.Empty));
    /// <summary>Identifies <see cref="ErrorMessage"/>.</summary>
    public static readonly DependencyProperty ErrorMessageProperty = DependencyProperty.Register("ErrorMessage", typeof(string), typeof(ShortcutDialogContentControl), new PropertyMetadata("Invalid shortcut"));

    private List<object>? _keys;

    /// <summary>Creates the content.</summary>
    public ShortcutDialogContentControl()
    {
        this.InitializeComponent();
    }

    /// <summary>Reset was clicked.</summary>
    public event RoutedEventHandler? ResetClick;

    /// <summary>Clear was clicked.</summary>
    public event RoutedEventHandler? ClearClick;

    /// <summary>The combination is taken (by the other shortcut, or by Windows or another app): Save is off, so the caps turn red like an invalid one.</summary>
    public bool HasConflict
    {
        get => (bool)GetValue(HasConflictProperty);
        set => SetValue(HasConflictProperty, value);
    }

    /// <summary>Says who has the combination.</summary>
    public string ConflictMessage
    {
        get => (string)GetValue(ConflictMessageProperty);
        set => SetValue(ConflictMessageProperty, value);
    }

    /// <summary>The key caps (see <see cref="ConnectFlyout.Presentation.Shortcuts.HotkeySettings.GetKeysList"/>); setting it draws them.</summary>
    public List<object>? Keys
    {
        get => _keys;
        set
        {
            _keys = value;
            DrawKeys();
        }
    }

    /// <summary>The keys as text ("Ctrl+Alt+J", "No keys"), the key area's accessible name.</summary>
    public string KeysName
    {
        get => AutomationProperties.GetName(KeysArea);
        set => AutomationProperties.SetName(KeysArea, value);
    }

    /// <summary>The combination can't be used (the caps turn red and <see cref="ErrorMessage"/> shows).</summary>
    public bool IsError
    {
        get => (bool)GetValue(IsErrorProperty);
        set => SetValue(IsErrorProperty, value);
    }

    /// <summary>Why the combination can't be used ("Invalid shortcut. It must start with the Windows key, Ctrl, or Alt.").</summary>
    public string ErrorMessage
    {
        get => (string)GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    /// <summary>Puts keyboard focus on the key area (the dialog's first focusable control is Reset, whose tooltip would cover the caps).</summary>
    public void FocusKeys() => KeysArea.Focus(FocusState.Programmatic);

    /// <summary>Ctrl+Alt without Win: the AltGr warning shows.</summary>
    public bool IsWarningAltGr
    {
        get => (bool)GetValue(IsWarningAltGrProperty);
        set => SetValue(IsWarningAltGrProperty, value);
    }

    /// <summary>True when <paramref name="newValue"/> lists the same keys as <see cref="Keys"/>.</summary>
    public bool JudgeIfKeyValueSame(List<object> newValue)
    {
        List<object>? currentValue = _keys;

        if (currentValue == null && newValue == null)
        {
            return true;
        }

        if (currentValue == null || newValue == null)
        {
            return false;
        }

        if (currentValue.Count != newValue.Count)
        {
            return false;
        }

        for (int index = 0; index < currentValue.Count; index++)
        {
            if (!Equals(currentValue[index], newValue[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static void OnIsErrorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ShortcutDialogContentControl)d).DrawKeys();

    // The Key Caps (PowerToys' KeysControl item template), Or The Prompt With None, And Clear, On Only With Keys To
    // Clear (off, not hidden, so Reset and Clear keep their places). Caps already on screen are updated in place; only a
    // missing one is made and only a surplus one removed
    private void DrawKeys()
    {
        var keys = _keys ?? [];
        var caps = KeysControl.Children;
        var state = IsError || HasConflict ? KeyVisualState.Error : KeyVisualState.Normal;

        while (caps.Count > keys.Count)
        {
            caps.RemoveAt(caps.Count - 1);
        }

        for (int index = 0; index < keys.Count; index++)
        {
            if (index < caps.Count)
            {
                var cap = (KeyVisual)caps[index];
                cap.Content = keys[index];
                cap.State = state;
                continue;
            }

            var keyVisual = new KeyVisual
            {
                Padding = new Thickness(20, 16, 20, 16),
                Content = keys[index],
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                IsTabStop = false,
                RenderKeyAsGlyph = true,
                State = state,
                Style = (Style)Application.Current.Resources["KeyChipStyle"],
            };
            AutomationProperties.SetAccessibilityView(keyVisual, AccessibilityView.Raw);
            caps.Add(keyVisual);
        }

        KeysPrompt.Visibility = keys.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearBtn.IsEnabled = keys.Count > 0;
    }

    private void ResetBtn_Click(object sender, RoutedEventArgs e)
    {
        ResetClick?.Invoke(this, new RoutedEventArgs());
    }

    private void ClearBtn_Click(object sender, RoutedEventArgs e)
    {
        ClearClick?.Invoke(this, new RoutedEventArgs());
    }
}
