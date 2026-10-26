using System;
using System.IO;
using System.Security;

namespace AutoTrigger.Core.Engine
{
    public static class ConsoleProgram
    {
        private const int HeaderBytes = 4096;
        private const int PeOffsetField = 0x3C;
        // IMAGE_FILE_HEADER is 20 bytes; Subsystem is at the same place in PE32 and PE32+ optional headers.
        private const int SubsystemOffset = 4 + 20 + 68;
        private const ushort WindowsConsole = 3;

        // A .bat or .cmd runs in a console; an exe says so in its PE header. Only these can be started hidden safely:
        // a hidden window program would run with no way for the user to see or close it.
        public static bool Is(string path)
        {
            string file = (path ?? string.Empty).Trim();
            try
            {
                if (file.Length == 0 || !Path.IsPathRooted(file) || !File.Exists(file))
                    return false;
                string extension = Path.GetExtension(file);
                if (extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                    return false;
                var header = new byte[HeaderBytes];
                int read;
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    read = stream.Read(header, 0, header.Length);
                return IsConsoleImage(header, read);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException
                || ex is NotSupportedException || ex is SecurityException)
            {
                return false;
            }
        }

        public static bool IsConsoleImage(byte[] header, int length)
        {
            if (header == null || length < PeOffsetField + 4 || header[0] != 'M' || header[1] != 'Z')
                return false;
            int pe = BitConverter.ToInt32(header, PeOffsetField);
            if (pe <= 0 || pe > length - SubsystemOffset - 2)
                return false;
            if (header[pe] != 'P' || header[pe + 1] != 'E' || header[pe + 2] != 0 || header[pe + 3] != 0)
                return false;
            return BitConverter.ToUInt16(header, pe + SubsystemOffset) == WindowsConsole;
        }
    }
}
