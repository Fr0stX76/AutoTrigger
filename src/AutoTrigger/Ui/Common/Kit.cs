using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AutoTrigger.Core.Model;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;
using UiButton = Wpf.Ui.Controls.Button;
using UiControlAppearance = Wpf.Ui.Controls.ControlAppearance;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace AutoTrigger.Ui
{
    internal static class Kit
    {
        public const string IconPlus = "M10 4v12M4 10h12";
        public const string IconMinus = "M4 10h12";
        public const string IconMinusSmall = "M5 10h10";
        public const string IconPencil = "M13.5 3.5l3 3L7 16H4v-3z";
        public const string IconUp = "M10 16V4M5 9l5-5 5 5";
        public const string IconDown = "M10 4v12M5 11l5 5 5-5";
        public const string IconMonitor = "M3 4h14v10H3zM7 17h6M10 14v3";
        public const string IconList = "M4 6h12M4 10h12M4 14h12";
        public const string IconRefresh = "M16 10a6 6 0 1 1-1.8-4.3M16 4v4h-4";
        public const string IconCopy = "M7 7h9v9H7zM4 13V4h9";
        public const string IconSort = "M6 4v12M3 13l3 3 3-3M14 16V4M11 7l3-3 3 3";

        public static FrameworkElement Icon(string data, double size = 16, string brush = Res.TextPrimary, bool filled = false)
        {
            Path path = IconPath(data, filled);
            path.SetResourceReference(filled ? Shape.FillProperty : Shape.StrokeProperty, brush);
            return Frame(path, size);
        }

        public static FrameworkElement ControlIcon(string data, double size = 16, bool filled = false)
        {
            Path path = IconPath(data, filled);
            path.SetBinding(filled ? Shape.FillProperty : Shape.StrokeProperty, new Binding("Foreground")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1),
            });
            return Frame(path, size);
        }

        private static Path IconPath(string data, bool filled) => new Path
        {
            Data = Geometry.Parse(data),
            StrokeThickness = filled ? 0 : 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };

        private static FrameworkElement Frame(Path path, double size)
        {
            var canvas = new Canvas { Width = 20, Height = 20 };
            canvas.Children.Add(path);
            return new Viewbox
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Child = canvas,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        public static UiButton Button(string text, string icon = null, bool primary = false)
        {
            var button = new UiButton
            {
                Appearance = primary ? UiControlAppearance.Primary : UiControlAppearance.Secondary,
                MinWidth = 0,
            };
            if (icon == null)
            {
                button.Content = text;
            }
            else
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(ControlIcon(icon));
                content.Children.Add(new TextBlock { Text = text, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
                button.Content = content;
            }
            return button;
        }

        public static UiButton MenuButton(string text, string icon, IEnumerable<KeyValuePair<string, Action>> items)
        {
            UiButton button = Button(text, icon);
            var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (KeyValuePair<string, Action> entry in items)
            {
                Action click = entry.Value;
                var item = new MenuItem { Header = entry.Key };
                item.Click += (s, e) => click();
                menu.Items.Add(item);
            }
            button.ContextMenu = menu;
            button.Click += (s, e) => menu.IsOpen = true;
            return button;
        }

        public static UiButton SortButton(Action<bool> sort) => MenuButton(Texts.Sort, IconSort, new[]
        {
            new KeyValuePair<string, Action>(Texts.SortAscending, () => sort(false)),
            new KeyValuePair<string, Action>(Texts.SortDescending, () => sort(true)),
        });

        public static UiButton IconButton(string icon, string name, double size = 32, bool ghost = false, double iconSize = 16)
        {
            var button = new UiButton
            {
                Width = size,
                Height = size,
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                Content = ControlIcon(icon, iconSize),
                Appearance = ghost ? UiControlAppearance.Transparent : UiControlAppearance.Secondary,
            };
            if (ghost)
                button.BorderThickness = new Thickness(0);
            AutomationProperties.SetName(button, name);
            return button;
        }

        public static TextBlock Heading(string text, double size = 20) => Res.Text(text, size, FontWeights.SemiBold);

        public static TextBlock Caption(string text) => Res.Text(text, 12);

        public static TextBlock Wrapped(string text, double size = 14, string brush = Res.TextPrimary)
        {
            var block = Res.Text(text, size, null, brush);
            block.TextWrapping = TextWrapping.Wrap;
            block.TextTrimming = TextTrimming.None;
            return block;
        }

        public static Border Separator(Thickness margin)
        {
            var line = new Border { Height = 1, Margin = margin };
            line.SetResourceReference(Border.BackgroundProperty, Res.Divider);
            return line;
        }

        public static StackPanel Field(string label, UIElement control, double top = 18)
        {
            var panel = new StackPanel { Margin = new Thickness(0, top, 0, 0) };
            var text = Res.Text(label);
            text.Margin = new Thickness(0, 0, 0, 6);
            panel.Children.Add(text);
            panel.Children.Add(control);
            return panel;
        }

        public static UiTextBox TextField(string text, Action<string> changed = null, string placeholder = null)
        {
            var box = new UiTextBox { Text = text ?? string.Empty, MinWidth = 0, PlaceholderText = placeholder ?? string.Empty };
            if (changed != null)
                box.TextChanged += (s, e) => changed(box.Text);
            return box;
        }

        public static TextBox NumberBox(int value, int min, int max, Action<int> commit, double width = 80)
        {
            int current = Math.Max(min, Math.Min(max, value));
            var box = new TextBox
            {
                Text = current.ToString(CultureInfo.InvariantCulture),
                Width = width,
                MinWidth = 0,
                TextAlignment = TextAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            box.PreviewTextInput += (s, e) => e.Handled = !e.Text.All(char.IsDigit);
            DataObject.AddPastingHandler(box, (s, e) =>
            {
                string pasted = e.DataObject.GetData(DataFormats.UnicodeText) as string;
                if (pasted == null || !pasted.Trim().All(char.IsDigit))
                    e.CancelCommand();
            });

            void Apply()
            {
                int value = int.TryParse(box.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : current;
                value = Math.Max(min, Math.Min(max, value));
                box.Text = value.ToString(CultureInfo.InvariantCulture);
                if (value != current)
                {
                    current = value;
                    commit?.Invoke(value);
                }
            }

            box.LostKeyboardFocus += (s, e) => Apply();
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Apply();
                    box.SelectAll();
                    e.Handled = true;
                }
            };
            return box;
        }

        public static TextBox SecondsBox(int ms, Action<int> commit, double width = 64)
        {
            int current = ms;
            var box = new TextBox
            {
                Text = DelayText.Format(current),
                Width = width,
                MinWidth = 0,
                TextAlignment = TextAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            box.PreviewTextInput += (s, e) => e.Handled = !e.Text.All(DelayText.IsAllowed);
            DataObject.AddPastingHandler(box, (s, e) =>
            {
                string pasted = e.DataObject.GetData(DataFormats.UnicodeText) as string;
                if (!DelayText.TryParse(pasted, out _))
                    e.CancelCommand();
            });

            void Apply()
            {
                int value = DelayText.TryParse(box.Text, out int parsed) ? parsed : current;
                box.Text = DelayText.Format(value);
                if (value != current)
                {
                    current = value;
                    commit?.Invoke(value);
                }
            }

            box.LostKeyboardFocus += (s, e) => Apply();
            box.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Apply();
                    box.SelectAll();
                    e.Handled = true;
                }
            };
            return box;
        }

        public static ToggleSwitch Switch(bool on, Action<bool> changed, string name)
        {
            var toggle = new ToggleSwitch { IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(toggle, name);
            toggle.Click += (s, e) => changed(toggle.IsChecked == true);
            return toggle;
        }

        public static ComboBox Combo<T>(IEnumerable<KeyValuePair<T, string>> items, T selected, Action<T> changed, double minWidth = 220)
        {
            var combo = new ComboBox { MinWidth = minWidth };
            T current = selected;
            foreach (KeyValuePair<T, string> item in items)
            {
                var entry = new ComboBoxItem { Content = item.Value, Tag = item.Key };
                combo.Items.Add(entry);
                if (EqualityComparer<T>.Default.Equals(item.Key, selected))
                    combo.SelectedItem = entry;
            }
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedItem is ComboBoxItem chosen && chosen.Tag is T value && !EqualityComparer<T>.Default.Equals(value, current))
                {
                    current = value;
                    changed?.Invoke(value);
                }
            };
            return combo;
        }

        public static ComboBox WithScrollBar(ComboBox combo)
        {
            combo.MaxDropDownHeight = 360;
            ScrollViewer.SetVerticalScrollBarVisibility(combo, ScrollBarVisibility.Visible);
            combo.DropDownOpened += (s, e) => combo.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (combo.Template?.FindName("PART_Popup", combo) is System.Windows.Controls.Primitives.Popup popup && popup.Child != null)
                {
                    foreach (ScrollViewer viewer in Descendants<ScrollViewer>(popup.Child))
                        viewer.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
                }
            }));
            return combo;
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;
                foreach (T deeper in Descendants<T>(child))
                    yield return deeper;
            }
        }

        public static ComboBox LazyCombo<T>(IReadOnlyList<KeyValuePair<T, string>> items, T selected, Action<T> changed, double minWidth = 220)
        {
            var combo = new ComboBox { MinWidth = minWidth };
            T current = selected;
            bool filled = false;
            bool filling = false;
            foreach (KeyValuePair<T, string> item in items)
            {
                if (!EqualityComparer<T>.Default.Equals(item.Key, selected))
                    continue;
                var entry = new ComboBoxItem { Content = item.Value, Tag = item.Key };
                combo.Items.Add(entry);
                combo.SelectedItem = entry;
                break;
            }

            void Fill()
            {
                if (filled)
                    return;
                filled = true;
                filling = true;
                try
                {
                    combo.Items.Clear();
                    foreach (KeyValuePair<T, string> item in items)
                    {
                        var entry = new ComboBoxItem { Content = item.Value, Tag = item.Key };
                        combo.Items.Add(entry);
                        if (EqualityComparer<T>.Default.Equals(item.Key, current))
                            combo.SelectedItem = entry;
                    }
                }
                finally
                {
                    filling = false;
                }
            }

            combo.GotKeyboardFocus += (s, e) => Fill();
            combo.DropDownOpened += (s, e) => Fill();
            combo.SelectionChanged += (s, e) =>
            {
                if (filling)
                    return;
                if (combo.SelectedItem is ComboBoxItem chosen && chosen.Tag is T value && !EqualityComparer<T>.Default.Equals(value, current))
                {
                    current = value;
                    changed?.Invoke(value);
                }
            };
            return combo;
        }

        public static bool HasAncestor(DependencyObject source, DependencyObject stop, Func<DependencyObject, bool> match)
        {
            for (DependencyObject element = source; element != null && element != stop; element = ParentOf(element))
            {
                if (match(element))
                    return true;
            }
            return false;
        }

        private static DependencyObject ParentOf(DependencyObject element)
        {
            bool visual = element is Visual || element is System.Windows.Media.Media3D.Visual3D;
            return visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }

        public static List<KeyValuePair<Guid, string>> ProfileChoices(AppSettings settings, bool withNone = true)
        {
            var list = new List<KeyValuePair<Guid, string>>();
            if (withNone)
                list.Add(new KeyValuePair<Guid, string>(Guid.Empty, Texts.None));
            list.AddRange(settings.Profiles
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => new KeyValuePair<Guid, string>(p.Id, string.IsNullOrWhiteSpace(p.Name) ? Texts.Unnamed : p.Name)));
            return list;
        }

        public static Grid PairGrid(double labelWidth = 170, double gap = 24)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(gap) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        public static void AddPair(Grid grid, UIElement label, UIElement value, double top)
        {
            int row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (label is FrameworkElement l)
            {
                l.Margin = new Thickness(0, row == 0 ? 0 : top, 0, 0);
                l.VerticalAlignment = VerticalAlignment.Center;
            }
            if (value is FrameworkElement v)
            {
                v.Margin = new Thickness(0, row == 0 ? 0 : top, 0, 0);
                if (v.VerticalAlignment == VerticalAlignment.Stretch)
                    v.VerticalAlignment = VerticalAlignment.Center;
            }
            Grid.SetRow(label, row);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 2);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }
    }
}
