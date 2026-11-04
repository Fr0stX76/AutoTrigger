using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using AutoTrigger.Core.Displays;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Text;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;

namespace AutoTrigger.Ui
{
    internal sealed class StatusPage : Grid
    {
        private const string Separator = " \u00B7 ";
        private const double AppsMinWidth = 400;
        private const double AppsMaxWidth = 640;
        private const double CardMinWidth = 480;
        private const double CardMaxWidth = 640;

        private readonly AppIcons _icons;
        private readonly StackPanel _appList = new StackPanel();
        private readonly StateRow _hdr;
        private readonly StateRow _monitoring;
        private readonly AppIconBox _currentIcon = new AppIconBox(40, 8);
        private readonly TextBlock _currentName;
        private readonly TextBlock _currentProfile;
        private readonly TextBlock _profileLink;
        private readonly Run _profileRun = new Run();
        private readonly Grid _shortcuts = new Grid();
        private readonly Grid _displays = new Grid();
        private readonly ColumnDefinition _appsColumn = new ColumnDefinition { Width = GridLength.Auto, MinWidth = AppsMinWidth, MaxWidth = AppsMaxWidth };
        private string _shownShortcuts;
        private string _shownDisplays;
        private Guid _profileId;
        private Dictionary<Guid, AppEntry> _apps = new Dictionary<Guid, AppEntry>();
        private Guid _shownApp = Guid.NewGuid();
        private string _shownList;

        public StatusPage(AppIcons icons)
        {
            _icons = icons;
            this.Bind(BackgroundProperty, Res.Layer);

            _hdr = new StateRow(Texts.Hdr, Texts.StatusHdrOff);
            _hdr.Switch.Click += (s, e) => HdrChanged?.Invoke(_hdr.Switch.IsChecked == true);
            _monitoring = new StateRow(Texts.Monitoring, Texts.StatusMonitoringOff);
            _monitoring.Switch.Click += (s, e) => MonitoringChanged?.Invoke(_monitoring.Switch.IsChecked == true);
            // Both switches line up whatever the state texts of the language.
            string[] states = { Texts.StatusHdrOn, Texts.StatusHdrOff, Texts.StatusHdrUnavailable, Texts.StatusMonitoringOn, Texts.StatusMonitoringOff };
            double stateWidth = states.Max(text => Kit.TextWidth(this, text, 14, FontWeights.Normal));
            _hdr.TextWidth = stateWidth;
            _monitoring.TextWidth = stateWidth;
            _currentName = Res.Text(Texts.StatusNoApplication, 16);
            _currentProfile = Res.Text(Texts.StatusNoProfile, 14);
            _profileLink = Kit.Link(_profileRun, () =>
            {
                if (_profileId != Guid.Empty)
                    ProfileClicked?.Invoke(_profileId);
            });

            PageColumns.Fill(this,
                _appsColumn,
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = CardMinWidth + Spacing.Page },
                PageColumns.Column(Texts.Applications, BuildApplicationsCard(), false),
                PageColumns.Column(Texts.Monitoring, BuildStatusCards(), true));
            SizeChanged += (s, e) => FitApplicationsColumn();

            ShowCurrent(null, null);
        }

        // The cards keep their minimum width, page margin included; in a narrow window the applications give way.
        private void FitApplicationsColumn()
        {
            double room = Math.Max(0, ActualWidth - Spacing.Section - CardMinWidth - Spacing.Page);
            _appsColumn.MaxWidth = Math.Min(AppsMaxWidth, room);
            _appsColumn.MinWidth = Math.Min(AppsMinWidth, room);
        }

