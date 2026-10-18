using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AutoTrigger.Ui
{
    internal sealed class ListRow : Border
    {
        private static readonly Style FocusStyle = Res.FocusOutline(new CornerRadius(4));

        private readonly Border _pill;
        private bool _selected;

        public ListRow(UIElement content, Thickness padding)
        {
            CornerRadius = new CornerRadius(4);
            Padding = padding;
            Background = Brushes.Transparent;
            Focusable = true;
            FocusVisualStyle = FocusStyle;
            _pill = new Border
            {
                Width = 3,
                Height = 16,
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(-padding.Left, 0, 0, 0),
                Visibility = Visibility.Hidden,
            };
            _pill.SetResourceReference(Border.BackgroundProperty, Res.AccentFill);
            var grid = new Grid();
            grid.Children.Add(_pill);
            grid.Children.Add(content);
            Child = grid;

            MouseEnter += (s, e) => UpdateBackground();
            MouseLeave += (s, e) => UpdateBackground();
            MouseLeftButtonDown += (s, e) =>
            {
                Focus();
                Clicked?.Invoke();
            };
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    Clicked?.Invoke();
                    e.Handled = true;
                }
            };
        }

        public event Action Clicked;

        public bool IsSelected
        {
            get => _selected;
            set
            {
                _selected = value;
                _pill.Visibility = value ? Visibility.Visible : Visibility.Hidden;
                UpdateBackground();
            }
        }

        private void UpdateBackground()
        {
            if (_selected)
                SetResourceReference(BackgroundProperty, IsMouseOver ? Res.SubtlePressed : Res.SubtleHover);
            else if (IsMouseOver)
                SetResourceReference(BackgroundProperty, Res.SubtleHover);
            else
                Background = Brushes.Transparent;
        }
    }
}
