using System;
using System.Globalization;
using System.Threading;

namespace AutoTrigger
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            UseEnglishForSystemTexts();
            Command command = Command.Parse(args);
            if (command.Kind == CommandKind.Invalid)
                return 2;
            using (SingleInstance instance = SingleInstance.TryAcquire())
            {
                if (instance == null)
                {
                    if (command.Kind != CommandKind.None)
                        return CommandChannel.Send(command);
                    SingleInstance.ActivateRunningInstance();
                    return 0;
                }
                var app = new App(instance, command);
                return app.Run();
            }
        }

        private static void UseEnglishForSystemTexts()
        {
            CultureInfo english = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = english;
            Thread.CurrentThread.CurrentUICulture = english;
        }
    }
}
