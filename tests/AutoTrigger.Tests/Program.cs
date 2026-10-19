using System;
using System.Globalization;
using System.IO;
using AutoTrigger.Core.Logging;

namespace AutoTrigger.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            string logDir = Path.Combine(Path.GetTempPath(), "AutoTrigger.Tests",
                DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            Console.WriteLine(Runner.RunAll(logDir));
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Console.WriteLine("Log: " + Log.HideUserProfile(Path.Combine(logDir, "AutoTrigger.log"), profile));
            return Runner.FailureCount == 0 ? 0 : 1;
        }
    }
}
