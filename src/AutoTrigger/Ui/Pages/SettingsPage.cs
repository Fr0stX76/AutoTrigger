using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Text;
using ThemeMode = AutoTrigger.Core.Model.ThemeMode;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;

namespace AutoTrigger.Ui
{
    internal sealed class SettingsPage : Grid, IPage
    {
        private static readonly KeyValuePair<ThemeMode, string>[] Themes =
        {
            new KeyValuePair<ThemeMode, string>(ThemeMode.Auto, Texts.ThemeAuto),
            new KeyValuePair<ThemeMode, string>(ThemeMode.Light, Texts.ThemeLight),
            new KeyValuePair<ThemeMode, string>(ThemeMode.Dark, Texts.ThemeDark),
        };

        private static readonly KeyValuePair<LogLevel, string>[] LogLevels =
        {
            new KeyValuePair<LogLevel, string>(LogLevel.Error, Texts.LogErrors),
            new KeyValuePair<LogLevel, string>(LogLevel.Warning, Texts.LogWarnings),
            new KeyValuePair<LogLevel, string>(LogLevel.Info, Texts.LogInfo),
            new KeyValuePair<LogLevel, string>(LogLevel.Detailed, Texts.LogDetailed),
        };

        private static readonly Thickness CardPadding = new Thickness(20, 4, 20, 4);

        private readonly SettingsController _settings;
        private readonly Func<bool> _isStartWithWindows;
        private readonly Action<bool> _setStartWithWindows;
        private readonly Action _openLog;
        private readonly Action _importFromAutoActions;
        private readonly Border _general = new Border();
        private readonly Border _advanced = new Border();
        private readonly string _runningLanguage;
        private readonly Action _restart;

        public SettingsPage(SettingsController settings, Func<bool> isStartWithWindows, Action<bool> setStartWithWindows,
            Action openLog, Action importFromAutoActions, string runningLanguage, Action restart)
        {
            _runningLanguage = runningLanguage;
            _restart = restart;
            _settings = settings;
            _isStartWithWindows = isStartWithWindows;
            _setStartWithWindows = setStartWithWindows;
            _openLog = openLog;
            _importFromAutoActions = importFromAutoActions;
            this.Bind(BackgroundProperty, Res.Layer);
            // Two cards of the same width, side by side even in the narrowest window.
            PageColumns.Fill(this, CardColumn(), CardColumn(),
                PageColumns.Column(Texts.General, _general, true),
                PageColumns.Column(Texts.Advanced, _advanced, true));
        }

        private AppSettings Settings => _settings.Current;

        private static ColumnDefinition CardColumn() =>
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 380, MaxWidth = 560 };

        public void OnShown()
        {
            _general.Child = Card(BuildGeneral());
            _advanced.Child = Card(BuildAdvanced());
        }

        // Aligned at the top, or the scrolling area stretches the card to the height of the column.
        private static Border Card(StackPanel lines)
        {
            Border card = Res.Card(lines, CardPadding);
            card.VerticalAlignment = VerticalAlignment.Top;
            return card;
        }

        private StackPanel BuildGeneral()
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

            list.Children.Add(Line(Texts.Theme, Kit.Combo(Themes, Settings.Theme, mode =>
            {
                Settings.Theme = mode;
                _settings.Commit();
                ThemeController.Apply(mode);
            }), true));

