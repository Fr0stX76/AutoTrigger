using System;
using System.Globalization;
using System.Threading;

namespace AutoTrigger
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            UseEnglishForSystemTexts();
            using (SingleInstance instance = SingleInstance.TryAcquire())
            {
                if (instance == null)
                {
                    SingleInstance.ActivateRunningInstance();
                    return 0;
                }
                var app = new App(instance);
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
