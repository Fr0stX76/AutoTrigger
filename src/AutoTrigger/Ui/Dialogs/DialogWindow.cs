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
    internal abstract class DialogWindow : FluentWindow
    {
        protected const double Spacing = 24;
        private const double ButtonWidth = 120;
        private const double ButtonGap = 8;

        private readonly Grid _root = new Grid();

        protected DialogWindow(string title, string titleBarText, double width)
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

            for (int i = 0; i < 3; i++)
                _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _root.Children.Add(new UiTitleBar
            {
                Title = titleBarText,
                ShowMaximize = false,
                ShowMinimize = false,
                CanMaximize = false,
                Icon = new UiImageIcon { Source = AppImages.SmallIcon, Width = 16, Height = 16 },
            });
            Content = _root;
            SourceInitialized += (s, e) => WindowPlacement.PlaceOver(this);
            Loaded += (s, e) => DialogFit.Fit(this, true);
        }

        protected void SetBody(FrameworkElement body)
        {
            body.Margin = new Thickness(Spacing, 12, Spacing, Spacing);
            Grid.SetRow(body, 1);
            _root.Children.Add(body);
        }

        protected void SetFooter(UIElement left, params UiButton[] right)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            for (int i = 0; i < right.Length; i++)
            {
                right[i].Margin = new Thickness(i == 0 ? 0 : ButtonGap, 0, 0, 0);
                buttons.Children.Add(right[i]);
            }
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (left != null)
                row.Children.Add(left);
            Grid.SetColumn(buttons, 1);
            row.Children.Add(buttons);

            var footer = new Border { Padding = new Thickness(Spacing, 20, Spacing, 20), BorderThickness = new Thickness(0, 1, 0, 0), Child = row };
            footer.SetResourceReference(Border.BackgroundProperty, Res.Layer);
            footer.SetResourceReference(Border.BorderBrushProperty, Res.Divider);
            Grid.SetRow(footer, 2);
            _root.Children.Add(footer);
        }

        protected static UiButton FooterButton(string text, bool primary = false)
        {
            var button = new UiButton { Content = text, MinWidth = ButtonWidth };
            if (primary)
            {
                button.Appearance = UiControlAppearance.Primary;
                button.IsDefault = true;
            }
            return button;
        }

        protected void ShowOver(Window owner)
        {
            if (owner != null && owner.IsVisible && owner.WindowState != WindowState.Minimized)
            {
                Owner = owner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                ShowInTaskbar = false;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                ShowInTaskbar = true;
            }
            DialogFit.Fit(this, false);
            ShowDialog();
        }
    }
}
