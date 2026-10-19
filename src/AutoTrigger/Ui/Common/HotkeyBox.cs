using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using AutoTrigger.Core.Model;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace AutoTrigger.Ui
{
    internal sealed class HotkeyBox : UiTextBox
    {
        public const double BoxWidth = 240;

        private static int _capturing;

        private readonly string _placeholder;
        private KeyCombination _keys;
        private bool _counted;

        public HotkeyBox(KeyCombination keys, string placeholder)
        {
            SetResourceReference(StyleProperty, typeof(UiTextBox));
            _keys = keys?.Copy() ?? new KeyCombination();
            _placeholder = placeholder ?? string.Empty;
            Width = BoxWidth;
            MinWidth = 0;
            HorizontalAlignment = HorizontalAlignment.Left;
            PlaceholderText = string.Empty;
            IsUndoEnabled = false;
            ClearButtonEnabled = true;
            AllowDrop = false;
            InputMethod.SetIsInputMethodEnabled(this, false);
            PreviewKeyDown += OnKeyDown;
            PreviewKeyUp += OnKeyUp;
            PreviewTextInput += (s, e) => e.Handled = true;
            DataObject.AddPastingHandler(this, (s, e) => e.CancelCommand());
            PreviewMouseRightButtonUp += (s, e) => e.Handled = true;
            ContextMenuOpening += (s, e) => e.Handled = true;
            IsKeyboardFocusWithinChanged += (s, e) =>
            {
                PlaceholderText = IsKeyboardFocusWithin ? _placeholder : string.Empty;
                SetCapturing(IsKeyboardFocusWithin);
                if (!IsKeyboardFocusWithin)
                    ShowKeys();
            };
            Unloaded += (s, e) => SetCapturing(false);
            ShowKeys();
        }

        public static event Action<bool> CapturingChanged;

        public event Action<KeyCombination> Captured;

        public event Action Cleared;

        public KeyCombination Keys
        {
            get => _keys.Copy();
            set
            {
                _keys = value?.Copy() ?? new KeyCombination();
                ShowKeys();
            }
        }

        private void SetCapturing(bool capturing)
        {
            if (capturing == _counted)
                return;
            _counted = capturing;
            _capturing += capturing ? 1 : -1;
            if (_capturing == (capturing ? 1 : 0))
                CapturingChanged?.Invoke(capturing);
        }

        private void ShowKeys() => Text = _keys.IsSet ? ActionText.HotkeyText(_keys) : string.Empty;

        protected override void OnClearButtonClick()
        {
            _keys = new KeyCombination();
            ShowKeys();
            Cleared?.Invoke();
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            Key key = PressedKey(e);
            ModifierKeys held = Keyboard.Modifiers;
            if (LeavesTheBox(key, held))
                return;
            e.Handled = true;
            if (IsModifier(key))
            {
                Text = HeldModifiers(held);
                return;
            }
            int virtualKey = KeyInterop.VirtualKeyFromKey(key);
            if (!KeyNames.CanSend(virtualKey))
                return;
            _keys = new KeyCombination
            {
                Ctrl = (held & ModifierKeys.Control) != 0,
                Alt = (held & ModifierKeys.Alt) != 0,
                Shift = (held & ModifierKeys.Shift) != 0,
                Key = virtualKey,
            };
            ShowKeys();
            Captured?.Invoke(_keys.Copy());
        }

        private void OnKeyUp(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            if (Keyboard.Modifiers == ModifierKeys.None)
                ShowKeys();
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
    }
}
