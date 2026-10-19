using System;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Logging;

namespace AutoTrigger.Core.Platform
{
    public sealed class ForegroundWatcher : IForegroundSource, IDisposable
    {
        private const string StoreAppFrameClass = "ApplicationFrameWindow";

        private readonly NativeMethods.WinEventProc _callback;
        private IntPtr _hook;

        public ForegroundWatcher()
        {
            _callback = OnWinEvent;
            _hook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _callback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
            if (_hook == IntPtr.Zero)
                Log.Error("Focus changes cannot be followed (the Windows notification could not be set up)");
        }

        public event Action Changed;

        public int GetForegroundProcessId()
        {
            IntPtr window = NativeMethods.GetForegroundWindow();
            if (window == IntPtr.Zero)
                return 0;
            _ = NativeMethods.GetWindowThreadProcessId(window, out int pid);
            if (!IsStoreAppFrame(window))
                return pid;
            int hosted = HostedAppProcess(window, pid);
            return hosted != 0 ? hosted : ForegroundIds.EmptyFrame;
        }

        private static bool IsStoreAppFrame(IntPtr window)
        {
            var name = new char[64];
            int length = NativeMethods.GetClassName(window, name, name.Length);
            return length > 0 && string.Equals(new string(name, 0, length), StoreAppFrameClass, StringComparison.Ordinal);
        }

        private static int HostedAppProcess(IntPtr frame, int framePid)
        {
            int hosted = 0;
            NativeMethods.EnumChildProc visit = (child, lParam) =>
            {
                _ = NativeMethods.GetWindowThreadProcessId(child, out int pid);
                if (pid == 0 || pid == framePid)
                    return true;
                hosted = pid;
                return false;
            };
            NativeMethods.EnumChildWindows(frame, visit, IntPtr.Zero);
            GC.KeepAlive(visit);
            return hosted;
        }

        private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Error("Focus change handling failed", ex);
            }
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }
}