            AddLanguageLines(list);
            AddUpdateLines(list);
            return list;
        }

        private StackPanel BuildAdvanced()
        {
            var list = new StackPanel();

            list.Children.Add(NumberLine(Texts.MinimumFocusTime, Settings.FocusStabilityMs, 0, 5000, Texts.Milliseconds, v =>
            {
                Settings.FocusStabilityMs = v;
                _settings.Commit();
            }, true));

            list.Children.Add(NumberLine(Texts.StartDetectionInterval, Settings.StartPollMs, 50, 5000, Texts.Milliseconds, v =>
            {
                Settings.StartPollMs = v;
                _settings.Commit();
            }, true));

            list.Children.Add(Line(Texts.LogLevel, Kit.Combo(LogLevels, Settings.LogLevel, level =>
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
            return list;
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
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock label = Res.Text(text);
            label.TextWrapping = TextWrapping.Wrap;
            label.Margin = new Thickness(0, Spacing.Medium, Spacing.Large, Spacing.Medium);
            grid.Children.Add(label);
            control.VerticalAlignment = VerticalAlignment.Center;
            PlaceControl(control, false);
            grid.Children.Add(control);
            Kit.LabelFor(control, label);
            if (separator)
            {
                Border line = Kit.Separator(new Thickness(0));
                line.VerticalAlignment = VerticalAlignment.Bottom;
                Grid.SetColumnSpan(line, 2);
                Grid.SetRowSpan(line, 2);
                grid.Children.Add(line);
            }

            // As in the Settings app of Windows, a control that leaves no room for the longest word of its label
            // moves under it: a long language in the narrowest window.
            double labelNeed = text.Split(' ').Max(word => Kit.TextWidth(label, word, label.FontSize, FontWeights.Normal))
                + Spacing.Large;
            grid.SizeChanged += (s, e) => PlaceControl(control, e.NewSize.Width < labelNeed + control.DesiredSize.Width);
            return grid;
        }

        private static void PlaceControl(FrameworkElement control, bool below)
        {
            Grid.SetRow(control, below ? 1 : 0);
            Grid.SetColumn(control, below ? 0 : 1);
            Grid.SetColumnSpan(control, below ? 2 : 1);
            control.HorizontalAlignment = below ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            control.Margin = below ? new Thickness(0, 0, 0, Spacing.Medium) : new Thickness(0, Spacing.Medium, 0, Spacing.Medium);
        }

        private void AddLanguageLines(StackPanel list)
        {
            var restartNow = Kit.Button(Texts.RestartNow);
            restartNow.Click += (s, e) => _restart();
            Grid notice = Line(Texts.LanguageRestart, restartNow, true);
            notice.Visibility = NeedsRestart() ? Visibility.Visible : Visibility.Collapsed;

            List<string> available = Languages.Available();
            var choices = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(string.Empty, Texts.LanguageSystem) };
            choices.AddRange(available.Select(code => new KeyValuePair<string, string>(code, Languages.NativeName(code))));
            string selected = Settings.Language ?? string.Empty;
            if (selected == Languages.Pseudo)
                choices.Add(new KeyValuePair<string, string>(Languages.Pseudo, Languages.NativeName(Languages.Pseudo)));
            else if (!available.Contains(selected))
                selected = string.Empty;

            list.Children.Add(Line(Texts.Language, Kit.Combo(choices, selected, code =>
            {
                Settings.Language = code;
                Log.Info("Language setting: " + (code.Length == 0 ? "Windows display language" : code));
                _settings.Commit();
                notice.Visibility = NeedsRestart() ? Visibility.Visible : Visibility.Collapsed;
            }), true));
            list.Children.Add(notice);
        }

        private bool NeedsRestart() => Languages.Resolve(Settings.Language, Program.SystemCulture) != _runningLanguage;

        private void AddUpdateLines(StackPanel list)
        {
            StackPanel interval = NumberLine(Texts.CheckEvery, Settings.UpdateCheckHours, AppSettings.MinUpdateCheckHours,
                AppSettings.MaxUpdateCheckHours, Texts.Hours, v =>
                {
                    Settings.UpdateCheckHours = v;
                    _settings.Commit();
                }, false);
            interval.IsEnabled = Settings.CheckForUpdates;
            list.Children.Add(Line(Texts.CheckForUpdates, Kit.Switch(Settings.CheckForUpdates, on =>
            {
                Settings.CheckForUpdates = on;
                interval.IsEnabled = on;
                _settings.Commit();
            }, Texts.CheckForUpdates), false));
            list.Children.Add(interval);
        }

        private static StackPanel NumberLine(string text, int value, int min, int max, string unit, Action<int> commit, bool separator)
        {
            var panel = new StackPanel();
            TextBlock label = Res.Text(text);
            label.Margin = new Thickness(0, Spacing.Medium, 0, Spacing.Small);
            panel.Children.Add(label);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, Spacing.Medium) };
            TextBox box = Kit.NumberBox(value, min, max, commit, 80);
            Kit.LabelFor(box, label);
            row.Children.Add(box);
            TextBlock unitText = Kit.Caption(unit);
            unitText.Margin = Spacing.Left(Spacing.Small);
            row.Children.Add(unitText);
            panel.Children.Add(row);
            if (separator)
                panel.Children.Add(Kit.Separator(new Thickness(0)));
            return panel;
        }
    }
}
