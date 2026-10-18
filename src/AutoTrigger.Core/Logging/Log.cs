using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace AutoTrigger.Core.Logging
{
    public enum LogLevel
    {
        Error = 0,
        Warning = 1,
        Info = 2,
        Detailed = 3,
    }

    public static class Log
    {
        public const string FileName = "AutoTrigger.log";
        private const long MaxFileBytes = 1024 * 1024;
        private const int KeptFiles = 3;
        private const int MaxQueuedLines = 10000;

        private static readonly BlockingCollection<string> Queue = new BlockingCollection<string>(new ConcurrentQueue<string>());
        private static readonly object StartLock = new object();
        private static Thread _writer;
        private static string _directory;
        private static volatile int _level = (int)LogLevel.Error;

        public static string FilePath => _directory == null ? null : Path.Combine(_directory, FileName);

        public static LogLevel Level
        {
            get => (LogLevel)_level;
            set => _level = (int)value;
        }

        public static void Start(string directory, LogLevel level)
        {
            lock (StartLock)
            {
                Level = level;
                if (_writer != null)
                    return;
                _directory = directory;
                System.IO.Directory.CreateDirectory(directory);
                _writer = new Thread(WriterLoop) { IsBackground = true, Name = "AutoTrigger log writer" };
                _writer.Start();
            }
        }

        public static void Stop(TimeSpan timeout)
        {
            Thread writer;
            lock (StartLock)
                writer = _writer;
            if (writer == null)
                return;
            Queue.CompleteAdding();
            writer.Join(timeout);
        }

        public static bool IsEnabled(LogLevel level) => (int)level <= _level;

        public static void Error(string message, Exception exception = null) => Write(LogLevel.Error, message, exception);

        public static void Warning(string message, Exception exception = null) => Write(LogLevel.Warning, message, exception);

        public static void Info(string message) => Write(LogLevel.Info, message, null);

        public static void Detailed(string message) => Write(LogLevel.Detailed, message, null);

        public static void Write(LogLevel level, string message, Exception exception)
        {
            if (!IsEnabled(level) || Queue.IsAddingCompleted)
                return;
            if (Queue.Count >= MaxQueuedLines)
                return;

            var sb = new StringBuilder(128);
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
            sb.Append(' ').Append(LevelTag(level)).Append(' ').Append(message);
            if (exception != null)
            {
                if (level >= LogLevel.Detailed || Level >= LogLevel.Detailed)
                    sb.Append(Environment.NewLine).Append(exception);
                else
                    sb.Append(" (").Append(exception.GetType().Name).Append(": ").Append(exception.Message).Append(')');
            }
            try
            {
                Queue.TryAdd(sb.ToString());
            }
            catch (InvalidOperationException)
            {
            }
        }

        private static string LevelTag(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Error: return "[ERROR]  ";
                case LogLevel.Warning: return "[WARNING]";
                case LogLevel.Info: return "[INFO]   ";
                default: return "[DETAIL] ";
            }
        }

        private static void WriterLoop()
        {
            var batch = new StringBuilder();
            foreach (string first in Queue.GetConsumingEnumerable())
            {
                batch.Clear();
                batch.Append(first).Append(Environment.NewLine);
                while (batch.Length < 64 * 1024 && Queue.TryTake(out string next))
                    batch.Append(next).Append(Environment.NewLine);
                try
                {
                    WriteBatch(batch.ToString());
                }
                catch (Exception)
                {
                }
            }
        }

        private static void WriteBatch(string text)
        {
            string path = FilePath;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    RotateIfNeeded(path);
                    using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                    using (var writer = new StreamWriter(fs, new UTF8Encoding(false)))
                        writer.Write(text);
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException)
                {
                    return;
                }
            }
        }

        private static void RotateIfNeeded(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxFileBytes)
                return;
            string dir = info.DirectoryName;
            string stem = Path.GetFileNameWithoutExtension(path);
            string Rotated(int n) => Path.Combine(dir, $"{stem}.{n}.log");

            string oldest = Rotated(KeptFiles - 1);
            if (File.Exists(oldest))
                File.Delete(oldest);
            for (int n = KeptFiles - 2; n >= 1; n--)
            {
                if (File.Exists(Rotated(n)))
                    File.Move(Rotated(n), Rotated(n + 1));
            }
            File.Move(path, Rotated(1));
        }

        public static LogLevel ParseLevel(string text, LogLevel fallback)
        {
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "errors":
                case "error":
                    return LogLevel.Error;
                case "warnings":
                case "warning":
                    return LogLevel.Warning;
                case "info":
                    return LogLevel.Info;
                case "detailed":
                    return LogLevel.Detailed;
                default:
                    return fallback;
            }
        }

        public static string LevelToText(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Warning: return "warnings";
                case LogLevel.Info: return "info";
                case LogLevel.Detailed: return "detailed";
                default: return "errors";
            }
        }
    }
}
