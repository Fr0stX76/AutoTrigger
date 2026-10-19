using System;
using System.Windows;
using System.Windows.Controls;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Platform;

namespace AutoTrigger.Ui
{
    internal sealed class ShortcutEditor : StackPanel
    {
        private readonly Profile _profile;
        private readonly SettingsController _settings;
        private readonly HotkeyService _hotkeys;
        private readonly HotkeyBox _box;
        private readonly TextBlock _message;
        private string _error;

        public ShortcutEditor(Profile profile, SettingsController settings, HotkeyService hotkeys)
        {
            _profile = profile;
            _settings = settings;
            _hotkeys = hotkeys;

            _box = new HotkeyBox(profile.Shortcut, Texts.PressKeys);
            _box.Captured += OnCaptured;
            _box.Cleared += Clear;
            Children.Add(_box);

            _message = Kit.Wrapped(string.Empty, 12, Res.Critical);
            _message.Margin = new Thickness(0, 6, 0, 0);
            Children.Add(_message);

            Loaded += (s, e) =>
            {
                if (_hotkeys != null)
                    _hotkeys.Changed += ShowState;
                ShowState();
            };
            Unloaded += (s, e) =>
            {
                if (_hotkeys != null)
                    _hotkeys.Changed -= ShowState;
            };
        }

        private void OnCaptured(KeyCombination keys)
        {
            _error = Check(keys);
            if (_error == null)
            {
                _profile.Shortcut = keys;
                _settings.Commit();
            }
            else
            {
                _box.Keys = _profile.Shortcut;
            }
            ShowState();
        }

        private void Clear()
        {
            _error = null;
            _box.Keys = null;
            if (_profile.Shortcut != null)
            {
                _profile.Shortcut = null;
                _settings.Commit();
            }
            ShowState();
        }

        private string Check(KeyCombination keys)
        {
            if (!KeyNames.CanBeShortcut(keys))
                return Texts.ErrorShortcutModifier;
            if (WindowsKeyboard.TypesCharacterWithAltGr(keys))
                return Texts.ErrorShortcutAltGr;
            Profile owner = ShortcutRules.OwnerOf(_settings.Current, keys, _profile.Id);
            if (owner != null)
                return Texts.Format(Texts.ErrorShortcutTaken, NameOf(owner));
            Profile sender = ShortcutRules.SenderOf(_settings.Current, keys);
            if (sender != null)
                return Texts.Format(Texts.ErrorShortcutIsHotkey, NameOf(sender));
            return null;
        }

        private static string NameOf(Profile profile) => string.IsNullOrWhiteSpace(profile.Name) ? Texts.Unnamed : profile.Name;

        private void ShowState()
        {
            string message = _error;
            if (message == null && _profile.Shortcut != null && _hotkeys != null && _hotkeys.IsInUseElsewhere(_profile.Id))
                message = Texts.ShortcutInUse;
            _message.Text = message ?? string.Empty;
            _message.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
