using System.Windows.Controls;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Ui
{
    internal sealed partial class ActionDialog
    {
        private StackPanel _hotkeyPanel;
        private HotkeyBox _hotkeyBox;

        private void BuildHotkey(PressHotkeyAction hotkey)
        {
            _hotkeyPanel = new StackPanel();
            _hotkeyBox = new HotkeyBox(hotkey?.Keys, Texts.PressKeys);
            _hotkeyBox.Captured += keys => ShowError(_error, null);
            _hotkeyPanel.Children.Add(Kit.Field(Texts.KeyboardShortcut, _hotkeyBox));
        }

        private bool TrySaveHotkey(int delay)
        {
            KeyCombination keys = _hotkeyBox.Keys;
            if (!keys.IsSet)
            {
                ShowError(_error, Texts.ErrorNoHotkey);
                return false;
            }
            Profile owner = ShortcutRules.OwnerOf(_settings, keys);
            if (owner != null)
            {
                ShowError(_error, Texts.Format(Texts.ErrorHotkeyIsShortcut, ProfileName(owner)));
                return false;
            }
            Result = new PressHotkeyAction { Keys = keys, DelayMs = delay };
            return true;
        }

        private static string ProfileName(Profile profile) =>
            string.IsNullOrWhiteSpace(profile.Name) ? Texts.Unnamed : profile.Name;
    }
}
