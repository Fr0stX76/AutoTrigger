using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoTrigger.Core.Engine;
using Microsoft.Win32.SafeHandles;

namespace AutoTrigger.Core.Platform
{
    public sealed class WindowsProcessSystem : IProcessSystem
    {
        private readonly ProcessScanner _scanner = new ProcessScanner();

        public void Snapshot(List<ProcessEntry> into) => _scanner.Scan(into);

        public ProcessDetails Query(int pid)
        {
            using (SafeKernelHandle process = Open(pid, NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION))
            {
                if (process.IsInvalid || !IsActive(process))
                    return null;
                var buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                if (!NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size))
                {
                    if (Marshal.GetLastWin32Error() != NativeMethods.ERROR_INSUFFICIENT_BUFFER)
                        return null;
                    buffer = new StringBuilder(32768);
                    size = buffer.Capacity;
                    if (!NativeMethods.QueryFullProcessImageName(process, 0, buffer, ref size))
                        return null;
                }
                return new ProcessDetails
                {
                    Pid = pid,
                    Path = buffer.ToString(0, size),
                    CreationTime = CreationTime(process),
                };
            }
        }

        public IDisposable WatchExit(int pid, long creationTime, Action onExit)
        {
            SafeKernelHandle process = Open(pid, NativeMethods.SYNCHRONIZE | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION);
            if (process.IsInvalid)
            {
                process.Dispose();
                return null;
            }
            if (creationTime != 0)
            {
                long actual = CreationTime(process);
                if (actual != 0 && actual != creationTime)
                {
                    process.Dispose();
                    ThreadPool.QueueUserWorkItem(_ => onExit());
                    return new ExitWatcher(null, null);
                }
            }
            return new ExitWatcher(process, onExit);
        }

        public bool IsRunning(int pid, long creationTime)
        {
            using (SafeKernelHandle process = Open(pid, NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION))
            {
                if (process.IsInvalid)
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
            int asked = 0;
            NativeMethods.EnumWindowsProc callback = (hwnd, lParam) =>
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out int owner);
                if (owner == pid && NativeMethods.IsWindowVisible(hwnd)
                    && NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) == IntPtr.Zero
                    && NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
                    asked++;
                return true;
            };
            NativeMethods.EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return asked > 0;
        }

        public void Kill(int pid)
        {
            using (SafeKernelHandle process = Open(pid, NativeMethods.PROCESS_TERMINATE | NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION))
            {
                if (process.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 87)
                        return;
                    throw new Win32Exception(error);
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
                NativeMethods.NtQueryInformationProcess(process, NativeMethods.ProcessCommandLineInformation, IntPtr.Zero, 0, out int needed);
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
