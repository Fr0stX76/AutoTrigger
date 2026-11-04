using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AutoTrigger.Core.Engine;

namespace AutoTrigger.Core.Model
{
    // A desktop shortcut runs what the Launch button of its application runs: the program itself, or AutoTrigger
    // with /launch, so a later change of profile needs no new shortcut.
    public sealed class DesktopShortcut
    {
        public const int MaxCopies = 99;
        private const int MaxNameLength = 100;
        private const string InvalidNameCharacters = "<>:\"/\\|?*";

        public string Name { get; private set; } = string.Empty;

        public string Target { get; private set; } = string.Empty;

        public string Arguments { get; private set; } = string.Empty;

        public string WorkingDirectory { get; private set; } = string.Empty;

        public string IconPath { get; private set; } = string.Empty;

        public static string LaunchArguments(Guid appId) => "/launch " + appId.ToString("D", CultureInfo.InvariantCulture);

        public static DesktopShortcut For(AppEntry app, string autoTriggerPath)
        {
            string program = ActionExecutor.ExpandPath(app.Path);
            bool direct = app.Launch == LaunchKind.Program;
            string target = direct ? program : autoTriggerPath;
            return new DesktopShortcut
            {
                Name = FileName(app),
                Target = target,
                Arguments = direct ? string.Empty : LaunchArguments(app.Id),
                WorkingDirectory = ActionExecutor.ProgramFolder(target) ?? string.Empty,
                IconPath = program,
            };
        }

        // "Name.lnk", then "Name (2).lnk" and so on, for when a shortcut of that name is someone else's.
        public IEnumerable<string> FileNames()
        {
            yield return Name + ".lnk";
            for (int copy = 2; copy <= MaxCopies; copy++)
                yield return string.Format(CultureInfo.InvariantCulture, "{0} ({1}).lnk", Name, copy);
        }

        // The same shortcut, even one that still calls AutoTrigger where it was before it moved: making it again
        // repairs it. Any other shortcut is left alone.
        public bool IsSameAs(string target, string arguments)
        {
            if (!string.Equals((arguments ?? string.Empty).Trim(), Arguments, StringComparison.OrdinalIgnoreCase))
                return false;
            if (Arguments.Length == 0)
                return PathMatcher.SamePath(target ?? string.Empty, Target);
            return string.Equals(SafeFileName(target), SafeFileName(Target), StringComparison.OrdinalIgnoreCase);
        }

        private static string FileName(AppEntry app)
        {
            string name = string.IsNullOrWhiteSpace(app.Name) ? Path.GetFileNameWithoutExtension(SafeFileName(app.Path)) : app.Name;
            name = new string(name.Where(c => c >= ' ' && InvalidNameCharacters.IndexOf(c) < 0).ToArray()).Trim();
            if (name.Length > MaxNameLength)
                name = name.Substring(0, MaxNameLength);
            // Windows drops trailing dots and spaces from file names, which would turn two names into one.
            name = name.TrimEnd('.', ' ');
            return name.Length > 0 ? name : "AutoTrigger";
        }

        private static string SafeFileName(string path)
        {
            try
            {
                return Path.GetFileName(path ?? string.Empty);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }
    }
}
