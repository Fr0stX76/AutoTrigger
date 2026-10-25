using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AutoTrigger.Core.Engine;
using Microsoft.Win32.SafeHandles;

namespace AutoTrigger.Core.Platform
{
    public sealed class WindowsProcessSystem : IProcessSystem, IDisposable
    {
        private readonly ProcessScanner _scanner = new ProcessScanner();

        public void Snapshot(List<ProcessEntry> into) => _scanner.Scan(into);

        public ProcessDetails Query(int pid)
        {
            using (SafeKernelHandle process = Open(pid, NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION))
            {
                if (process.IsInvalid || !IsActive(process))
                    return null;
                string path = ImagePath(process);
                if (path == null)
                    return null;
                return new ProcessDetails
                {
                    Pid = pid,
                    Path = path,
                    CreationTime = CreationTime(process),
                };
            }
        }

        public IDisposable WatchExit(int pid, long creationTime, Action onExit)
        {
            SafeKernelHandle process = Open(pid, NativeMethods.SYNCHRONIZE | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION);
            try
            {
                if (process.IsInvalid)
                    return null;
                if (creationTime != 0)
                {
                    long actual = CreationTime(process);
                    // The pid already belongs to a newer process: the one we track has exited.
                    if (actual != 0 && actual != creationTime)
                    {
                        process.Dispose();
                        process = null;
                        ThreadPool.QueueUserWorkItem(_ => onExit());
                        return new ExitWatcher(null, null);
                    }
                }
                var watcher = new ExitWatcher(process, onExit);
                process = null;
                return watcher;
            }
            finally
            {
                process?.Dispose();
            }
        }

        public bool IsRunning(int pid, long creationTime)
        {
            using (SafeKernelHandle process = Open(pid, NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION))
            {
                if (process.IsInvalid)
                    // Access denied: the process exists but is protected.
                    return Marshal.GetLastWin32Error() == NativeMethods.ERROR_ACCESS_DENIED;
                if (creationTime != 0)
                {
                    long actual = CreationTime(process);
                    if (actual != 0 && actual != creationTime)
                        return false;
                }
                return IsActive(process);
            }
        }

        public StartedProgram Start(string path, string arguments, string workingDirectory, bool followExit)
        {
            var info = new ProcessStartInfo
            {
                FileName = path,
                Arguments = arguments ?? string.Empty,
                // Like the Run box: finds App Paths names such as notepad.exe and opens documents and links.
                UseShellExecute = true,
            };
            if (!string.IsNullOrEmpty(workingDirectory))
                info.WorkingDirectory = workingDirectory;

            Process process = Process.Start(info);
            if (process == null)
                return new StartedProgram();

            int pid = 0;
            try
            {
                pid = process.Id;
            }
            catch (InvalidOperationException)
            {
            }

            if (!followExit)
            {
                process.Dispose();
                return new StartedProgram { Pid = pid };
            }

            var exited = new TaskCompletionSource<bool>();
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += (s, e) => exited.TrySetResult(true);
                if (process.HasExited)
                    exited.TrySetResult(true);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is Win32Exception || ex is NotSupportedException)
            {
                process.Dispose();
                return new StartedProgram { Pid = pid };
            }
            exited.Task.ContinueWith(_ => process.Dispose(), TaskScheduler.Default);
            return new StartedProgram { Pid = pid, Exited = exited.Task };
        }

        public bool RequestClose(int pid)
        {
            var visible = new List<IntPtr>();
            var hidden = new List<IntPtr>();
            TopWindows((hwnd, owner, isVisible) =>
            {
                if (owner != pid)
                    return;
                if (isVisible)
                    visible.Add(hwnd);
                else if (!IsImeWindow(hwnd))
                    hidden.Add(hwnd);
            });
            // Like taskkill without /F: a program with no visible window, such as one in the tray or whose
            // window belongs to a helper process, is asked through its hidden windows.
            int asked = 0;
            foreach (IntPtr hwnd in visible.Count > 0 ? visible : hidden)
            {
                if (NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                    asked++;
            }
            return asked > 0;
        }

        public void WindowOwners(HashSet<int> into) =>
            TopWindows((hwnd, owner, isVisible) =>
            {
                if (isVisible && owner > 0 && !IsCloakedByApp(hwnd))
                    into.Add(owner);
            });

        // Windows without an owner are main windows; dialogs and popups have one.
        private static void TopWindows(Action<IntPtr, int, bool> each)
        {
            NativeMethods.EnumWindowsProc callback = (hwnd, lParam) =>
            {
                if (NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) == IntPtr.Zero)
                {
                    _ = NativeMethods.GetWindowThreadProcessId(hwnd, out int owner);
                    each(hwnd, owner, NativeMethods.IsWindowVisible(hwnd));
                }
                return true;
            };
            NativeMethods.EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
        }

