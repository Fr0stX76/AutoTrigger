using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using AutoTrigger.Core.Logging;

namespace AutoTrigger.Ui
{
    internal enum TaskbarEdge
    {
        Bottom,
        Top,
        Left,
        Right,
    }

    internal sealed class TrayIcon : IDisposable
    {
        private const uint IconId = 1;
        private const int CallbackMessage = NativeUi.WM_APP + 1;
        private const int AppIconResourceId = 32512;

        private readonly HwndSource _window;
        private readonly uint _taskbarCreated;
        private readonly DispatcherTimer _retry;
        private IntPtr _normalIcon;
        private IntPtr _pausedIcon;
        private bool _added;
        private bool _paused;
        private int _retries;

        public TrayIcon()
        {
            _window = new HwndSource(new HwndSourceParameters("AutoTrigger tray")
            {
                WindowStyle = 0,
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0,
            });
            _window.AddHook(WndProc);
            _taskbarCreated = NativeUi.RegisterWindowMessage("TaskbarCreated");
            _retry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _retry.Tick += (s, e) => RetryAdd();
            LoadIcons();
            Add();
        }

        public event Action OpenRequested;

        public event Action<NativeUi.POINT> MenuRequested;

        public event Action ThemeChanged;

        public event Action DisplaysChanged;

        public bool Paused
        {
            get => _paused;
            set
            {
                if (_paused == value)
                    return;
                _paused = value;
                Update();
            }
        }

        public bool TryGetIconRect(out NativeUi.RECT rect)
        {
            var id = new NativeUi.NOTIFYICONIDENTIFIER
            {
                cbSize = (uint)Marshal.SizeOf<NativeUi.NOTIFYICONIDENTIFIER>(),
                hWnd = _window.Handle,
                uID = IconId,
            };
            return NativeUi.Shell_NotifyIconGetRect(ref id, out rect) == 0 && rect.Width > 0 && rect.Height > 0;
        }

        public void ReturnFocus()
        {
            if (!_added)
                return;
            NativeUi.NOTIFYICONDATAW data = NewData(0);
            NativeUi.Shell_NotifyIcon(NativeUi.NIM_SETFOCUS, ref data);
        }

        public static TaskbarEdge GetTaskbarEdge()
        {
            var data = new NativeUi.APPBARDATA { cbSize = (uint)Marshal.SizeOf<NativeUi.APPBARDATA>() };
            if (NativeUi.SHAppBarMessage(NativeUi.ABM_GETTASKBARPOS, ref data) == UIntPtr.Zero)
                return TaskbarEdge.Bottom;
            switch (data.uEdge)
            {
                case NativeUi.ABE_TOP: return TaskbarEdge.Top;
                case NativeUi.ABE_LEFT: return TaskbarEdge.Left;
                case NativeUi.ABE_RIGHT: return TaskbarEdge.Right;
                default: return TaskbarEdge.Bottom;
            }
        }

        private NativeUi.NOTIFYICONDATAW NewData(uint flags)
        {
            return new NativeUi.NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NativeUi.NOTIFYICONDATAW>(),
                hWnd = _window.Handle,
                uID = IconId,
                uFlags = flags,
                uCallbackMessage = CallbackMessage,
                hIcon = _paused ? _pausedIcon : _normalIcon,
                szTip = _paused ? Texts.Paused : Texts.AppName,
                szInfo = string.Empty,
                szInfoTitle = string.Empty,
            };
        }

        private void Add()
        {
            NativeUi.NOTIFYICONDATAW data = NewData(NativeUi.NIF_MESSAGE | NativeUi.NIF_ICON | NativeUi.NIF_TIP | NativeUi.NIF_SHOWTIP);
            NativeUi.Shell_NotifyIcon(NativeUi.NIM_DELETE, ref data);
            _added = NativeUi.Shell_NotifyIcon(NativeUi.NIM_ADD, ref data);
            if (_added)
            {
                data.uTimeoutOrVersion = NativeUi.NOTIFYICON_VERSION_4;
                NativeUi.Shell_NotifyIcon(NativeUi.NIM_SETVERSION, ref data);
                _retry.Stop();
            }
            else if (!_retry.IsEnabled)
            {
                _retries = 0;
                _retry.Start();
            }
        }

        private void RetryAdd()
        {
            if (_added || ++_retries > 60)
            {
                _retry.Stop();
                if (!_added)
                    Log.Error("The tray icon could not be added");
                return;
            }
            Add();
        }

