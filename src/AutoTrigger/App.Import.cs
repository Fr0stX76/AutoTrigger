using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Platform;
using AutoTrigger.Ui;

namespace AutoTrigger
{
    internal sealed partial class App
    {
        private void ImportFromAutoActions()
        {
            string file = PickAutoActionsFile();
            if (file == null)
                return;

            ImportResult result;
            try
            {
                result = AutoActionsImport.Read(File.ReadAllText(file, Encoding.UTF8));
            }
            catch (Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Error($"Import from AutoActions failed ({file}): {ex.Message}");
                ShowImportError(ex.Message);
                return;
            }

            if (HasContent() && !ConfirmReplace())
                return;

            string backup;
            try
            {
                backup = _store.BackupBeforeImport();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Error("The current settings could not be copied before the import", ex);
                ShowImportError(ex.Message);
                return;
            }

            ApplyImport(result);
            LogImport(result, file, backup);
            MessageDialog.Show(_window, Texts.ImportFromAutoActions, ImportSummary(result), Texts.Ok);
        }

        private string PickAutoActionsFile()
        {
            string found = null;
            try
            {
                found = AutoActionsLocator.FindSettingsFile(_processes);
            }
            catch (Exception ex)
            {
                Log.Detailed("AutoActions could not be looked for: " + ex.Message);
            }

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Texts.ImportFromAutoActions,
                Filter = Texts.ImportFilter,
                CheckFileExists = true,
            };
            if (found != null)
            {
                dialog.InitialDirectory = Path.GetDirectoryName(found);
                dialog.FileName = Path.GetFileName(found);
            }
            return dialog.ShowDialog(_window) == true ? dialog.FileName : null;
        }

        private bool HasContent() => _settings.Applications.Count > 0 || _settings.Profiles.Count > 0;

        private bool ConfirmReplace() =>
            MessageDialog.Show(_window, Texts.ImportFromAutoActions, Texts.ImportReplaceText, Texts.Replace, Texts.Cancel) == DialogChoice.Primary;

        private void ShowImportError(string reason) =>
            MessageDialog.Show(_window, Texts.ImportFromAutoActions, Texts.Format(Texts.ImportFailed, reason), Texts.Ok);

        private void ApplyImport(ImportResult result)
        {
            _settings.Applications.Clear();
            _settings.Applications.AddRange(result.Applications);
            _settings.Profiles.Clear();
            _settings.Profiles.AddRange(result.Profiles);
            _settings.Displays.Clear();
            _settings.Displays.AddRange(result.Displays);
            _controller.Commit();
            _displays.Refresh();
        }

        private static void LogImport(ImportResult result, string file, string backup)
        {
            string copied = backup != null ? $"; previous settings copied to {Path.GetFileName(backup)}" : string.Empty;
            Log.Info($"Imported from AutoActions ({file}): {result.Applications.Count} applications, {result.Profiles.Count} profiles, "
                + $"{result.ActionCount} actions, {result.Displays.Count} displays{copied}");
            foreach (string warning in result.Warnings)
                Log.Warning("Import: " + warning);
        }

        private static string ImportSummary(ImportResult result)
        {
            const int shownNotes = 8;
            var text = new StringBuilder(Texts.Format(Texts.ImportDone,
                result.Applications.Count, result.Profiles.Count, result.ActionCount, result.Displays.Count));
            List<string> programs = ProgramsStarted(result);
            if (programs.Count > 0)
            {
                text.Append("\n\n").Append(Texts.ImportPrograms);
                foreach (string program in programs.Take(shownNotes))
                    text.Append("\n• ").Append(program);
                if (programs.Count > shownNotes)
                    text.Append('\n').Append(Texts.Format(Texts.ImportMoreNotes, programs.Count - shownNotes));
            }
            if (result.Warnings.Count > 0)
            {
                text.Append("\n\n").Append(Texts.ImportNotes);
                foreach (string warning in result.Warnings.Take(shownNotes))
                    text.Append("\n• ").Append(warning);
                if (result.Warnings.Count > shownNotes)
                    text.Append('\n').Append(Texts.Format(Texts.ImportMoreNotes, result.Warnings.Count - shownNotes));
            }
            if (IsAutoActionsRunning())
                text.Append("\n\n").Append(Texts.ImportQuitAutoActions);
            if (result.AutoStart)
                text.Append("\n\n").Append(Texts.ImportAutoStart);
            return text.ToString();
        }

        private static List<string> ProgramsStarted(ImportResult result) =>
            result.Profiles
                .SelectMany(profile => ActionText.Events.SelectMany(profile.For))
                .OfType<RunProgramAction>()
                .Select(run => (run.Path ?? string.Empty).Trim())
                .Where(path => path.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private static bool IsAutoActionsRunning()
        {
            try
            {
                return AutoActionsLocator.IsRunning();
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
