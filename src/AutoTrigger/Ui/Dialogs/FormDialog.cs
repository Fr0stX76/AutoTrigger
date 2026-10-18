using System.Windows;
using System.Windows.Controls;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;
using UiButton = Wpf.Ui.Controls.Button;
using UiControlAppearance = Wpf.Ui.Controls.ControlAppearance;
using UiImageIcon = Wpf.Ui.Controls.ImageIcon;
using UiTitleBar = Wpf.Ui.Controls.TitleBar;
using UiWindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;
using UiWindowCornerPreference = Wpf.Ui.Controls.WindowCornerPreference;

namespace AutoTrigger.Ui
{
    internal abstract class FormDialog : FluentWindow
    {
        private readonly UiButton _primary;
        private bool _saved;

        protected FormDialog(string context, string title, string primaryText, double width = 600)
        {
            Title = title;
            Width = width;
            MinWidth = 0;
            MinHeight = 0;
            ResizeMode = ResizeMode.NoResize;
            ExtendsContentIntoTitleBar = true;
            WindowBackdropType = UiWindowBackdropType.Mica;
            WindowCornerPreference = UiWindowCornerPreference.Round;
            Icon = AppImages.WindowIcon;
            ShowInTaskbar = false;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(new UiTitleBar
            {
                Title = Texts.AppName,
                ShowMaximize = false,
                ShowMinimize = false,
                CanMaximize = false,
                Icon = new UiImageIcon { Source = AppImages.SmallIcon, Width = 16, Height = 16 },
            });

            var content = new StackPanel { Margin = new Thickness(28, 12, 28, 24) };
            if (!string.IsNullOrEmpty(context))
                content.Children.Add(Kit.Caption(context));
            var heading = Kit.Heading(title);
            heading.Margin = new Thickness(0, 2, 0, 0);
            content.Children.Add(heading);
            Body = new StackPanel();
            content.Children.Add(Body);
            Grid.SetRow(content, 1);
            root.Children.Add(content);

            var cancel = new UiButton { Content = Texts.Cancel, MinWidth = 120, IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
            _primary = new UiButton { Content = primaryText, MinWidth = 120, Appearance = UiControlAppearance.Primary, IsDefault = true };
            _primary.Click += (s, e) => Accept();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(cancel);
            buttons.Children.Add(_primary);
            var footer = new Border { Padding = new Thickness(28, 20, 28, 20), BorderThickness = new Thickness(0, 1, 0, 0), Child = buttons };
            footer.SetResourceReference(Border.BackgroundProperty, Res.Layer);
            footer.SetResourceReference(Border.BorderBrushProperty, Res.Divider);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
            Loaded += (s, e) => DialogFit.Fit(this, true);
            Body.SizeChanged += (s, e) =>
            {
                if (IsLoaded)
                    DialogFit.Fit(this, false);
            };
        }

        protected StackPanel Body { get; }

        protected void Accept()
        {
            if (!TrySave())
                return;
            _saved = true;
            Close();
        }

        protected abstract bool TrySave();

        public bool Run(Window owner)
        {
            if (owner != null && owner.IsVisible)
            {
                Owner = owner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                ShowInTaskbar = true;
            }
            DialogFit.Fit(this, false);
            ShowDialog();
            return _saved;
        }

        protected static TextBlock ErrorLine()
        {
            var line = Kit.Wrapped(string.Empty, 12, Res.Critical);
            line.Margin = new Thickness(0, 6, 0, 0);
            line.Visibility = Visibility.Collapsed;
            return line;
        }

        protected static void ShowError(TextBlock line, string message)
        {
            line.Text = message ?? string.Empty;
            line.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