        private void Update()
        {
            if (!_added)
                return;
            NativeUi.NOTIFYICONDATAW data = NewData(NativeUi.NIF_ICON | NativeUi.NIF_TIP | NativeUi.NIF_SHOWTIP);
            NativeUi.Shell_NotifyIcon(NativeUi.NIM_MODIFY, ref data);
        }

        private void LoadIcons()
        {
            FreeIcons();
            int cx = Math.Max(16, NativeUi.GetSystemMetrics(NativeUi.SM_CXSMICON));
            int cy = Math.Max(16, NativeUi.GetSystemMetrics(NativeUi.SM_CYSMICON));
            IntPtr module = NativeUi.GetModuleHandle(null);
            if (NativeUi.LoadIconWithScaleDown(module, new IntPtr(AppIconResourceId), cx, cy, out _normalIcon) != 0)
            {
                _normalIcon = IntPtr.Zero;
                Log.Warning("The tray icon image could not be loaded");
                return;
            }
            try
            {
                _pausedIcon = MakeGreyed(_normalIcon);
            }
            catch (Exception ex)
            {
                Log.Detailed("Greyed tray icon not available: " + ex.Message);
                _pausedIcon = IntPtr.Zero;
            }
            if (_pausedIcon == IntPtr.Zero)
                _pausedIcon = _normalIcon;
        }

        private static IntPtr MakeGreyed(IntPtr icon)
        {
            using (Icon source = Icon.FromHandle(icon))
            using (Bitmap bitmap = source.ToBitmap())
            {
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        Color c = bitmap.GetPixel(x, y);
                        int grey = (int)(c.R * 0.3 + c.G * 0.59 + c.B * 0.11);
                        if (grey < 200)
                            grey += (255 - grey) * 15 / 100;
                        bitmap.SetPixel(x, y, Color.FromArgb(c.A, grey, grey, grey));
                    }
                }
                return bitmap.GetHicon();
            }
        }

        private void FreeIcons()
        {
            if (_pausedIcon != IntPtr.Zero && _pausedIcon != _normalIcon)
                NativeUi.DestroyIcon(_pausedIcon);
            if (_normalIcon != IntPtr.Zero)
                NativeUi.DestroyIcon(_normalIcon);
            _pausedIcon = IntPtr.Zero;
            _normalIcon = IntPtr.Zero;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            try
            {
                if (msg == CallbackMessage)
                {
                    int evt = (int)((long)lParam & 0xFFFF);
                    switch (evt)
                    {
                        case NativeUi.WM_CONTEXTMENU:
                            long anchor = (long)wParam;
                            MenuRequested?.Invoke(new NativeUi.POINT { X = (short)(anchor & 0xFFFF), Y = (short)((anchor >> 16) & 0xFFFF) });
                            break;
                        case NativeUi.WM_LBUTTONDBLCLK:
                        case NativeUi.NIN_KEYSELECT:
                            OpenRequested?.Invoke();
                            break;
                    }
                    handled = true;
                }
                else if (msg == (int)_taskbarCreated && _taskbarCreated != 0)
                {
                    LoadIcons();
                    _added = false;
                    Add();
                }
                else if (msg == NativeUi.WM_SETTINGCHANGE)
                {
                    string area = lParam != IntPtr.Zero ? Marshal.PtrToStringUni(lParam) : null;
                    if (area == "ImmersiveColorSet")
                        ThemeChanged?.Invoke();
                }
                else if (msg == NativeUi.WM_DWMCOLORIZATIONCOLORCHANGED || msg == NativeUi.WM_THEMECHANGED)
                {
                    ThemeChanged?.Invoke();
                }
                else if (msg == NativeUi.WM_DISPLAYCHANGE)
                {
                    DisplaysChanged?.Invoke();
                }
                else if (msg == NativeUi.WM_POWERBROADCAST && (long)wParam == NativeUi.PBT_APMRESUMEAUTOMATIC)
                {
                    Log.Info("Resumed from sleep");
                    DisplaysChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Tray message handling failed", ex);
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            _retry.Stop();
            if (_added)
            {
                NativeUi.NOTIFYICONDATAW data = NewData(0);
                NativeUi.Shell_NotifyIcon(NativeUi.NIM_DELETE, ref data);
                _added = false;
            }
            FreeIcons();
            _window.RemoveHook(WndProc);
            _window.Dispose();
        }
    }
}
