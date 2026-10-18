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
    internal enum DialogChoice
    {
        None,
        Primary,
        Secondary,
    }

    internal sealed class MessageDialog : FluentWindow
    {
        private DialogChoice _choice;

        private MessageDialog(string title, string text, string primary, string secondary)
        {
            Title = title;
            Width = 480;
            MinWidth = 0;
            MinHeight = 0;
            ResizeMode = ResizeMode.NoResize;
            ExtendsContentIntoTitleBar = true;
            WindowBackdropType = UiWindowBackdropType.Mica;
            WindowCornerPreference = UiWindowCornerPreference.Round;
            Icon = AppImages.WindowIcon;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var titleBar = new UiTitleBar
            {
                Title = title,
                ShowMaximize = false,
                ShowMinimize = false,
                CanMaximize = false,
                Icon = new UiImageIcon { Source = AppImages.SmallIcon, Width = 16, Height = 16 },
            };
            root.Children.Add(titleBar);

            var body = Res.Text(text, 14);
            body.TextWrapping = TextWrapping.Wrap;
            body.TextTrimming = TextTrimming.None;
            body.Margin = new Thickness(24, 12, 24, 24);
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (!string.IsNullOrEmpty(secondary))
            {
                var second = new UiButton { Content = secondary, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
                second.Click += (s, e) => Finish(DialogChoice.Secondary);
                buttons.Children.Add(second);
            }
            var first = new UiButton { Content = primary, MinWidth = 96, Appearance = UiControlAppearance.Primary, IsDefault = true };
            first.Click += (s, e) => Finish(DialogChoice.Primary);
            buttons.Children.Add(first);

            var footer = new Border
            {
                Padding = new Thickness(24, 16, 24, 16),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = buttons,
            };
            footer.SetResourceReference(Border.BackgroundProperty, Res.Layer);
            footer.SetResourceReference(Border.BorderBrushProperty, Res.Divider);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
            Loaded += (s, e) => DialogFit.Fit(this, true);
        }

        private void Finish(DialogChoice choice)
        {
            _choice = choice;
            Close();
        }

        public static DialogChoice Show(Window owner, string title, string text, string primary, string secondary = null)
        {
            var dialog = new MessageDialog(title, text, primary, secondary);
            if (owner != null && owner.IsVisible && owner.WindowState != WindowState.Minimized)
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                dialog.ShowInTaskbar = false;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dialog.ShowInTaskbar = true;
            }
            DialogFit.Fit(dialog, false);
            dialog.ShowDialog();
            return dialog._choice;
        }
    }
}