        private Border BuildApplicationsCard()
        {
            _appList.Margin = Spacing.Right(Spacing.Section);
            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _appList,
            };
            Border appCard = Res.Card(scroller, Spacing.ListInset);
            appCard.VerticalAlignment = VerticalAlignment.Top;
            return appCard;
        }

        private Grid BuildStatusCards()
        {
            var right = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetIsSharedSizeScope(right, true);
            for (int i = 0; i < 5; i++)
                right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            right.Children.Add(BuildMonitoringCard());
            AddCard(right, 1, Texts.TabDisplays, BuildDisplaysCard());
            AddCard(right, 3, Texts.KeyboardShortcuts, BuildShortcutsCard());
            SetShortcuts(Array.Empty<Profile>());
            ShowDisplays(Array.Empty<MonitorInfo>());
            return right;
        }

        private static void AddCard(Grid column, int row, string heading, Border card)
        {
            TextBlock title = Kit.Heading(heading);
            title.Margin = new Thickness(0, Spacing.Section, 0, Spacing.Medium);
            Grid.SetRow(title, row);
            column.Children.Add(title);
            Grid.SetRow(card, row + 1);
            column.Children.Add(card);
        }

        private Border BuildMonitoringCard()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(LabelColumn());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Spacing.Section) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var current = new StackPanel { Orientation = Orientation.Horizontal };
            current.Children.Add(_currentIcon);
            _currentName.Margin = Spacing.Left(Spacing.Medium);
            current.Children.Add(_currentName);

            _profileLink.Visibility = Visibility.Collapsed;
            var profilePanel = new Grid();
            profilePanel.Children.Add(_currentProfile);
            profilePanel.Children.Add(_profileLink);

            AddRow(grid, 0, Res.Text(Texts.Status, 14, FontWeights.SemiBold), _monitoring.Panel);
            AddRow(grid, 1, Res.Text(Texts.CurrentApplication, 14, FontWeights.SemiBold), current);
            AddRow(grid, 2, Res.Text(Texts.CurrentProfile, 14, FontWeights.SemiBold), profilePanel);
            return Card(grid, Spacing.Panel);
        }

        // The HDR switch, then one line per active display, filled by ShowDisplays.
        private Border BuildDisplaysCard()
        {
            _displays.ColumnDefinitions.Add(LabelColumn());
            _displays.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Spacing.Section) });
            _displays.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddRow(_displays, 0, Res.Text(Texts.Hdr, 14, FontWeights.SemiBold), _hdr.Panel);
            return Card(_displays, Spacing.Panel);
        }

        private static Border Card(Grid content, Thickness padding)
        {
            Border card = Res.Card(content, padding);
            card.VerticalAlignment = VerticalAlignment.Top;
            card.MinWidth = CardMinWidth;
            card.MaxWidth = CardMaxWidth;
            return card;
        }

        // The cards share the label width, which grows with the longest label of the language.
        private static ColumnDefinition LabelColumn() =>
            new ColumnDefinition { Width = GridLength.Auto, MinWidth = Kit.LabelWidth, SharedSizeGroup = "StatusLabels" };

        private Border BuildShortcutsCard()
        {
            _shortcuts.ColumnDefinitions.Add(LabelColumn());
            _shortcuts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Spacing.Section) });
            _shortcuts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _shortcuts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Spacing.Section) });
            _shortcuts.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            return Card(_shortcuts, new Thickness(24, 14, 24, 14));
        }

        public event Action<AppEntry> LaunchRequested;

        public event Action<AppEntry> ShortcutRequested;

        public event Action<bool> MonitoringChanged;

        public event Action<bool> HdrChanged;

        public event Action<Guid> ProfileClicked;

        public event Action DisplaysClicked;

        // Only the applications with something to launch are listed, each with what its button runs.
        public void SetApplications(IReadOnlyList<AppEntry> applications, IReadOnlyList<Profile> profiles)
        {
            _apps = new Dictionary<Guid, AppEntry>();
            foreach (AppEntry app in applications)
            {
                if (!_apps.ContainsKey(app.Id))
                    _apps[app.Id] = app;
            }
            var lines = applications.Where(a => a.Launch != LaunchKind.None)
                .Select(a => new KeyValuePair<AppEntry, string>(a, LaunchCaption(a, profiles)))
                .ToList();
            string shown = applications.Count + "\n" + string.Join("\n",
                lines.Select(l => l.Key.Id + "|" + l.Key.Name + "|" + l.Key.Path + "|" + l.Key.Enabled + "|" + l.Value));
            if (shown == _shownList)
                return;
            _shownList = shown;
            _appList.Children.Clear();
            if (lines.Count == 0)
            {
                var empty = Res.Text(applications.Count == 0 ? Texts.NoApplicationsYet : Texts.StatusNoApplication);
                empty.TextWrapping = TextWrapping.Wrap;
                empty.TextTrimming = TextTrimming.None;
                empty.Margin = new Thickness(Spacing.Medium);
                _appList.Children.Add(empty);
                return;
            }
            foreach (KeyValuePair<AppEntry, string> line in lines)
                _appList.Children.Add(AppRow(line.Key, line.Value));
        }

        private static string LaunchCaption(AppEntry app, IReadOnlyList<Profile> profiles) =>
            app.Launch == LaunchKind.Profile
                ? Texts.Profile + Separator + ActionText.ProfileName(profiles.FirstOrDefault(p => p.Id == app.LaunchProfileId)?.Name)
                : Texts.Program + Separator + app.FileName;

        private static string DisplayName(AppEntry app) => string.IsNullOrWhiteSpace(app.Name) ? app.FileName : app.Name;

        // Active displays only, the primary one first; it is named so only when there are several.
        public void ShowDisplays(IReadOnlyList<MonitorInfo> monitors)
        {
            bool several = monitors.Count > 1;
            var lines = monitors.OrderByDescending(m => m.IsPrimary)
                .Select(m => new KeyValuePair<string, string>(m.Name, DisplayText(m, several)))
                .ToList();
            string shown = string.Join("\n", lines.Select(l => l.Key + "|" + l.Value));
            if (shown == _shownDisplays)
                return;
            _shownDisplays = shown;
            foreach (UIElement old in _displays.Children.Cast<UIElement>().Where(c => Grid.GetRow(c) > 0).ToList())
                _displays.Children.Remove(old);
            while (_displays.RowDefinitions.Count > 1)
                _displays.RowDefinitions.RemoveAt(_displays.RowDefinitions.Count - 1);
            for (int i = 0; i < lines.Count; i++)
            {
                TextBlock name = Kit.Link(new Run(lines[i].Key), () => DisplaysClicked?.Invoke());
                name.FontWeight = FontWeights.SemiBold;
                AddRow(_displays, i + 1, name, Res.Text(lines[i].Value));
            }
        }

        private static string DisplayText(MonitorInfo monitor, bool several)
        {
            var parts = new List<string> { monitor.Desktop.ToString(), ActionText.Hz(monitor.RefreshNumerator, monitor.RefreshDenominator) };
            if (monitor.HdrOn)
                parts.Add(Texts.Hdr);
            if (several && monitor.IsPrimary)
                parts.Add(Texts.PrimaryShort);
            return string.Join(Separator, parts);
        }

        public void SetShortcuts(IEnumerable<Profile> profiles)
        {
            List<ShortcutEntry> entries = ShortcutRules.Sorted(profiles);
            string shown = string.Join("\n", entries.Select(e => e.Profile.Id + "|" + e.Profile.Name + "|" + e.Event + "|" + KeyNames.Readable(e.Keys)));
            if (shown == _shownShortcuts)
                return;
            _shownShortcuts = shown;
            _shortcuts.Children.Clear();
            _shortcuts.RowDefinitions.Clear();
            if (entries.Count == 0)
            {
                _shortcuts.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
                TextBlock none = Res.Text(Texts.StatusNoShortcuts);
                Grid.SetColumnSpan(none, 5);
                _shortcuts.Children.Add(none);
                return;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                ShortcutEntry entry = entries[i];
                _shortcuts.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
                TextBlock keys = Res.Text(KeyNames.Readable(entry.Keys), 14, FontWeights.SemiBold);
                Grid.SetRow(keys, i);
                _shortcuts.Children.Add(keys);
                Guid id = entry.Profile.Id;
                TextBlock profile = Kit.Link(new Run(ActionText.ProfileName(entry.Profile.Name)), () => ProfileClicked?.Invoke(id));
                Grid.SetRow(profile, i);
                Grid.SetColumn(profile, 2);
                _shortcuts.Children.Add(profile);
                TextBlock kind = Res.Text(ActionText.EventName(entry.Event));
                Grid.SetRow(kind, i);
                Grid.SetColumn(kind, 4);
                _shortcuts.Children.Add(kind);
            }
        }

        public void ShowEngine(EngineSnapshot snapshot)
        {
            bool on = snapshot.Monitoring && !snapshot.Paused;
            _monitoring.Show(on, on ? Texts.StatusMonitoringOn : Texts.StatusMonitoringOff, true);

            _apps.TryGetValue(snapshot.CurrentAppId, out AppEntry app);
            if (snapshot.CurrentAppId != _shownApp)
            {
                _shownApp = snapshot.CurrentAppId;
                ShowCurrent(app, snapshot.CurrentAppName);
            }
            _profileId = snapshot.CurrentProfileId;
            bool hasProfile = snapshot.CurrentProfileId != Guid.Empty;
            _profileRun.Text = hasProfile ? ActionText.ProfileName(snapshot.CurrentProfileName) : string.Empty;
            _profileLink.Visibility = hasProfile ? Visibility.Visible : Visibility.Collapsed;
            _currentProfile.Visibility = hasProfile ? Visibility.Collapsed : Visibility.Visible;
        }

        public void ShowHdr(HdrState hdr, bool changing)
        {
            if (!hdr.Available)
                _hdr.Show(false, Texts.StatusHdrUnavailable, false);
            else
                _hdr.Show(hdr.On, hdr.On ? Texts.StatusHdrOn : Texts.StatusHdrOff, !changing);
        }

        private void ShowCurrent(AppEntry app, string name)
        {
            if (app == null && string.IsNullOrEmpty(name))
            {
                _currentIcon.Visibility = Visibility.Collapsed;
                _currentName.Margin = new Thickness(0);
                _currentName.Text = Texts.StatusNoApplication;
                _currentName.FontSize = 14;
                _currentName.SetResourceReference(TextBlock.ForegroundProperty, Res.TextPrimary);
                return;
            }
            _currentIcon.Visibility = Visibility.Visible;
            _currentName.Margin = Spacing.Left(Spacing.Medium);
            _currentName.Text = name ?? app.Name;
            _currentName.FontSize = 16;
            _currentName.SetResourceReference(TextBlock.ForegroundProperty, Res.TextPrimary);
            _currentIcon.ShowInitials(name ?? app?.Name);
            if (app != null)
                _icons.Get(app.Path, image => { if (_shownApp == app.Id) _currentIcon.ShowImage(image); });
        }

        private Border AppRow(AppEntry app, string caption)
        {
            var icon = new AppIconBox(40, 8);
            icon.ShowInitials(app.Name);
            _icons.Get(app.Path, icon.ShowImage);

            var name = new StackPanel { Margin = new Thickness(Spacing.Medium, 0, Spacing.Medium, 0), VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(Res.Text(DisplayName(app)));
            TextBlock launches = Res.Text(caption, 12, null, Res.TextSecondary);
            launches.Margin = Spacing.Top(2);
            name.Children.Add(launches);

            if (!app.Enabled)
            {
                icon.Opacity = 0.45;
                name.Opacity = 0.45;
            }

            var launch = new LaunchButton();
            AutomationProperties.SetName(launch, Texts.Launch + " " + app.Name);
            launch.Click += (s, e) => LaunchRequested?.Invoke(app);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(icon);
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            Grid.SetColumn(launch, 2);
            grid.Children.Add(launch);

            var row = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(Spacing.Small),
                Background = Brushes.Transparent,
                Child = grid,
            };
            var shortcut = new MenuItem { Header = Texts.CreateDesktopShortcut };
            shortcut.Click += (s, e) => ShortcutRequested?.Invoke(app);
            row.ContextMenu = new ContextMenu { Items = { shortcut } };
            row.MouseEnter += (s, e) => row.SetResourceReference(Border.BackgroundProperty, Res.SubtleHover);
            row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
            return row;
        }

        private static void AddRow(Grid grid, int row, UIElement label, UIElement value)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var labelHost = new Border { Child = label, MinHeight = 40, Margin = Spacing.Top(row == 0 ? 0 : Spacing.Field) };
            Grid.SetRow(labelHost, row);
            grid.Children.Add(labelHost);

            var valueHost = new Border { Child = value, MinHeight = 40, Margin = labelHost.Margin, VerticalAlignment = VerticalAlignment.Center };
            if (value is FrameworkElement fe)
                fe.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(valueHost, row);
            Grid.SetColumn(valueHost, 2);
            grid.Children.Add(valueHost);
        }

        private sealed class StateRow
        {
            private readonly Ellipse _dot;
            private readonly TextBlock _text;

            public StateRow(string name, string offText)
            {
                _dot = new Ellipse { Width = 10, Height = 10, StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center };
                _text = Res.Text(offText);
                _text.MinWidth = 72;
                _text.Margin = new Thickness(10, 0, 10, 0);
                Switch = new ToggleSwitch { VerticalAlignment = VerticalAlignment.Center };
                AutomationProperties.SetName(Switch, name);
                Panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Panel.Children.Add(_dot);
                Panel.Children.Add(_text);
                Panel.Children.Add(Switch);
            }

            public StackPanel Panel { get; }

            public ToggleSwitch Switch { get; }

            public double TextWidth
            {
                set => _text.MinWidth = Math.Max(72, value);
            }

            public void Show(bool on, string text, bool enabled)
            {
                _text.Text = text;
                Switch.IsChecked = on;
                Switch.IsEnabled = enabled;
                if (on)
                {
                    _dot.SetResourceReference(Shape.FillProperty, Res.Success);
                    _dot.Stroke = null;
                }
                else
                {
                    _dot.Fill = Brushes.Transparent;
                    _dot.SetResourceReference(Shape.StrokeProperty, Res.TextPrimary);
                }
            }
        }
    }
}
