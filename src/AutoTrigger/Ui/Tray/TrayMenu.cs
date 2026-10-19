using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using AutoTrigger.Core.Logging;
using Microsoft.Win32;

namespace AutoTrigger.Ui
{
    internal enum TrayCommand
    {
        None,
        Open,
        Pause,
        StartWithWindows,
        Exit,
    }

    internal sealed partial class TrayMenu : IDisposable
    {
        private const uint IdOpen = 1;
        private const uint IdPause = 2;
        private const uint IdStartup = 3;
        private const uint IdExit = 4;

        private readonly HwndSource _owner;
        private readonly Win32.HookProc _filterProc;
        private readonly List<Item> _items = new List<Item>();
        private IntPtr _menu;
        private IntPtr _menuWindow;
        private Win32.RECT _icon;
        private bool _hasIcon;
        private bool _reopen;
        private bool _disposed;
        private Look _look;

        public TrayMenu()
        {
            _owner = new HwndSource(new HwndSourceParameters("AutoTrigger menu")
            {
                WindowStyle = unchecked((int)Win32.WS_POPUP),
                ExtendedWindowStyle = Win32.WS_EX_TOOLWINDOW,
                Width = 0,
                Height = 0,
            });
            _owner.AddHook(WndProc);
            _filterProc = MenuFilter;
        }

        public bool IsOpen { get; private set; }

