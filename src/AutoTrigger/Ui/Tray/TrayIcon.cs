using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Text;

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

        // A fixed light green: the accent color can be too close to the icon's own purple.
        private static readonly Color BadgeColor = Color.FromArgb(0x6C, 0xCB, 0x5F);

        private readonly HwndSource _window;
        private readonly uint _taskbarCreated;
        private readonly DispatcherTimer _retry;
        private IntPtr _normalIcon;
        private IntPtr _pausedIcon;
        private IntPtr _normalBadged;
        private IntPtr _pausedBadged;
        private string _update;
        private int _iconSize;
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
            // Sent when Explorer restarts; the icon has to be added again.
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

        public string UpdateVersion
        {
            get => _update;
            set
            {
                if (_update == value)
                    return;
                _update = value;
                LoadBadges();
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
                hIcon = CurrentIcon(),
                szTip = ToolTip(),
                szInfo = string.Empty,
                szInfoTitle = string.Empty,
            };
        }

        private IntPtr CurrentIcon()
        {
            if (_update != null)
                return _paused ? _pausedBadged : _normalBadged;
            return _paused ? _pausedIcon : _normalIcon;
        }

        private string ToolTip()
        {
            string state = _paused ? Texts.Paused : Texts.AppName;
            return _update == null ? state : state + " \u00B7 " + Texts.UpdateAvailable(_update);
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

        // The size follows the taskbar's DPI, which can change while AutoTrigger runs.
        private static int TrayIconSize()
        {
            IntPtr taskbar = NativeUi.FindWindow("Shell_TrayWnd", null);
            uint dpi = taskbar != IntPtr.Zero ? NativeUi.GetDpiForWindow(taskbar) : 0;
            int size = dpi > 0
                ? NativeUi.GetSystemMetricsForDpi(NativeUi.SM_CXSMICON, dpi)
                : NativeUi.GetSystemMetrics(NativeUi.SM_CXSMICON);
            return Math.Max(16, size);
        }

        private void RefreshIconSize()
        {
            if (TrayIconSize() == _iconSize)
                return;
            LoadIcons();
            Update();
        }

        private void LoadIcons()
        {
            FreeIcons();
            _iconSize = TrayIconSize();
            Log.Detailed($"Tray icon drawn at {_iconSize} px");
            IntPtr module = NativeUi.GetModuleHandle(null);
            if (NativeUi.LoadIconWithScaleDown(module, new IntPtr(AppIconResourceId), _iconSize, _iconSize, out _normalIcon) != 0)
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
                // The greyed icon is cosmetic; the normal icon is used if it cannot be drawn.
                Log.Detailed("Greyed tray icon not available: " + ex.Message);
                _pausedIcon = IntPtr.Zero;
            }
            if (_pausedIcon == IntPtr.Zero)
                _pausedIcon = _normalIcon;
            LoadBadges();
        }

        private void LoadBadges()
        {
            FreeBadges();
            _normalBadged = _normalIcon;
            _pausedBadged = _pausedIcon;
            if (_update == null || _normalIcon == IntPtr.Zero)
                return;
            try
            {
                _normalBadged = MakeBadged(_normalIcon);
                _pausedBadged = _pausedIcon == _normalIcon ? _normalBadged : MakeBadged(_pausedIcon);
            }
            catch (Exception ex)
            {
                // The badge is cosmetic (GDI+ drawing); the tooltip still tells about the update.
                Log.Detailed("Tray update badge not available: " + ex.Message);
                FreeBadges();
                _normalBadged = _normalIcon;
                _pausedBadged = _pausedIcon;
            }
        }

        // A dot at the bottom right, cut out from the icon by a thin transparent ring,
        // like the badges on Windows 11 taskbar buttons.
        private static IntPtr MakeBadged(IntPtr icon)
        {
            using (Icon source = Icon.FromHandle(icon))
            using (Bitmap bitmap = source.ToBitmap())
            {
                int size = Math.Min(bitmap.Width, bitmap.Height);
                float radius = Math.Max(8f, size * 0.5f) / 2;
                float cutout = radius + Math.Max(1f, size / 16f);
                float centerX = bitmap.Width - radius;
                float centerY = bitmap.Height - radius;
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        double distance = Math.Sqrt(Math.Pow(x + 0.5 - centerX, 2) + Math.Pow(y + 0.5 - centerY, 2));
                        double keep = Math.Max(0, Math.Min(1, distance - cutout + 0.5));
                        if (keep >= 1)
                            continue;
                        Color c = bitmap.GetPixel(x, y);
                        bitmap.SetPixel(x, y, Color.FromArgb((int)(c.A * keep), c.R, c.G, c.B));
                    }
                }
                using (Graphics graphics = Graphics.FromImage(bitmap))
                using (var brush = new SolidBrush(BadgeColor))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.FillEllipse(brush, centerX - radius, centerY - radius, radius * 2, radius * 2);
                }
                return bitmap.GetHicon();
            }
        }

        private void FreeBadges()
        {
            if (_pausedBadged != IntPtr.Zero && _pausedBadged != _pausedIcon && _pausedBadged != _normalBadged)
                NativeUi.DestroyIcon(_pausedBadged);
            if (_normalBadged != IntPtr.Zero && _normalBadged != _normalIcon)
                NativeUi.DestroyIcon(_normalBadged);
            _pausedBadged = IntPtr.Zero;
            _normalBadged = IntPtr.Zero;
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
            FreeBadges();
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
                    RefreshIconSize();
                }
                else if (msg == NativeUi.WM_DWMCOLORIZATIONCOLORCHANGED || msg == NativeUi.WM_THEMECHANGED)
                {
                    ThemeChanged?.Invoke();
                }
                else if (msg == NativeUi.WM_DISPLAYCHANGE)
                {
                    RefreshIconSize();
                    DisplaysChanged?.Invoke();
                }
                else if (msg == NativeUi.WM_DPICHANGED)
                {
                    RefreshIconSize();
                }
                // After sleep, monitors can come back in other modes; read them again.
                else if (msg == NativeUi.WM_POWERBROADCAST && (long)wParam == NativeUi.PBT_APMRESUMEAUTOMATIC)
                {
                    Log.Info("Resumed from sleep");
                    DisplaysChanged?.Invoke();
                }
            }
            catch (Exception ex)
            {
                // Exceptions must not escape a window procedure.
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
