using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Platform;

namespace AutoTrigger.Ui
{
    internal sealed class HotkeyService : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NOREPEAT = 0x4000;

        private readonly HwndSource _window;
        private readonly WindowsKeyboard _keyboard;
        private readonly Action<Guid, EventKind, KeyCombination> _pressed;
        private readonly Dictionary<int, Registration> _registered = new Dictionary<int, Registration>();
        private readonly HashSet<string> _inUse = new HashSet<string>();
        private List<Registration> _wanted = new List<Registration>();
        private string _signature = string.Empty;
        private bool _suspended;
        private bool _disposed;

        public HotkeyService(WindowsKeyboard keyboard, Action<Guid, EventKind, KeyCombination> pressed)
        {
            _keyboard = keyboard;
            _pressed = pressed;
            _window = new HwndSource(new HwndSourceParameters("AutoTrigger shortcuts")
            {
                ParentWindow = new IntPtr(-3),
                Width = 0,
                Height = 0,
                WindowStyle = 0,
            });
            _window.AddHook(WndProc);
        }

        public event Action Changed;

        public bool IsInUseElsewhere(Guid profileId, EventKind kind) => _inUse.Contains(Key(profileId, kind));

        private static string Key(Guid profileId, EventKind kind) => profileId.ToString("N") + ":" + kind;

        public void Update(IEnumerable<Profile> profiles)
        {
            if (_disposed)
                return;
            List<Registration> wanted = ShortcutRules.All(profiles)
                .Select(e => new Registration { ProfileId = e.Profile.Id, Event = e.Event, Keys = e.Keys.Copy() })
                .ToList();
            string signature = string.Join(";", wanted.Select(w => Key(w.ProfileId, w.Event) + "=" + w.Keys.GetHashCode()));
            if (signature == _signature)
                return;
            _signature = signature;
            _wanted = wanted;
            if (!_suspended)
                RegisterAll();
        }

        public void Suspend(bool suspended)
        {
            if (_disposed || _suspended == suspended)
                return;
            _suspended = suspended;
            if (suspended)
            {
                UnregisterAll();
                Log.Detailed("Keyboard shortcuts paused while one is being typed");
            }
            else
            {
                RegisterAll();
            }
        }

        private void RegisterAll()
        {
            UnregisterAll();
            _inUse.Clear();
            int id = 1;
            foreach (Registration registration in _wanted)
            {
                if (NativeUi.RegisterHotKey(_window.Handle, id, Modifiers(registration.Keys) | MOD_NOREPEAT, (uint)registration.Keys.Key))
                {
                    _registered[id] = registration;
                }
                else
                {
                    _inUse.Add(Key(registration.ProfileId, registration.Event));
                    Log.Warning($"Keyboard shortcut {KeyNames.Readable(registration.Keys)} cannot be used: another program already uses it");
                }
                id++;
            }
            Changed?.Invoke();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_HOTKEY || !_registered.TryGetValue(wParam.ToInt32(), out Registration registration))
                return IntPtr.Zero;
            handled = true;
            if (_keyboard != null && _keyboard.IsOwnEcho(registration.Keys))
            {
                Log.Detailed($"Keyboard shortcut {KeyNames.Readable(registration.Keys)} ignored: sent by a 'Hotkey' action");
                return IntPtr.Zero;
            }
            try
            {
                _pressed(registration.ProfileId, registration.Event, registration.Keys.Copy());
            }
            catch (Exception ex)
            {
                Log.Error("Keyboard shortcut handling failed", ex);
            }
            return IntPtr.Zero;
        }

        private static uint Modifiers(KeyCombination keys) =>
            (keys.Ctrl ? MOD_CONTROL : 0) | (keys.Alt ? MOD_ALT : 0) | (keys.Shift ? MOD_SHIFT : 0);

        private void UnregisterAll()
        {
            foreach (int id in _registered.Keys)
                NativeUi.UnregisterHotKey(_window.Handle, id);
            _registered.Clear();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            UnregisterAll();
            _window.RemoveHook(WndProc);
            _window.Dispose();
        }

        private sealed class Registration
        {
            public Guid ProfileId;
            public EventKind Event;
            public KeyCombination Keys;
        }
    }
}
