using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;
using ThemeMode = AutoTrigger.Core.Model.ThemeMode;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;

namespace AutoTrigger.Ui
{
    internal sealed class SettingsPage : Grid, IPage
    {
        private readonly SettingsController _settings;
        private readonly Func<bool> _isStartWithWindows;
        private readonly Action<bool> _setStartWithWindows;
        private readonly Action _openLog;
        private readonly Action _importFromAutoActions;
        private readonly Border _host = new Border();

        public SettingsPage(SettingsController settings, Func<bool> isStartWithWindows, Action<bool> setStartWithWindows,
            Action openLog, Action importFromAutoActions)
        {
            _settings = settings;
            _isStartWithWindows = isStartWithWindows;
            _setStartWithWindows = setStartWithWindows;
            _openLog = openLog;
            _importFromAutoActions = importFromAutoActions;
            this.Bind(BackgroundProperty, Res.Layer);
            var root = new StackPanel { Margin = new Thickness(32, 24, 32, 24), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
            root.Children.Add(Kit.Heading(Texts.TabSettings));
            _host.Margin = new Thickness(0, 12, 0, 0);
            root.Children.Add(_host);
            Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = root,
            });
        }

        private AppSettings Settings => _settings.Current;

        public void OnShown()
        {
            var list = new StackPanel();

            ToggleSwitch startup = Kit.Switch(_isStartWithWindows(), on => _setStartWithWindows(on), Texts.StartWithWindows);
            list.Children.Add(Line(Texts.StartWithWindows, startup, true));

            list.Children.Add(Line(Texts.StartMinimized, Kit.Switch(Settings.StartMinimized, on =>
            {
                Settings.StartMinimized = on;
                _settings.Commit();
            }, Texts.StartMinimized), true));

            list.Children.Add(Line(Texts.CloseToTray, Kit.Switch(Settings.CloseToTray, on =>
            {
                Settings.CloseToTray = on;
                _settings.Commit();
            }, Texts.CloseToTray), true));

            var themes = new List<KeyValuePair<ThemeMode, string>>
            {
                new KeyValuePair<ThemeMode, string>(ThemeMode.Auto, Texts.ThemeAuto),
                new KeyValuePair<ThemeMode, string>(ThemeMode.Light, Texts.ThemeLight),
                new KeyValuePair<ThemeMode, string>(ThemeMode.Dark, Texts.ThemeDark),
            };
            list.Children.Add(Line(Texts.Theme, Kit.Combo(themes, Settings.Theme, mode =>
            {
                Settings.Theme = mode;
                _settings.Commit();
                ThemeController.Apply(mode);
            }), true));

            list.Children.Add(NumberLine(Texts.FocusStability, Settings.FocusStabilityMs, 0, 5000, v =>
            {
                Settings.FocusStabilityMs = v;
                _settings.Commit();
            }));

            list.Children.Add(NumberLine(Texts.StartDetectionInterval, Settings.StartPollMs, 50, 5000, v =>
            {
                Settings.StartPollMs = v;
                _settings.Commit();
            }));

            var levels = new List<KeyValuePair<LogLevel, string>>
            {
                new KeyValuePair<LogLevel, string>(LogLevel.Error, Texts.LogErrors),
                new KeyValuePair<LogLevel, string>(LogLevel.Warning, Texts.LogWarnings),
                new KeyValuePair<LogLevel, string>(LogLevel.Info, Texts.LogInfo),
                new KeyValuePair<LogLevel, string>(LogLevel.Detailed, Texts.LogDetailed),
            };
            list.Children.Add(Line(Texts.LogLevel, Kit.Combo(levels, Settings.LogLevel, level =>
            {
                Settings.LogLevel = level;
                Log.Level = level;
                Log.Info("Log level: " + Log.LevelToText(level));
                _settings.Commit();
            }), true));

            var openLog = Kit.Button(Texts.OpenLog);
            openLog.Click += (s, e) => _openLog();
            list.Children.Add(Line(Texts.LogFile, openLog, true));

            var import = Kit.Button(Texts.Import);
            import.Click += (s, e) => _importFromAutoActions();
            list.Children.Add(Line(Texts.ImportFromAutoActions, import, true));

            var reset = Kit.Button(Texts.Reset);
            reset.Click += (s, e) => ResetToDefaults();
            list.Children.Add(Line(Texts.ResetToDefaults, reset, false));

            _host.Child = Res.Card(list, new Thickness(20, 4, 20, 4));
        }

        private void ResetToDefaults()
        {
            Log.Info("Settings reset to defaults");
            Settings.ResetPreferences();
            Log.Level = Settings.LogLevel;
            ThemeController.Apply(Settings.Theme);
            _settings.Commit();
            OnShown();
        }

        private static Grid Line(string text, FrameworkElement control, bool separator)
        {
            var grid = new Grid { MinHeight = 56 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock label = Res.Text(text);
            label.Margin = new Thickness(0, 12, 16, 12);
            grid.Children.Add(label);
            control.VerticalAlignment = VerticalAlignment.Center;
            control.Margin = new Thickness(0, 12, 0, 12);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            if (separator)
            {
                Border line = Kit.Separator(new Thickness(0));
                line.VerticalAlignment = VerticalAlignment.Bottom;
                Grid.SetColumnSpan(line, 2);
                grid.Children.Add(line);
            }
            return grid;
        }

        private static StackPanel NumberLine(string text, int value, int min, int max, Action<int> commit)
        {
            var panel = new StackPanel();
            TextBlock label = Res.Text(text);
            label.Margin = new Thickness(0, 12, 0, 8);
            panel.Children.Add(label);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            row.Children.Add(Kit.NumberBox(value, min, max, commit, 80));
            TextBlock unit = Kit.Caption(Texts.Milliseconds);
            unit.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(unit);
            panel.Children.Add(row);
            panel.Children.Add(Kit.Separator(new Thickness(0)));
            return panel;
        }
    }
}
