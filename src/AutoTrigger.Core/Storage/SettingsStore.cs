using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using AutoTrigger.Core.Json;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;

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

        public string Message { get; set; }

        public List<string> Warnings { get; } = new List<string>();
    }

    public sealed class SettingsStore : IDisposable
    {
        public const string FileName = "settings.json";
        public const string OldBackupName = "settings.bak";
        private const int SaveDelayMs = 500;
        private const int ReadAttempts = 5;
        private const int ReadRetryMs = 200;

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
                _readOnly = true;
                result.Settings = new AppSettings();
                result.Source = LoadSource.Locked;
                result.Message = "settings.json could not be opened; another program may be using it. "
                    + "AutoTrigger started without your settings and will not save any change until it is restarted.";
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
                Log.Error($"settings.json cannot be read: {ex.Message}");
            }

            string moved = SetAside(FilePath);
            result.Settings = new AppSettings();
            result.Source = LoadSource.Reset;
            result.Message = $"settings.json could not be read and was set aside ({Path.GetFileName(moved)}). "
                + "AutoTrigger started with empty settings.";
            Log.Error(result.Message);
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
            catch (Exception ex)
            {
                Log.Warning($"{OldBackupName} could not be deleted: {ex.Message}");
            }
        }

        private static string ReadWithRetry(string path)
        {
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

        private static string SetAside(string path)
        {
            string target = Path.Combine(Path.GetDirectoryName(path),
                "settings.invalid-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json");
            try
            {
                File.Move(path, target);
            }
            catch (Exception)
            {
                try
                {
                    File.Copy(path, target, true);
                }
                catch (Exception ex)
                {
                    Log.Error($"Could not set aside {path}: {ex.Message}");
                }
            }
            return target;
        }

        private static string Serialize(AppSettings settings) => SettingsJson.Write(settings).ToJson(true) + "\r\n";

        private void WriteJson(string json)
        {
            lock (_saveLock)
            {
                if (_readOnly)
                    return;
                System.IO.Directory.CreateDirectory(Directory);
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
                    Log.Error("Settings could not be saved", ex);
                }
            }
        }
    }
}