        // Every GUI thread has these input method windows; closing them would only break typing in the program.
        private static bool IsImeWindow(IntPtr hwnd)
        {
            var name = new char[16];
            int length = NativeMethods.GetClassName(hwnd, name, name.Length);
            string className = new string(name, 0, Math.Max(0, length));
            return className == "IME" || className == "MSCTFIME UI";
        }

        // Suspended Store apps keep cloaked windows. Windows on other virtual desktops are cloaked by the shell instead and still count.
        private static bool IsCloakedByApp(IntPtr hwnd) =>
            NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
            && (cloaked & NativeMethods.DWM_CLOAKED_APP) != 0;

        public void Kill(int pid, long creationTime)
        {
            using (SafeKernelHandle process = Open(pid, NativeMethods.PROCESS_TERMINATE | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION))
            {
                if (process.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    // ERROR_INVALID_PARAMETER: the process has already exited.
                    if (error == 87)
                        return;
                    throw new Win32Exception(error);
                }
                // The process ended in the meantime and Windows gave its pid to another one, which must not be ended.
                if (creationTime != 0)
                {
                    long actual = CreationTime(process);
                    if (actual != 0 && actual != creationTime)
                        return;
                }
                if (!NativeMethods.TerminateProcess(process, 1))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (!IsActive(process))
                        return;
                    throw new Win32Exception(error);
                }
            }
        }

        public string GetCommandLine(int pid)
        {
            using (SafeKernelHandle process = Open(pid, NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION))
            {
                if (process.IsInvalid)
                    return null;
                // ProcessCommandLineInformation needs only limited query access, unlike reading the process memory.
                _ = NativeMethods.NtQueryInformationProcess(process, NativeMethods.ProcessCommandLineInformation, IntPtr.Zero, 0, out int needed);
                if (needed <= 0 || needed > 1024 * 1024)
                    return null;
                IntPtr buffer = Marshal.AllocHGlobal(needed);
                try
                {
                    int status = NativeMethods.NtQueryInformationProcess(process, NativeMethods.ProcessCommandLineInformation,
                        buffer, needed, out needed);
                    if (status != 0)
                        return null;
                    int length = (ushort)Marshal.ReadInt16(buffer);
                    IntPtr text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                    return text == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUni(text, length / 2);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        public void Dispose() => _scanner.Dispose();

        // Needs only PROCESS_QUERY_LIMITED_INFORMATION, which Windows grants even on elevated processes of the same user.
        internal static string ImagePath(SafeKernelHandle process)
        {
            var buffer = new char[1024];
            int size = buffer.Length;
            if (NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size))
                return new string(buffer, 0, size);
            if (Marshal.GetLastWin32Error() != NativeMethods.ERROR_INSUFFICIENT_BUFFER)
                return null;
            buffer = new char[32768];
            size = buffer.Length;
            return NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }

        private static SafeKernelHandle Open(int pid, uint access) => NativeMethods.OpenProcess(access, false, pid);

        private static bool IsActive(SafeKernelHandle process) =>
            NativeMethods.GetExitCodeProcess(process, out uint code) && code == NativeMethods.STILL_ACTIVE;

        private static long CreationTime(SafeKernelHandle process) =>
            NativeMethods.GetProcessTimes(process, out long created, out _, out _, out _) ? created : 0;

        private sealed class ExitWatcher : IDisposable
        {
            private readonly ProcessWaitHandle _handle;
            private readonly RegisteredWaitHandle _registration;
            private Action _onExit;

            public ExitWatcher(SafeKernelHandle process, Action onExit)
            {
                if (process == null)
                    return;
                _onExit = onExit;
                IntPtr raw = process.DangerousGetHandle();
                // The wait handle now owns the raw handle; the SafeHandle must not close it as well.
                process.SetHandleAsInvalid();
                _handle = new ProcessWaitHandle(new SafeWaitHandle(raw, true));
                _registration = ThreadPool.RegisterWaitForSingleObject(_handle, (state, timedOut) => Fire(), null, Timeout.Infinite, true);
            }

            private void Fire()
            {
                Interlocked.Exchange(ref _onExit, null)?.Invoke();
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _onExit, null);
                _registration?.Unregister(null);
                _handle?.Dispose();
            }
        }

        private sealed class ProcessWaitHandle : WaitHandle
        {
            public ProcessWaitHandle(SafeWaitHandle handle)
            {
                SafeWaitHandle = handle;
            }
        }
    }
}
