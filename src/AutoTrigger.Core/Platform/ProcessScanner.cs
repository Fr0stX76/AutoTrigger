using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Logging;

namespace AutoTrigger.Core.Platform
{
    internal sealed class ProcessScanner : IDisposable
    {
        private const int SystemProcessInformation = 5;
        private const int SystemBasicProcessInformation = 252;
        private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
        private const int InitialBufferSize = 1024 * 1024;
        private const double RequiredAgreement = 0.9;

        internal const int EntryNextOffset = 0;
        internal const int EntryCreateTime = 32;
        internal const int EntryNameLength = 56;
        internal const int EntryNameBuffer = 64;
        internal const int EntryProcessId = 80;

        internal const int BasicNextOffset = 0;
        internal const int BasicProcessId = 8;
        internal const int BasicSequence = 24;
        internal const int BasicNameLength = 32;
        internal const int BasicNameMaximum = 34;
        internal const int BasicNameBuffer = 40;
        internal const int BasicEntrySize = 48;

        private readonly object _lock = new object();
        private readonly Dictionary<int, CachedName> _fullNames = new Dictionary<int, CachedName>();
        private readonly Dictionary<int, CachedName> _basicNames = new Dictionary<int, CachedName>();
        private readonly List<int> _stale = new List<int>();
        private IntPtr _buffer;
        private int _size;
        private int _generation;
        private bool? _basic;

        public void Scan(List<ProcessEntry> into)
        {
            lock (_lock)
            {
                if (_basic == null)
                {
                    _basic = ChooseList(into);
                    return;
                }
                if (_basic == true)
                {
                    if (TryQuery(SystemBasicProcessInformation, out int used) == 0 && ReadBasic(_buffer, used, into))
                        return;
                    _basic = false;
                    Log.Detailed("Process list: the light list could not be read; the full list is used from now on");
                }
                Query(SystemProcessInformation);
                Read(_buffer, into);
            }
        }

        private bool ChooseList(List<ProcessEntry> into)
        {
            var basic = new List<ProcessEntry>();
            int status = TryQuery(SystemBasicProcessInformation, out int used);
            bool readable = status == 0 && ReadBasic(_buffer, used, basic);
            Query(SystemProcessInformation);
            Read(_buffer, into);
            if (status != 0)
            {
                Log.Detailed($"Process list: full (the light list is not offered by this Windows, 0x{status:X8})");
                return false;
            }
            if (!readable || !Agrees(basic, into))
            {
                Log.Detailed("Process list: full (the light list did not match the full list)");
                return false;
            }
            into.Clear();
            into.AddRange(basic);
            Log.Detailed($"Process list: light ({basic.Count} processes)");
            return true;
        }

        internal static bool Agrees(List<ProcessEntry> basic, List<ProcessEntry> full)
        {
            if (basic.Count == 0 || full.Count == 0)
                return false;
            var names = new Dictionary<int, string>(full.Count);
            foreach (ProcessEntry e in full)
                names[e.Pid] = e.ExeName;
            int own = OwnPid();
            bool ownFound = own == 0;
            int matches = 0;
            foreach (ProcessEntry e in basic)
            {
                if (!names.TryGetValue(e.Pid, out string name) || !string.Equals(name, e.ExeName, StringComparison.OrdinalIgnoreCase))
                    continue;
                matches++;
                if (e.Pid == own)
                    ownFound = true;
            }
            return ownFound && matches >= RequiredAgreement * Math.Min(basic.Count, full.Count);
        }

