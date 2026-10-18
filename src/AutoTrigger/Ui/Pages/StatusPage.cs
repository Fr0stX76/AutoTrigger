using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Model;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;

namespace AutoTrigger.Ui
{
    internal sealed class StatusPage : Grid
    {
        private readonly AppIcons _icons;
        private readonly StackPanel _appList = new StackPanel();
        private readonly StateRow _hdr;
        private readonly StateRow _monitoring;
        private readonly AppIconBox _currentIcon = new AppIconBox(40, 8);
        private readonly TextBlock _currentName;
        private readonly TextBlock _currentProfile;
        private readonly TextBlock _profileLink;
        private readonly Run _profileRun = new Run();
        private Guid _profileId;
        private Dictionary<Guid, AppEntry> _apps = new Dictionary<Guid, AppEntry>();
        private Guid _shownApp = Guid.NewGuid();
        private string _shownList;

        public StatusPage(AppIcons icons)
        {
            _icons = icons;
            this.Bind(BackgroundProperty, Res.Layer);

            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 360, MaxWidth = 560 });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 380 });
            Margin = new Thickness(0);

            var left = new Grid { Margin = new Thickness(32, 24, 0, 24) };
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            left.Children.Add(Heading(Texts.Applications));
            _appList.Margin = new Thickness(0, 0, 24, 0);
            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _appList,
            };
            Border appCard = Res.Card(scroller, new Thickness(4));
            appCard.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(appCard, 1);
            left.Children.Add(appCard);
            Children.Add(left);

            var right = new Grid { Margin = new Thickness(0, 24, 32, 24), HorizontalAlignment = HorizontalAlignment.Left };
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.Children.Add(Heading(Texts.Status));

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _hdr = new StateRow(Texts.Hdr);
            _hdr.Switch.Click += (s, e) => HdrChanged?.Invoke(_hdr.Switch.IsChecked == true);
            _monitoring = new StateRow(Texts.Monitoring);
            _monitoring.Switch.Click += (s, e) => MonitoringChanged?.Invoke(_monitoring.Switch.IsChecked == true);

            _currentName = Res.Text(Texts.None, 16);
            var current = new StackPanel { Orientation = Orientation.Horizontal };
            current.Children.Add(_currentIcon);
            _currentName.Margin = new Thickness(12, 0, 0, 0);
            current.Children.Add(_currentName);

            _currentProfile = Res.Text(Texts.None, 14);
            var link = new Hyperlink(_profileRun) { TextDecorations = null, Cursor = System.Windows.Input.Cursors.Hand };
            link.SetResourceReference(TextElement.ForegroundProperty, Res.AccentFill);
            link.MouseEnter += (s, e) => link.TextDecorations = TextDecorations.Underline;
            link.MouseLeave += (s, e) => link.TextDecorations = null;
            link.Click += (s, e) =>
            {
                if (_profileId != Guid.Empty)
                    ProfileClicked?.Invoke(_profileId);
            };
            _profileLink = Res.Text(string.Empty, 14);
            _profileLink.Inlines.Add(link);
            _profileLink.Visibility = Visibility.Collapsed;
            var profilePanel = new Grid();
            profilePanel.Children.Add(_currentProfile);
            profilePanel.Children.Add(_profileLink);

            AddRow(grid, 0, Texts.Hdr, _hdr.Panel);
            AddRow(grid, 1, Texts.Monitoring, _monitoring.Panel);
            AddRow(grid, 2, Texts.CurrentApplication, current);
            AddRow(grid, 3, Texts.CurrentProfile, profilePanel);

            Border statusCard = Res.Card(grid, new Thickness(24, 20, 24, 20));
            statusCard.VerticalAlignment = VerticalAlignment.Top;
            statusCard.MinWidth = 480;
            statusCard.MaxWidth = 640;
            Grid.SetRow(statusCard, 1);
            right.Children.Add(statusCard);
            Grid.SetColumn(right, 2);
            Children.Add(right);

            ShowCurrent(null, null);
        }

        public event Action<AppEntry> LaunchRequested;

        public event Action<bool> MonitoringChanged;

        public event Action<bool> HdrChanged;

        public event Action<Guid> ProfileClicked;

        public void SetApplications(IReadOnlyList<AppEntry> applications)
        {
            _apps = new Dictionary<Guid, AppEntry>();
            foreach (AppEntry app in applications)
            {
                if (!_apps.ContainsKey(app.Id))
                    _apps[app.Id] = app;
            }
            string shown = string.Join("\n", applications.Select(a => a.Id + "|" + a.Name + "|" + a.Path + "|" + a.Enabled));
            if (shown == _shownList)
                return;
            _shownList = shown;
            _appList.Children.Clear();
            if (applications.Count == 0)
            {
                var empty = Res.Text(Texts.NoApplicationsYet);
                empty.TextWrapping = TextWrapping.Wrap;
                empty.TextTrimming = TextTrimming.None;
                empty.Margin = new Thickness(12);
                _appList.Children.Add(empty);
                return;
            }
            foreach (AppEntry app in applications)
                _appList.Children.Add(AppRow(app));
        }

        public void ShowEngine(EngineSnapshot snapshot)
        {
            bool on = snapshot.Monitoring && !snapshot.Paused;
            _monitoring.Show(on, on ? Texts.Enabled : Texts.Disabled, true);

            _apps.TryGetValue(snapshot.CurrentAppId, out AppEntry app);
            if (snapshot.CurrentAppId != _shownApp)
            {
                _shownApp = snapshot.CurrentAppId;
                ShowCurrent(app, snapshot.CurrentAppName);
            }
            _profileId = snapshot.CurrentProfileId;
            bool hasProfile = snapshot.CurrentProfileId != Guid.Empty;
            _profileRun.Text = hasProfile ? ProfileName(snapshot.CurrentProfileName) : string.Empty;
            _profileLink.Visibility = hasProfile ? Visibility.Visible : Visibility.Collapsed;
            _currentProfile.Visibility = hasProfile ? Visibility.Collapsed : Visibility.Visible;
        }

        private static string ProfileName(string name) => string.IsNullOrWhiteSpace(name) ? Texts.Unnamed : name;

        public void ShowHdr(HdrState hdr, bool changing)
        {
            if (!hdr.Available)
                _hdr.Show(false, Texts.NotAvailable, false);
            else
                _hdr.Show(hdr.On, hdr.On ? Texts.Enabled : Texts.Disabled, !changing);
        }

        private void ShowCurrent(AppEntry app, string name)
        {
            if (app == null && string.IsNullOrEmpty(name))
            {
                _currentIcon.Visibility = Visibility.Collapsed;
                _currentName.Margin = new Thickness(0);
                _currentName.Text = Texts.None;
                _currentName.FontSize = 14;
                _currentName.SetResourceReference(TextBlock.ForegroundProperty, Res.TextPrimary);
                return;
            }
            _currentIcon.Visibility = Visibility.Visible;
            _currentName.Margin = new Thickness(12, 0, 0, 0);
            _currentName.Text = name ?? app.Name;
            _currentName.FontSize = 16;
            _currentName.SetResourceReference(TextBlock.ForegroundProperty, Res.TextPrimary);
            _currentIcon.ShowInitials(name ?? app?.Name);
            if (app != null)
                _icons.Get(app.Path, image => { if (_shownApp == app.Id) _currentIcon.ShowImage(image); });
        }

        private UIElement AppRow(AppEntry app)
        {
            var icon = new AppIconBox(36, 6);
            icon.ShowInitials(app.Name);
            _icons.Get(app.Path, icon.ShowImage);

            var name = Res.Text(string.IsNullOrWhiteSpace(app.Name) ? app.FileName : app.Name);
            name.Margin = new Thickness(12, 0, 12, 0);

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
                Padding = new Thickness(8, 6, 8, 6),
                Background = Brushes.Transparent,
                Child = grid,
            };
            row.MouseEnter += (s, e) => row.SetResourceReference(Border.BackgroundProperty, Res.SubtleHover);
            row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
            return row;
        }

        private static TextBlock Heading(string text)
        {
            var heading = Res.Text(text, 20, FontWeights.SemiBold);
            heading.Margin = new Thickness(0, 0, 0, 12);
            return heading;
        }

        private static void AddRow(Grid grid, int row, string label, UIElement value)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = Res.Text(label, 14, FontWeights.SemiBold);
            var labelHost = new Border { Child = text, MinHeight = 40, Margin = new Thickness(0, row == 0 ? 0 : 18, 0, 0) };
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

            public StateRow(string name)
            {
                _dot = new Ellipse { Width = 10, Height = 10, StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center };
                _text = Res.Text(Texts.Disabled);
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
