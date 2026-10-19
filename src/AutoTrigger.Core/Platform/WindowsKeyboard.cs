using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Core.Platform
{
    public sealed class WindowsKeyboard : IKeyboard
    {
        private const int ModifierGapMs = 20;
        private const int LeftShift = 0xA0;
        private const int LeftCtrl = 0xA2;
        private const int LeftAlt = 0xA4;
        private const int SentEchoMs = 500;
        private const int HeldKeysPollMs = 10;

        private static readonly int[] HeldModifiers = { 0x10, 0x11, 0x12, 0x5B, 0x5C };

        private readonly Lazy<bool> _selfAsAdmin = new Lazy<bool>(SelfRunsAsAdmin);
        private readonly object _sentLock = new object();
        private KeyCombination _sending;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private KeyCombination _lastSent;
        private long _lastSentAt;

        public bool IsOwnEcho(KeyCombination keys)
        {
            lock (_sentLock)
            {
                if (keys == null)
                    return false;
                if (keys.Equals(_sending))
                    return true;
                return keys.Equals(_lastSent) && _clock.ElapsedMilliseconds - _lastSentAt < SentEchoMs;
            }
        }

        public bool FrontWindowRunsAsAdmin()
        {
            if (_selfAsAdmin.Value)
                return false;
            IntPtr window = NativeMethods.GetForegroundWindow();
            if (window == IntPtr.Zero)
                return false;
            _ = NativeMethods.GetWindowThreadProcessId(window, out int pid);
            return pid != 0 && RunsAsAdmin(pid);
        }

        public bool WaitForHeldKeys(int timeoutMs)
        {
            var waited = Stopwatch.StartNew();
            while (AnyModifierDown())
            {
                if (waited.ElapsedMilliseconds >= timeoutMs)
                    return false;
                Thread.Sleep(HeldKeysPollMs);
            }
            return true;
        }

        private static bool AnyModifierDown()
        {
            foreach (int key in HeldModifiers)
            {
                if ((Native.GetAsyncKeyState(key) & 0x8000) != 0)
                    return true;
            }
            return false;
        }

        public void Press(KeyCombination keys, int holdMs)
        {
            lock (_sentLock)
                _sending = keys.Copy();
            List<int> modifiers = Modifiers(keys);
            var pressed = new Stack<int>();
            try
            {
                foreach (int modifier in modifiers)
                    Down(modifier, pressed);
                if (modifiers.Count > 0)
                    Thread.Sleep(ModifierGapMs);
                Down(keys.Key, pressed);
                Thread.Sleep(holdMs);
            }
            finally
            {
                Release(pressed, keys.Key);
                lock (_sentLock)
                {
                    _lastSent = _sending;
                    _lastSentAt = _clock.ElapsedMilliseconds;
                    _sending = null;
                }
            }
        }

        public static bool TypesCharacterWithAltGr(KeyCombination keys)
        {
            if (keys == null || !keys.Ctrl || !keys.Alt)
                return false;
            var state = new byte[256];
            state[0x11] = 0x80;
            state[0x12] = 0x80;
            state[LeftCtrl] = 0x80;
            state[0xA5] = 0x80;
            if (keys.Shift)
            {
                state[0x10] = 0x80;
                state[LeftShift] = 0x80;
            }
            var text = new char[8];
            uint scan = Native.MapVirtualKey((uint)keys.Key, Native.MAPVK_VK_TO_VSC);
            int result = Native.ToUnicodeEx((uint)keys.Key, scan, state, text, text.Length, Native.ToUnicodeNoStateChange,
                Native.GetKeyboardLayout(0));
            return result != 0;
        }

        private static List<int> Modifiers(KeyCombination keys)
        {
            var modifiers = new List<int>();
            if (keys.Ctrl)
                modifiers.Add(LeftCtrl);
            if (keys.Alt)
                modifiers.Add(LeftAlt);
            if (keys.Shift)
                modifiers.Add(LeftShift);
            return modifiers;
        }

        private static void Down(int key, Stack<int> pressed)
        {
            if (!Send(key, false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows refused the keys");
            pressed.Push(key);
        }

        private static void Release(Stack<int> pressed, int mainKey)
        {
            while (pressed.Count > 0)
            {
                int key = pressed.Pop();
                Send(key, true);
                if (key == mainKey && pressed.Count > 0)
                    Thread.Sleep(ModifierGapMs);
            }
        }

        private static bool Send(int key, bool up)
        {
            uint flags = up ? Native.KEYEVENTF_KEYUP : 0;
            if (KeyNames.IsExtended(key))
                flags |= Native.KEYEVENTF_EXTENDEDKEY;
            var input = new Native.INPUT
            {
                type = Native.INPUT_KEYBOARD,
                u = new Native.InputUnion
                {
                    ki = new Native.KEYBDINPUT
                    {
                        wVk = (ushort)key,
                        wScan = (ushort)Native.MapVirtualKey((uint)key, Native.MAPVK_VK_TO_VSC),
                        dwFlags = flags,
                    },
                },
            };
            return Native.SendInput(1, new[] { input }, Marshal.SizeOf<Native.INPUT>()) == 1;
        }

        public static string LayoutName(int key)
        {
            uint mapped = Native.MapVirtualKey((uint)key, Native.MAPVK_VK_TO_CHAR) & 0x7FFF;
            return mapped > 32 ? char.ToUpperInvariant((char)mapped).ToString() : null;
        }

        private static bool SelfRunsAsAdmin()
        {
            using (Process self = Process.GetCurrentProcess())
                return RunsAsAdmin(self.Id);
        }

        private static bool RunsAsAdmin(int pid)
        {
            using (SafeKernelHandle process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid))
            {
                if (process.IsInvalid || !Native.OpenProcessToken(process, Native.TOKEN_QUERY, out SafeKernelHandle token))
                    return false;
                using (token)
                {
                    return Native.GetTokenInformation(token, Native.TokenElevation, out int elevated, sizeof(int), out _)
                        && elevated != 0;
                }
            }
        }

        private static class Native
        {
            public const uint INPUT_KEYBOARD = 1;
            public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
            public const uint KEYEVENTF_KEYUP = 0x0002;
            public const uint MAPVK_VK_TO_VSC = 0;
            public const uint MAPVK_VK_TO_CHAR = 2;
            public const uint TOKEN_QUERY = 0x0008;
            public const int TokenElevation = 20;
            public const uint ToUnicodeNoStateChange = 4;

            [StructLayout(LayoutKind.Sequential)]
            public struct INPUT
            {
                public uint type;
                public InputUnion u;
            }

            [StructLayout(LayoutKind.Explicit)]
            public struct InputUnion
            {
                [FieldOffset(0)]
                public MOUSEINPUT mi;

                [FieldOffset(0)]
                public KEYBDINPUT ki;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MOUSEINPUT
            {
                public int dx;
                public int dy;
                public uint mouseData;
                public uint dwFlags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct KEYBDINPUT
            {
                public ushort wVk;
                public ushort wScan;
                public uint dwFlags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            [DllImport("user32.dll", SetLastError = true)]
            public static extern uint SendInput(uint count, INPUT[] inputs, int size);

            [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
            public static extern uint MapVirtualKey(uint code, uint mapType);

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            public static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyState,
                [Out] char[] buffer, int bufferSize, uint flags, IntPtr layout);

            [DllImport("user32.dll")]
            public static extern IntPtr GetKeyboardLayout(uint threadId);

            [DllImport("user32.dll")]
            public static extern short GetAsyncKeyState(int virtualKey);

            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool OpenProcessToken(SafeKernelHandle process, uint access, out SafeKernelHandle token);

            [DllImport("advapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetTokenInformation(SafeKernelHandle token, int informationClass, out int information,
                int length, out int returnLength);
        }
    }
}
