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
                if (SystemParameters.HighContrast)
                    ApplicationThemeManager.ApplySystemTheme();
                else
                    ApplicationThemeManager.Apply(IsDark(mode) ? ApplicationTheme.Dark : ApplicationTheme.Light);
            }
            catch (Exception ex)
            {
                // Themes are switched by WPF UI; if it fails, the current theme stays.
                Log.Error("The theme could not be applied", ex);
            }
        }

        // Not WPF UI's own detection: it goes by the theme file name, and aero.theme counts as light even when apps are set to dark.
        private static bool IsDark(ThemeMode mode) => mode == ThemeMode.Auto ? !WindowsTheme.AppsUseLight : mode == ThemeMode.Dark;
    }
}
