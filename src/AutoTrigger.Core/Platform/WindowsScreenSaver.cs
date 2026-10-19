using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Logging;

namespace AutoTrigger.Core.Platform
{
    public sealed class WindowsScreenSaver : IScreenSaver
    {
        private readonly object _lock = new object();
        private bool _turnedOffByUs;
        private bool _closing;

        public bool IsOn()
        {
            if (!NativeMethods.SystemParametersInfoBool(NativeMethods.SPI_GETSCREENSAVEACTIVE, 0, out bool active, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return active;
        }

        public void Set(bool on)
        {
            lock (_lock)
            {
                if (_closing && !on)
                    throw new InvalidOperationException("AutoTrigger is closing");
                if (!NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETSCREENSAVEACTIVE, on ? 1u : 0u, IntPtr.Zero,
                    NativeMethods.SPIF_SENDCHANGE))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                _turnedOffByUs = !on;
            }
        }

        public void RestoreIfTurnedOff()
        {
            lock (_lock)
            {
                _closing = true;
                if (!_turnedOffByUs)
                    return;
                try
                {
                    Set(true);
                    Log.Info("Screen saver turned back on (AutoTrigger is closing)");
                }
                catch (Exception ex)
                {
                    Log.Error("The screen saver could not be turned back on", ex);
                }
            }
        }
    }
}