        private static int OwnPid()
        {
            try
            {
                using (Process self = Process.GetCurrentProcess())
                    return self.Id;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private void EnsureBuffer()
        {
            if (_buffer != IntPtr.Zero)
                return;
            _size = InitialBufferSize;
            _buffer = Marshal.AllocHGlobal(_size);
        }

        private int TryQuery(int informationClass, out int used)
        {
            EnsureBuffer();
            used = 0;
            for (int attempt = 0; ; attempt++)
            {
                int status = NativeMethods.NtQuerySystemInformation(informationClass, _buffer, _size, out int needed);
                if (status == 0)
                {
                    used = needed > 0 && needed <= _size ? needed : _size;
                    return 0;
                }
                if (status != StatusInfoLengthMismatch || attempt >= 5)
                    return status;
                Marshal.FreeHGlobal(_buffer);
                _buffer = IntPtr.Zero;
                _size = Math.Max(needed, _size * 2) + 64 * 1024;
                _buffer = Marshal.AllocHGlobal(_size);
            }
        }

        private void Query(int informationClass)
        {
            int status = TryQuery(informationClass, out _);
            if (status != 0)
                throw new Win32Exception(NativeMethods.RtlNtStatusToDosError(status));
        }

        internal void Read(IntPtr buffer, List<ProcessEntry> into)
        {
            into.Clear();
            int generation = ++_generation;
            IntPtr entry = buffer;
            while (true)
            {
                int pid = (int)Marshal.ReadIntPtr(entry, EntryProcessId).ToInt64();
                if (pid != 0)
                {
                    long created = Marshal.ReadInt64(entry, EntryCreateTime);
                    int bytes = (ushort)Marshal.ReadInt16(entry, EntryNameLength);
                    IntPtr text = Marshal.ReadIntPtr(entry, EntryNameBuffer);
                    into.Add(new ProcessEntry(pid, NameOf(_fullNames, generation, pid, created, bytes, text)));
                }
                int next = Marshal.ReadInt32(entry, EntryNextOffset);
                if (next == 0)
                    break;
                entry += next;
            }
            DropStale(_fullNames, generation);
        }

        internal bool ReadBasic(IntPtr buffer, int used, List<ProcessEntry> into)
        {
            into.Clear();
            int generation = ++_generation;
            long offset = 0;
            long start = buffer.ToInt64();
            while (true)
            {
                if (offset + BasicEntrySize > used)
                    return false;
                IntPtr entry = new IntPtr(start + offset);
                int next = Marshal.ReadInt32(entry, BasicNextOffset);
                long pid = Marshal.ReadIntPtr(entry, BasicProcessId).ToInt64();
                long sequence = Marshal.ReadInt64(entry, BasicSequence);
                int bytes = (ushort)Marshal.ReadInt16(entry, BasicNameLength);
                int maximum = (ushort)Marshal.ReadInt16(entry, BasicNameMaximum);
                IntPtr text = Marshal.ReadIntPtr(entry, BasicNameBuffer);
                if (pid < 0 || pid > int.MaxValue || (bytes & 1) != 0 || bytes > maximum)
                    return false;
                if (bytes > 0)
                {
                    long at = text.ToInt64() - start;
                    if (text == IntPtr.Zero || at < 0 || at + bytes > used)
                        return false;
                }
                if (pid != 0)
                    into.Add(new ProcessEntry((int)pid, NameOf(_basicNames, generation, (int)pid, sequence, bytes, text)));
                if (next == 0)
                    break;
                if (next < BasicEntrySize || (next & 7) != 0)
                    return false;
                offset += next;
            }
            DropStale(_basicNames, generation);
            return into.Count > 0;
        }

        private static string NameOf(Dictionary<int, CachedName> cache, int generation, int pid, long stamp, int bytes, IntPtr text)
        {
            if (cache.TryGetValue(pid, out CachedName cached) && cached.Stamp == stamp)
            {
                cached.Seen = generation;
                return cached.Name;
            }
            string name = bytes > 0 && text != IntPtr.Zero ? Marshal.PtrToStringUni(text, bytes / 2) : string.Empty;
            if (cached == null)
                cache[pid] = cached = new CachedName();
            cached.Stamp = stamp;
            cached.Name = name;
            cached.Seen = generation;
            return name;
        }

        private void DropStale(Dictionary<int, CachedName> cache, int generation)
        {
            _stale.Clear();
            foreach (KeyValuePair<int, CachedName> pair in cache)
            {
                if (pair.Value.Seen != generation)
                    _stale.Add(pair.Key);
            }
            foreach (int pid in _stale)
                cache.Remove(pid);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(_buffer);
                _buffer = IntPtr.Zero;
            }
        }

        private sealed class CachedName
        {
            public long Stamp;
            public string Name;
            public int Seen;
        }
    }
}
