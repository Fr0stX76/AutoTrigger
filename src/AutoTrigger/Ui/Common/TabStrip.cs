using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoTrigger.Ui
{
    internal sealed class TabStrip : Border
    {
        private readonly List<Tab> _tabs = new List<Tab>();
        private int _selected = -1;

        public TabStrip(IEnumerable<string> names)
        {
            BorderThickness = new Thickness(0, 0, 0, 1);
            SetResourceReference(BorderBrushProperty, Res.Divider);
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 16, 0) };
            int index = 0;
            foreach (string name in names)
            {
                int i = index++;
                var tab = new Tab(name);
                tab.Click += (s, e) => Select(i);
                _tabs.Add(tab);
                strip.Children.Add(tab);
            }
            Child = strip;
        }

        public event Action<int> SelectionChanged;

        public void Select(int index)
        {
            if (index < 0 || index >= _tabs.Count || index == _selected)
                return;
            _selected = index;
            for (int i = 0; i < _tabs.Count; i++)
                _tabs[i].IsCurrent = i == index;
            KeepFocusOnSelected();
            SelectionChanged?.Invoke(index);
        }

        public void KeepFocusOnSelected()
        {
            if (_selected < 0)
                return;
            Tab current = _tabs[_selected];
            if (IsKeyboardFocusWithin && !current.IsKeyboardFocused)
                current.Focus();
        }

        private sealed class Tab : Button
        {
            private static readonly Style FocusStyle = Res.FocusOutline(new CornerRadius(4));

            private readonly TextBlock _label;
            private readonly Border _underline;

            public Tab(string text)
            {
                Height = 44;
                MinWidth = 0;
                Padding = new Thickness(0);
                FocusVisualStyle = FocusStyle;
                _label = Res.Text(text);
                _label.Margin = new Thickness(14, 0, 14, 0);
                _label.TextTrimming = TextTrimming.None;
                _underline = new Border
                {
                    Height = 3,
                    CornerRadius = new CornerRadius(2),
                    Margin = new Thickness(14, 0, 14, 0),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Visibility = Visibility.Hidden,
                };
                _underline.SetResourceReference(Border.BackgroundProperty, Res.AccentFill);
                var grid = new Grid();
                grid.Children.Add(_label);
                grid.Children.Add(_underline);
                Content = grid;
                Template = BuildTemplate();
                System.Windows.Automation.AutomationProperties.SetName(this, text);
            }

            public bool IsCurrent
            {
                set
                {
                    _underline.Visibility = value ? Visibility.Visible : Visibility.Hidden;
                    _label.FontWeight = value ? FontWeights.SemiBold : FontWeights.Normal;
                }
            }

            private static ControlTemplate BuildTemplate()
            {
                var border = new FrameworkElementFactory(typeof(Border), "bd");
                border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4, 4, 0, 0));
                border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
                border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
                var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
                var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
                hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(Res.SubtleHover), "bd"));
                template.Triggers.Add(hover);
                return template;
            }
        }
    }
}
