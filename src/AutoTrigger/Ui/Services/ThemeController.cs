using System;
using System.Windows;
using AutoTrigger.Core.Logging;
using Wpf.Ui.Appearance;
using ThemeMode = AutoTrigger.Core.Model.ThemeMode;

namespace AutoTrigger.Ui
{
    internal static class ThemeController
    {
        public static void Apply(ThemeMode mode)
        {
            try
            {
                if (mode == ThemeMode.Auto || SystemParameters.HighContrast)
                    ApplicationThemeManager.ApplySystemTheme();
                else
                    ApplicationThemeManager.Apply(mode == ThemeMode.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
            }
            catch (Exception ex)
            {
                Log.Error("The theme could not be applied", ex);
            }
        }
    }
}
