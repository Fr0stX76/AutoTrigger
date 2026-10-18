using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;
using AutoTrigger.Core.Model;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace AutoTrigger.Ui
{
    internal sealed partial class ActionDialog
    {
        private StackPanel _hotkeyPanel;
        private UiTextBox _hotkeyBox;
        private PressHotkeyAction _keys;

        private void BuildHotkey(PressHotkeyAction keys)
        {
            _hotkeyPanel = new StackPanel();
            _keys = new PressHotkeyAction
            {
                Ctrl = keys?.Ctrl ?? false,
                Alt = keys?.Alt ?? false,
                Shift = keys?.Shift ?? false,
                Key = keys?.Key ?? 0,
            };
            _hotkeyBox = Kit.TextField(CapturedText(), null, Texts.PressKeys);
            _hotkeyBox.IsReadOnly = true;
            _hotkeyBox.IsUndoEnabled = false;
            _hotkeyBox.ClearButtonEnabled = false;
            InputMethod.SetIsInputMethodEnabled(_hotkeyBox, false);
            _hotkeyBox.PreviewKeyDown += OnHotkeyDown;
            _hotkeyBox.PreviewKeyUp += OnHotkeyUp;
            _hotkeyBox.PreviewMouseRightButtonUp += (s, e) => e.Handled = true;
            _hotkeyBox.LostKeyboardFocus += (s, e) => _hotkeyBox.Text = CapturedText();
            _hotkeyPanel.Children.Add(Kit.Field(Texts.Hotkey, _hotkeyBox));
        }

        private string CapturedText() => _keys.Key == 0 ? string.Empty : ActionText.HotkeyText(_keys);

        private void OnHotkeyDown(object sender, KeyEventArgs e)
        {
            Key key = PressedKey(e);
            ModifierKeys held = Keyboard.Modifiers;
            if (LeavesTheBox(key, held))
                return;
            e.Handled = true;
            if (IsModifier(key))
            {
                _hotkeyBox.Text = HeldModifiers(held);
                return;
            }
            int virtualKey = KeyInterop.VirtualKeyFromKey(key);
            if (!KeyNames.CanSend(virtualKey))
                return;
            _keys = new PressHotkeyAction
            {
                Ctrl = (held & ModifierKeys.Control) != 0,
                Alt = (held & ModifierKeys.Alt) != 0,
                Shift = (held & ModifierKeys.Shift) != 0,
                Key = virtualKey,
            };
            _hotkeyBox.Text = CapturedText();
            ShowError(_error, null);
        }

        private void OnHotkeyUp(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            if (Keyboard.Modifiers == ModifierKeys.None)
                _hotkeyBox.Text = CapturedText();
        }

        private static Key PressedKey(KeyEventArgs e)
        {
            if (e.Key == Key.System)
                return e.SystemKey;
            if (e.Key == Key.ImeProcessed)
                return e.ImeProcessedKey;
            if (e.Key == Key.DeadCharProcessed)
                return e.DeadCharProcessedKey;
            return e.Key;
        }

        private static bool LeavesTheBox(Key key, ModifierKeys held)
        {
            bool plain = (held & (ModifierKeys.Control | ModifierKeys.Alt)) == 0;
            if (key == Key.Tab)
                return plain;
            return (key == Key.Escape || key == Key.Enter) && held == ModifierKeys.None;
        }

        private static bool IsModifier(Key key) =>
            key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt
            || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin;

        private static string HeldModifiers(ModifierKeys held)
        {
            var parts = new List<string>();
            if ((held & ModifierKeys.Control) != 0)
                parts.Add("Ctrl");
            if ((held & ModifierKeys.Alt) != 0)
                parts.Add("Alt");
            if ((held & ModifierKeys.Shift) != 0)
                parts.Add("Shift");
            return parts.Count == 0 ? string.Empty : string.Join("+", parts) + "+";
        }

        private bool TrySaveHotkey(int delay)
        {
            if (_keys.Key == 0)
            {
                ShowError(_error, Texts.ErrorNoHotkey);
                return false;
            }
            Result = new PressHotkeyAction
            {
                Ctrl = _keys.Ctrl,
                Alt = _keys.Alt,
                Shift = _keys.Shift,
                Key = _keys.Key,
                DelayMs = delay,
            };
            return true;
        }
    }
}
