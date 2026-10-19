using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;
using UiButton = Wpf.Ui.Controls.Button;
using UiControlAppearance = Wpf.Ui.Controls.ControlAppearance;
using UiImageIcon = Wpf.Ui.Controls.ImageIcon;
using UiTitleBar = Wpf.Ui.Controls.TitleBar;
using UiWindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;
using UiWindowCornerPreference = Wpf.Ui.Controls.WindowCornerPreference;

namespace AutoTrigger.Ui
{
    internal sealed class MainWindow : FluentWindow
    {
        public const double DefaultWidth = 1040;
        public const double DefaultHeight = 720;

        public const int StatusTab = 0;
        public const int ProfilesTab = 2;
        public const int DisplaysTab = 3;

        private readonly TabStrip _tabs;
        private readonly Border _host = new Border();
        private readonly FrameworkElement[] _pages;

        public MainWindow(StatusPage status, FrameworkElement[] otherPages, string version, double width, double height)
        {
            Title = Texts.AppName;
            Icon = AppImages.WindowIcon;
            Width = width >= 400 ? width : DefaultWidth;
            Height = height >= 300 ? height : DefaultHeight;
            MinWidth = 920;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ExtendsContentIntoTitleBar = true;
            WindowBackdropType = UiWindowBackdropType.Mica;
            WindowCornerPreference = UiWindowCornerPreference.Round;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleBar = new UiTitleBar
            {
                Title = Texts.AppName,
                Icon = new UiImageIcon { Source = AppImages.SmallIcon, Width = 16, Height = 16 },
            };
            root.Children.Add(titleBar);

            _tabs = new TabStrip(new[] { Texts.TabStatus, Texts.TabApplications, Texts.TabProfiles, Texts.TabDisplays, Texts.TabSettings });
            Grid.SetRow(_tabs, 1);
            root.Children.Add(_tabs);

            Status = status;
            _pages = new FrameworkElement[1 + otherPages.Length];
            _pages[0] = status;
            otherPages.CopyTo(_pages, 1);
            Grid.SetRow(_host, 2);
            root.Children.Add(_host);
            _tabs.SelectionChanged += index =>
            {
                CurrentTab = index;
                _host.Child = _pages[index];
                (_pages[index] as IPage)?.OnShown();
                TabChanged?.Invoke();
            };
            _tabs.Select(StatusTab);

            UIElement footer = Footer(version);
            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            Content = root;
        }

        public event Action AboutClicked;

        public event Action TabChanged;

        public int CurrentTab { get; private set; }

        public StatusPage Status { get; }

        public void ShowTab(int index) => _tabs.Select(index);

        public void Bring()
        {
            bool back = !IsVisible || WindowState == WindowState.Minimized;
            if (back)
                ShowTab(StatusTab);
            if (!IsVisible)
                Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
            if (back)
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(_tabs.KeepFocusOnSelected));
        }

        private Border Footer(string version)
        {
            var about = Ghost(Texts.About);
            about.Click += (s, e) => AboutClicked?.Invoke();
            var versionText = Res.Text(version, 12);
            versionText.Margin = new Thickness(12, 0, 0, 0);

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(about);
            row.Children.Add(versionText);

            var border = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(24, 8, 24, 8), Child = row };
            border.SetResourceReference(Border.BorderBrushProperty, Res.Divider);
            return border;
        }

        private static UiButton Ghost(string text) =>
            new UiButton { Content = text, Appearance = UiControlAppearance.Transparent, BorderThickness = new Thickness(0) };
    }
}