        public TrayCommand Show(NativeUi.RECT? icon, NativeUi.POINT anchor, TaskbarEdge edge, bool paused, bool startWithWindows)
        {
            if (IsOpen || _disposed)
                return TrayCommand.None;

            var point = new Win32.POINT { X = anchor.X, Y = anchor.Y };
            _hasIcon = icon.HasValue;
            if (icon.HasValue)
            {
                NativeUi.RECT r = icon.Value;
                _icon = new Win32.RECT { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
                point = new Win32.POINT { X = r.Left + r.Width / 2, Y = r.Top + r.Height / 2 };
            }

            _items.Clear();
            _items.Add(new Item(IdOpen, Texts.Open, false, true));
            _items.Add(new Item(IdPause, Texts.Pause, paused, false));
            _items.Add(new Item(IdStartup, Texts.StartWithWindows, startWithWindows, false));
            _items.Add(new Item(IdExit, Texts.Exit, false, false));

            uint chosen = 0;
            IntPtr hook = IntPtr.Zero;
            try
            {
                _look = new Look(MonitorDpi(point));
                _menu = Win32.CreatePopupMenu();
                if (_menu == IntPtr.Zero || !Build())
                {
                    Log.Warning("The tray menu could not be prepared");
                    return TrayCommand.None;
                }

                IntPtr owner = _owner.Handle;
                Win32.SetWindowPos(owner, IntPtr.Zero, point.X, point.Y, 0, 0, Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                Win32.SetWindowPos(owner, IntPtr.Zero, 0, 0, 0, 0,
                    Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);

                if (!Win32.SetForegroundWindow(owner))
                {
                    Log.Warning("The tray menu did not get the focus; not shown");
                    return TrayCommand.None;
                }

                IsOpen = true;
                hook = Win32.SetWindowsHookEx(Win32.WH_MSGFILTER, _filterProc, IntPtr.Zero, Win32.GetCurrentThreadId());
                do
                {
                    _reopen = false;
                    _menuWindow = IntPtr.Zero;
                    chosen = Win32.TrackPopupMenuEx(_menu, Flags(edge) | Win32.TPM_RETURNCMD,
                        point.X, point.Y, owner, IntPtr.Zero);
                }
                while (_reopen && !_disposed && Win32.GetForegroundWindow() == owner);
            }
            catch (Exception ex)
            {
                Log.Error("The tray menu failed", ex);
            }
            finally
            {
                IsOpen = false;
                if (hook != IntPtr.Zero)
                    Win32.UnhookWindowsHookEx(hook);
                if (!_disposed)
                {
                    Win32.ShowWindow(_owner.Handle, Win32.SW_HIDE);
                    Win32.PostMessage(_owner.Handle, Win32.WM_NULL, IntPtr.Zero, IntPtr.Zero);
                }
                if (_menu != IntPtr.Zero)
                    Win32.DestroyMenu(_menu);
                _menu = IntPtr.Zero;
                _menuWindow = IntPtr.Zero;
                foreach (Item item in _items)
                    item.Dispose();
                _look?.Dispose();
                _look = null;
            }

            if (_disposed)
                return TrayCommand.None;
            switch (chosen)
            {
                case IdOpen: return TrayCommand.Open;
                case IdPause: return TrayCommand.Pause;
                case IdStartup: return TrayCommand.StartWithWindows;
                case IdExit: return TrayCommand.Exit;
                default: return TrayCommand.None;
            }
        }

        public void Close()
        {
            if (IsOpen)
                Win32.EndMenu();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            Close();
            _disposed = true;
            _owner.RemoveHook(WndProc);
            _owner.Dispose();
        }

        private static uint Flags(TaskbarEdge edge)
        {
            switch (edge)
            {
                case TaskbarEdge.Top: return Win32.TPM_LEFTALIGN | Win32.TPM_TOPALIGN;
                case TaskbarEdge.Right: return Win32.TPM_RIGHTALIGN | Win32.TPM_BOTTOMALIGN;
                default: return Win32.TPM_LEFTALIGN | Win32.TPM_BOTTOMALIGN;
            }
        }

        private static uint MonitorDpi(Win32.POINT point)
        {
            try
            {
                IntPtr monitor = Win32.MonitorFromPoint(point, Win32.MONITOR_DEFAULTTONEAREST);
                if (Win32.GetDpiForMonitor(monitor, Win32.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                    return dpiX;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
            }
            IntPtr dc = Win32.GetDC(IntPtr.Zero);
            try
            {
                int dpi = Win32.GetDeviceCaps(dc, Win32.LOGPIXELSX);
                return dpi > 0 ? (uint)dpi : 96;
            }
            finally
            {
                _ = Win32.ReleaseDC(IntPtr.Zero, dc);
            }
        }

        private bool Build()
        {
            var info = new Win32.MENUINFO
            {
                cbSize = (uint)Marshal.SizeOf<Win32.MENUINFO>(),
                fMask = Win32.MIM_BACKGROUND | Win32.MIM_STYLE,
                dwStyle = Win32.MNS_NOCHECK,
                hbrBack = _look.BackgroundBrush,
            };
            if (!Win32.SetMenuInfo(_menu, ref info))
                return false;

            IntPtr dc = Win32.GetDC(_owner.Handle);
            int textWidth = 0, textHeight = 0;
            try
            {
                foreach (Item item in _items)
                {
                    IntPtr old = Win32.SelectObject(dc, item.IsDefault ? _look.BoldFont : _look.Font);
                    Win32.GetTextExtentPoint32(dc, item.Text, item.Text.Length, out Win32.SIZE size);
                    Win32.SelectObject(dc, old);
                    textWidth = Math.Max(textWidth, size.cx);
                    textHeight = Math.Max(textHeight, size.cy);
                }
            }
            finally
            {
                _ = Win32.ReleaseDC(_owner.Handle, dc);
            }
            _look.ItemWidth = textWidth + _look.Scale(40);
            _look.ItemHeight = Math.Max(textHeight + _look.Scale(10), _look.Scale(28));

            foreach (Item item in _items)
            {
                uint flags = Win32.MF_OWNERDRAW | (item.Checked ? Win32.MF_CHECKED : 0);
                if (!Win32.AppendMenu(_menu, flags, (UIntPtr)item.Id, item.AccessibleInfo))
                    return false;
            }
            Win32.SetMenuDefaultItem(_menu, IdOpen, 0);
            return true;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (!IsOpen || _look == null)
                return IntPtr.Zero;
            try
            {
                switch (msg)
                {
                    case Win32.WM_MEASUREITEM:
                    {
                        var measure = Marshal.PtrToStructure<Win32.MEASUREITEMSTRUCT>(lParam);
                        if (measure.CtlType != Win32.ODT_MENU)
                            break;
                        measure.itemWidth = (uint)_look.ItemWidth;
                        measure.itemHeight = (uint)_look.ItemHeight;
                        Marshal.StructureToPtr(measure, lParam, false);
                        handled = true;
                        return new IntPtr(1);
                    }
                    case Win32.WM_DRAWITEM:
                    {
                        var draw = Marshal.PtrToStructure<Win32.DRAWITEMSTRUCT>(lParam);
                        if (draw.CtlType != Win32.ODT_MENU)
                            break;
                        Draw(draw);
                        handled = true;
                        return new IntPtr(1);
                    }
                    case Win32.WM_ENTERIDLE:
                        if ((long)wParam == Win32.MSGF_MENU)
                            RoundCorners();
                        break;
                    case Win32.WM_MENUCHAR:
                        handled = true;
                        return MenuChar((char)((long)wParam & 0xFFFF));
                    case Win32.WM_DPICHANGED:
                    case Win32.WM_DISPLAYCHANGE:
                        _reopen = false;
                        Win32.EndMenu();
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Tray menu message handling failed", ex);
            }
            return IntPtr.Zero;
        }

        private void Draw(Win32.DRAWITEMSTRUCT draw)
        {
            Item item = _items.Find(i => i.Id == draw.itemID);
            if (item == null)
                return;
            IntPtr dc = draw.hDC;
            int saved = Win32.SaveDC(dc);
            IntPtr pen = IntPtr.Zero;
            try
            {
                bool selected = (draw.itemState & Win32.ODS_SELECTED) != 0;
                bool disabled = (draw.itemState & (Win32.ODS_DISABLED | Win32.ODS_GRAYED)) != 0;
                uint ink = _look.Text;
                if (disabled)
                    ink = _look.DisabledText;
                else if (selected)
                    ink = _look.SelectedText;

                _ = Win32.SetDCBrushColor(dc, selected ? _look.Highlight : _look.Background);
                Win32.RECT rect = draw.rcItem;
                _ = Win32.FillRect(dc, ref rect, Win32.GetStockObject(Win32.DC_BRUSH));

                Win32.SelectObject(dc, item.IsDefault ? _look.BoldFont : _look.Font);
                _ = Win32.SetBkMode(dc, Win32.TRANSPARENT);
                _ = Win32.SetTextColor(dc, ink);
                Win32.RECT label = rect;
                label.Left += _look.Scale(32);
                label.Right -= _look.Scale(8);
                _ = Win32.DrawText(dc, item.Text, item.Text.Length, ref label,
                    Win32.DT_LEFT | Win32.DT_VCENTER | Win32.DT_SINGLELINE | Win32.DT_NOPREFIX);

                if ((draw.itemState & Win32.ODS_CHECKED) != 0)
                {
                    pen = Win32.CreatePen(Win32.PS_SOLID, Math.Max(1, _look.Scale(1)), ink);
                    Win32.SelectObject(dc, pen);
                    int x = rect.Left + _look.Scale(8);
                    int y = rect.Top + (rect.Bottom - rect.Top - _look.Scale(16)) / 2;
                    var points = new[]
                    {
                        new Win32.POINT { X = x + _look.Scale(2), Y = y + _look.Scale(8) },
                        new Win32.POINT { X = x + _look.Scale(6), Y = y + _look.Scale(12) },
                        new Win32.POINT { X = x + _look.Scale(14), Y = y + _look.Scale(3) },
                    };
                    Win32.Polyline(dc, points, points.Length);
                }
            }
            finally
            {
                Win32.RestoreDC(dc, saved);
                if (pen != IntPtr.Zero)
                    Win32.DeleteObject(pen);
            }
        }

        private void RoundCorners()
        {
            if (_menuWindow != IntPtr.Zero)
                return;
            IntPtr found = IntPtr.Zero;
            Win32.EnumThreadWindows(Win32.GetCurrentThreadId(), (hwnd, _) =>
            {
                var name = new char[16];
                int length = Win32.IsWindowVisible(hwnd) ? Win32.GetClassName(hwnd, name, name.Length) : 0;
                if (length > 0 && new string(name, 0, length) == "#32768")
                {
                    found = hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            if (found == IntPtr.Zero)
                return;
            _menuWindow = found;
            int preference = Win32.DWMWCP_ROUNDSMALL;
            try
            {
                _ = Win32.DwmSetWindowAttribute(found, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
            }
        }

        private IntPtr MenuChar(char key)
        {
            char upper = char.ToUpperInvariant(key);
            for (int i = 0; i < _items.Count; i++)
            {
                if (char.ToUpperInvariant(_items[i].Text[0]) == upper)
                    return new IntPtr((Win32.MNC_EXECUTE << 16) | i);
            }
            return IntPtr.Zero;
        }

        private IntPtr MenuFilter(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code == Win32.MSGF_MENU && lParam != IntPtr.Zero)
                {
                    var message = Marshal.PtrToStructure<Win32.MSG>(lParam);
                    if (IsRightButton(message.message))
                    {
                        if (_menuWindow != IntPtr.Zero && Win32.GetWindowRect(_menuWindow, out Win32.RECT menu) && Contains(menu, message.pt))
                            return new IntPtr(1);
                        if (_hasIcon && Contains(_icon, message.pt))
                        {
                            bool pressed = message.message != Win32.WM_RBUTTONUP && message.message != Win32.WM_NCRBUTTONUP;
                            if (pressed && !_reopen)
                            {
                                _reopen = true;
                                Win32.EndMenu();
                            }
                            return new IntPtr(1);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Tray menu filter failed", ex);
            }
            return Win32.CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        private static bool IsRightButton(uint message) =>
            message == Win32.WM_RBUTTONDOWN || message == Win32.WM_RBUTTONUP || message == Win32.WM_RBUTTONDBLCLK
            || message == Win32.WM_NCRBUTTONDOWN || message == Win32.WM_NCRBUTTONUP || message == Win32.WM_NCRBUTTONDBLCLK;

        private static bool Contains(Win32.RECT rect, Win32.POINT point) =>
            point.X >= rect.Left && point.X < rect.Right && point.Y >= rect.Top && point.Y < rect.Bottom;

        private sealed class Item : IDisposable
        {
            public Item(uint id, string text, bool isChecked, bool isDefault)
            {
                Id = id;
                Text = text;
                Checked = isChecked;
                IsDefault = isDefault;
                _text = Marshal.StringToHGlobalUni(text);
                AccessibleInfo = Marshal.AllocHGlobal(Marshal.SizeOf<Win32.MSAAMENUINFO>());
                Marshal.StructureToPtr(new Win32.MSAAMENUINFO
                {
                    dwMSAASignature = Win32.MSAA_MENU_SIG,
                    cchWText = (uint)text.Length,
                    pszWText = _text,
                }, AccessibleInfo, false);
            }

            private readonly IntPtr _text;

            public uint Id { get; }

            public string Text { get; }

            public bool Checked { get; }

            public bool IsDefault { get; }

            public IntPtr AccessibleInfo { get; private set; }

            public void Dispose()
            {
                if (AccessibleInfo == IntPtr.Zero)
                    return;
                Marshal.FreeHGlobal(AccessibleInfo);
                Marshal.FreeHGlobal(_text);
                AccessibleInfo = IntPtr.Zero;
            }
        }

        private sealed class Look : IDisposable
        {
            private readonly uint _dpi;

            public Look(uint dpi)
            {
                _dpi = dpi;
                var contrast = new Win32.HIGHCONTRAST { cbSize = (uint)Marshal.SizeOf<Win32.HIGHCONTRAST>() };
                bool highContrast = Win32.SystemParametersInfo(Win32.SPI_GETHIGHCONTRAST, contrast.cbSize, ref contrast, 0)
                    && (contrast.dwFlags & Win32.HCF_HIGHCONTRASTON) != 0;
                bool dark = !highContrast && !TaskbarIsLight();
                if (highContrast)
                {
                    Background = Win32.GetSysColor(Win32.COLOR_MENU);
                    Text = Win32.GetSysColor(Win32.COLOR_MENUTEXT);
                    Highlight = Win32.GetSysColor(Win32.COLOR_HIGHLIGHT);
                    SelectedText = Win32.GetSysColor(Win32.COLOR_HIGHLIGHTTEXT);
                    DisabledText = Win32.GetSysColor(Win32.COLOR_GRAYTEXT);
                }
                else if (dark)
                {
                    Background = Rgb(32, 32, 32);
                    Text = Rgb(240, 240, 240);
                    Highlight = Rgb(55, 55, 55);
                    SelectedText = Text;
                    DisabledText = Rgb(140, 140, 140);
                }
                else
                {
                    Background = Rgb(249, 249, 249);
                    Text = Rgb(26, 26, 26);
                    Highlight = Rgb(230, 230, 230);
                    SelectedText = Text;
                    DisabledText = Rgb(118, 118, 118);
                }
                BackgroundBrush = Win32.CreateSolidBrush(Background);

                Win32.LOGFONTW font = MenuFont(dpi);
                Font = Win32.CreateFontIndirect(ref font);
                font.lfWeight = 600;
                BoldFont = Win32.CreateFontIndirect(ref font);
            }

            public uint Background { get; }

            public uint Text { get; }

            public uint Highlight { get; }

            public uint SelectedText { get; }

            public uint DisabledText { get; }

            public IntPtr BackgroundBrush { get; }

            public IntPtr Font { get; }

            public IntPtr BoldFont { get; }

            public int ItemWidth { get; set; }

            public int ItemHeight { get; set; }

            public int Scale(int value) => (int)Math.Round(value * _dpi / 96.0);

            public void Dispose()
            {
                if (Font != IntPtr.Zero)
                    Win32.DeleteObject(Font);
                if (BoldFont != IntPtr.Zero)
                    Win32.DeleteObject(BoldFont);
                if (BackgroundBrush != IntPtr.Zero)
                    Win32.DeleteObject(BackgroundBrush);
            }

            private static uint Rgb(int r, int g, int b) => (uint)(r | (g << 8) | (b << 16));

            private static bool TaskbarIsLight()
            {
                try
                {
                    object value = Registry.GetValue(
                        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
                    return value is int light && light != 0;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            private static Win32.LOGFONTW MenuFont(uint dpi)
            {
                var metrics = new Win32.NONCLIENTMETRICSW { cbSize = (uint)Marshal.SizeOf<Win32.NONCLIENTMETRICSW>() };
                try
                {
                    if (Win32.SystemParametersInfoForDpi(Win32.SPI_GETNONCLIENTMETRICS, metrics.cbSize, ref metrics, 0, dpi))
                        return metrics.lfMenuFont;
                }
                catch (EntryPointNotFoundException)
                {
                }
                Win32.SystemParametersInfo(Win32.SPI_GETNONCLIENTMETRICS, metrics.cbSize, ref metrics, 0);
                return metrics.lfMenuFont;
            }
        }
    }
}
