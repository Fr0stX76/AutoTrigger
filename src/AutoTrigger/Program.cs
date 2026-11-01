using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace AutoTrigger
{
    internal static class Program
    {
        // Internal argument: an AutoTrigger restarting itself to change language waits for the old one to close.
        public const string RestartedArgument = "/restarted";

        private const int RestartWaitMs = 10000;

        // The Windows display language, read before .NET's own texts are switched to English below.
        public static string SystemCulture { get; private set; } = string.Empty;

        [STAThread]
        private static int Main(string[] args)
        {
            SystemCulture = CultureInfo.CurrentUICulture.Name;
            // .NET's own texts stay English until the language of the window is known, so early messages never mix languages.
            UseEnglishForSystemTexts();
            int restartedFrom = RestartedFrom(args);
            Command command = Command.Parse(restartedFrom != 0 ? null : args);
            if (command.Kind == CommandKind.Invalid)
                return 2;
            if (restartedFrom != 0)
                WaitForExit(restartedFrom);
            using (SingleInstance instance = SingleInstance.TryAcquire())
            {
                if (instance == null)
                {
                    if (command.Kind != CommandKind.None)
                        return CommandChannel.Send(command);
                    SingleInstance.ActivateRunningInstance();
                    return 0;
                }
                var app = new App(instance, command, restartedFrom != 0);
                return app.Run();
            }
        }

        private static void UseEnglishForSystemTexts()
        {
            CultureInfo english = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = english;
            Thread.CurrentThread.CurrentUICulture = english;
        }

        private static int RestartedFrom(string[] args)
        {
            if (args == null || args.Length != 2 || !string.Equals(args[0], RestartedArgument, StringComparison.OrdinalIgnoreCase))
                return 0;
            return int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int pid) && pid > 0 ? pid : 0;
        }

        private static void WaitForExit(int pid)
        {
            try
            {
                using (Process previous = Process.GetProcessById(pid))
                    previous.WaitForExit(RestartWaitMs);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is Win32Exception)
            {
                // Already closed, or not visible to this user: there is nothing to wait for.
            }
        }
    }
}
