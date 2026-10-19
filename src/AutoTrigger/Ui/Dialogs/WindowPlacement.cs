using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AutoTrigger.Ui
{
    internal static class WindowPlacement
    {
        private const double SpaceAbove = 0.45;

        public static void PlaceOver(Window dialog)
        {
            IntPtr hwnd = new WindowInteropHelper(dialog).Handle;
            if (hwnd == IntPtr.Zero || !NativeUi.GetWindowRect(hwnd, out NativeUi.RECT box) || !TryWorkArea(hwnd, out NativeUi.RECT work))
                return;
            NativeUi.RECT area = work;
            Window owner = dialog.Owner;
            if (owner != null && owner.WindowState == WindowState.Normal)
            {
                IntPtr ownerHwnd = new WindowInteropHelper(owner).Handle;
                if (ownerHwnd == IntPtr.Zero || !NativeUi.GetWindowRect(ownerHwnd, out area))
                    area = work;
            }
            int top = area.Top + (int)Math.Round((area.Height - box.Height) * SpaceAbove);
            MoveInside(hwnd, box, box.Left, top, work);
        }

        public static void KeepOnScreen(Window window)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !NativeUi.GetWindowRect(hwnd, out NativeUi.RECT box) || !TryWorkArea(hwnd, out NativeUi.RECT work))
                return;
            PresentationSource source = PresentationSource.FromVisual(window);
            if (source?.CompositionTarget != null && !double.IsNaN(window.Height))
                box.Bottom = box.Top + (int)Math.Ceiling(window.Height * source.CompositionTarget.TransformToDevice.M22);
            MoveInside(hwnd, box, box.Left, box.Top, work);
        }

        private static void MoveInside(IntPtr hwnd, NativeUi.RECT box, int left, int top, NativeUi.RECT work)
        {
            top = Math.Max(work.Top, Math.Min(top, work.Bottom - box.Height));
            left = Math.Max(work.Left, Math.Min(left, work.Right - box.Width));
            if (top == box.Top && left == box.Left)
                return;
            _ = NativeUi.SetWindowPos(hwnd, IntPtr.Zero, left, top, 0, 0,
                NativeUi.SWP_NOSIZE | NativeUi.SWP_NOZORDER | NativeUi.SWP_NOACTIVATE);
        }

        private static bool TryWorkArea(IntPtr hwnd, out NativeUi.RECT work)
        {
            work = default;
            IntPtr monitor = NativeUi.MonitorFromWindow(hwnd, NativeUi.MONITOR_DEFAULTTONEAREST);
            var info = new NativeUi.MONITORINFO { cbSize = Marshal.SizeOf<NativeUi.MONITORINFO>() };
            if (monitor == IntPtr.Zero || !NativeUi.GetMonitorInfo(monitor, ref info))
                return false;
            work = info.rcWork;
            return true;
        }
    }
}
