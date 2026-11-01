using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using AutoTrigger.Core.Json;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Text;

namespace AutoTrigger.Core.Storage
{
    public enum LoadSource
    {
        New,

        Main,

        Reset,

        Locked,
    }

    public sealed class LoadResult
    {
        public AppSettings Settings { get; set; }

        public LoadSource Source { get; set; }

        public Note Message { get; set; }

        public List<string> Warnings { get; } = new List<string>();
    }

    public sealed class SettingsStore : IDisposable
    {
        public const string FileName = "settings.json";
        public const string OldBackupName = "settings.bak";
        private const int SaveDelayMs = 500;
        private const int ReadAttempts = 5;
        private const int ReadRetryMs = 200;
        private const int KeptCopies = 3;

        private static readonly string[] CopyPatterns = { "settings.before-import-*.json", "settings.invalid-*.json" };

        private readonly object _saveLock = new object();
        private Timer _debounce;
        private string _pendingJson;
        private bool _readOnly;

        public SettingsStore(string directory)
        {
            Directory = directory;
            FilePath = Path.Combine(directory, FileName);
        }

        public string Directory { get; }

        public string FilePath { get; }

        public bool IsReadOnly => _readOnly;

        public static string DefaultDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoTrigger");

        public LoadResult Load()
        {
            var result = new LoadResult();
            if (!File.Exists(FilePath))
                return FirstStart(result);

            string text;
            try
            {
                text = ReadWithRetry(FilePath);
            }
            catch (Exception ex) when (IsMissing(ex))
            {
                return FirstStart(result);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // A file another program holds is never written over: its content would be lost.
                _readOnly = true;
                result.Settings = new AppSettings();
                result.Source = LoadSource.Locked;
                result.Message = new Note(() => Texts.SettingsLocked);
                Log.Error($"settings.json cannot be opened: {ex.Message}. Changes will not be saved.");
                return result;
            }

            try
            {
                result.Settings = SettingsJson.Read(JsonValue.Parse(text), result.Warnings);
                result.Source = LoadSource.Main;
                DeleteOldBackup();
                return result;
            }
            catch (Exception ex)
            {
                // A damaged or hand-edited file must never stop AutoTrigger from starting: it is set aside below.
                Log.Error($"settings.json cannot be read: {ex.Message}");
            }

            string moved = SetAside(FilePath);
            result.Settings = new AppSettings();
            if (moved == null)
            {
                // The damaged file is still in place: saving would overwrite it, and it may be the only copy.
                _readOnly = true;
                result.Source = LoadSource.Locked;
                result.Message = new Note(() => Texts.SettingsUnreadable);
                Log.Error(result.Message.English);
                return result;
            }
            result.Source = LoadSource.Reset;
            string file = Path.GetFileName(moved);
            result.Message = new Note(() => Texts.SettingsSetAside(file));
            Log.Error(result.Message.English);
            return result;
        }

        private static LoadResult FirstStart(LoadResult result)
        {
            result.Settings = new AppSettings();
            result.Source = LoadSource.New;
            Log.Info("No settings yet (first start)");
            return result;
        }

        private static bool IsMissing(Exception ex) => ex is FileNotFoundException || ex is DirectoryNotFoundException;

        private void DeleteOldBackup()
        {
            string oldBackup = Path.Combine(Directory, OldBackupName);
            if (!File.Exists(oldBackup))
                return;
            try
            {
                File.Delete(oldBackup);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Warning($"{OldBackupName} could not be deleted: {ex.Message}");
            }
        }

        private static string ReadWithRetry(string path)
        {
            // Editors, antivirus and sync tools hold the file for a moment; retrying avoids a false 'in use'.
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return File.ReadAllText(path, Encoding.UTF8);
                }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && !IsMissing(ex) && attempt < ReadAttempts)
                {
                    Thread.Sleep(ReadRetryMs);
                }
            }
        }

        // Null when the file can be neither moved nor copied.
        private static string SetAside(string path)
        {
            string target = Path.Combine(Path.GetDirectoryName(path),
                "settings.invalid-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json");
            try
            {
                File.Move(path, target);
                return target;
            }
            catch (Exception moveError) when (moveError is IOException || moveError is UnauthorizedAccessException)
            {
                try
                {
                    File.Copy(path, target, true);
                    return target;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Log.Error($"Could not set aside {path}: {ex.Message}");
                    return null;
                }
            }
        }

        private static string Serialize(AppSettings settings) => SettingsJson.Write(settings).ToJson(true) + "\r\n";

        private void WriteJson(string json)
        {
            lock (_saveLock)
            {
                if (_readOnly)
                    return;
                System.IO.Directory.CreateDirectory(Directory);
                // Write a temporary file, then swap it in: a crash or power loss never leaves a half-written file.
                string temp = FilePath + ".tmp";
                byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }

                if (!File.Exists(FilePath))
                {
                    File.Move(temp, FilePath);
                    return;
                }
                try
                {
                    File.Replace(temp, FilePath, null, true);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is PlatformNotSupportedException)
                {
                    Log.Warning($"Atomic replace failed ({ex.Message}); copying instead.");
                    File.Copy(temp, FilePath, true);
                    File.Delete(temp);
                }
            }
        }

        public void SaveSoon(AppSettings settings)
        {
            if (_readOnly)
                return;
            string json = Serialize(settings);
            lock (_saveLock)
            {
                _pendingJson = json;
                if (_debounce == null)
                    _debounce = new Timer(_ => FlushPending(), null, SaveDelayMs, Timeout.Infinite);
                else
                    _debounce.Change(SaveDelayMs, Timeout.Infinite);
            }
        }

        // The copies made before an import or of a damaged file would pile up; the most recent of each kind stay.
        public void RemoveOldCopies()
        {
            if (_readOnly || !System.IO.Directory.Exists(Directory))
                return;
            foreach (string pattern in CopyPatterns)
            {
                try
                {
                    string[] copies = System.IO.Directory.GetFiles(Directory, pattern);
                    // The date and time in the names sort them from oldest to newest.
                    Array.Sort(copies, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < copies.Length - KeptCopies; i++)
                    {
                        File.Delete(copies[i]);
                        Log.Info($"Old settings copy removed: {Path.GetFileName(copies[i])}");
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Log.Warning($"Old settings copies cannot be removed: {ex.Message}");
                }
            }
        }

        public string BackupBeforeImport()
        {
            FlushPending();
            lock (_saveLock)
            {
                if (_readOnly || !File.Exists(FilePath))
                    return null;
                string target = Path.Combine(Directory,
                    "settings.before-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json");
                File.Copy(FilePath, target, true);
                return target;
            }
        }

        public void Dispose()
        {
            FlushPending();
            lock (_saveLock)
            {
                _debounce?.Dispose();
                _debounce = null;
            }
        }

        public void FlushPending()
        {
            lock (_saveLock)
            {
                string pending = _pendingJson;
                _pendingJson = null;
                if (pending == null)
                    return;
                try
                {
                    WriteJson(pending);
                }
                catch (Exception ex)
                {
                    // Timer thread: a failed save is logged, and the next change tries again.
                    Log.Error("Settings could not be saved", ex);
                }
            }
        }
    }
}
