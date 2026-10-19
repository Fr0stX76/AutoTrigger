using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AutoTrigger.Core.Displays;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Ui
{
    internal sealed class DisplaysPage : Grid, IPage
    {
        private readonly SettingsController _settings;
        private readonly DisplayService _displays;
        private readonly Func<Window> _owner;
        private readonly WrapPanel _cards = new WrapPanel { Orientation = Orientation.Horizontal };
        private bool _shown;

        public DisplaysPage(SettingsController settings, DisplayService displays, Func<Window> owner)
        {
            _settings = settings;
            _displays = displays;
            _owner = owner;
            Grid.SetIsSharedSizeScope(_cards, true);
            this.Bind(BackgroundProperty, Res.Layer);
            var root = new Grid { Margin = new Thickness(32, 24, 32, 24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(Kit.Heading(Texts.TabDisplays));
            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _cards,
                Margin = new Thickness(0, 12, 0, 0),
            };
            Grid.SetRow(scroller, 1);
            root.Children.Add(scroller);
            Children.Add(root);

            _displays.MonitorsChanged += () =>
            {
                if (_shown && IsVisible)
                    Rebuild();
            };
            IsVisibleChanged += (s, e) => _shown = IsVisible;
        }

        public void OnShown()
        {
            _shown = true;
            Rebuild();
            _displays.Refresh();
        }

        private void Rebuild()
        {
            _cards.Children.Clear();
            List<DisplayEntry> entries = _settings.Current.Displays.ToList();
            if (entries.Count == 0)
            {
                _cards.Children.Add(Kit.Caption(Texts.NoDisplays));
                return;
            }
            foreach (DisplayEntry entry in entries)
            {
                MonitorInfo monitor = _displays.Monitors.FirstOrDefault(m =>
                    entry.IsLinked && string.Equals(m.DevicePath, entry.DevicePath, StringComparison.OrdinalIgnoreCase));
                _cards.Children.Add(Card(entry, monitor));
            }
        }

        private static string HdrText(MonitorInfo monitor)
        {
            if (monitor.HdrOn)
                return Texts.On;
            return monitor.HdrSupported ? Texts.OffSupported : Texts.NotSupported;
        }

        private Border Card(DisplayEntry entry, MonitorInfo monitor)
        {
            string name = CardName(entry, monitor);
            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, SharedSizeGroup = "CardHead" });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, SharedSizeGroup = "CardBody" });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, SharedSizeGroup = "CardFoot" });

            Border header = CardHeader(entry, name);
            content.Children.Add(header);

            Grid details = CardDetails(entry, monitor);
            Grid.SetRow(details, 1);
            content.Children.Add(details);

            Border footer = CardFooter(entry, monitor, name);
            Grid.SetRow(footer, 2);
            content.Children.Add(footer);

            if (monitor == null)
            {
                header.Opacity = 0.6;
                details.Opacity = 0.6;
            }

            Border card = Res.Card(content, new Thickness(0));
            card.Width = 560;
            card.Margin = new Thickness(0, 0, 16, 16);
            return card;
        }

        private static string CardName(DisplayEntry entry, MonitorInfo monitor)
        {
            if (!string.IsNullOrEmpty(monitor?.FriendlyName))
                return monitor.FriendlyName;
            if (!string.IsNullOrEmpty(entry.FriendlyName))
                return entry.FriendlyName;
            return entry.LegacyUid >= 0 ? Texts.Format(Texts.ImportedDisplay, entry.LegacyUid) : Texts.Display;
        }

        private Border CardHeader(DisplayEntry entry, string name)
        {
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(Kit.Icon(Kit.IconMonitor, 20, Res.TextPrimary));

            TextBlock title = Res.Text(name, 16, FontWeights.SemiBold);
            title.Margin = new Thickness(14, 0, 14, 0);
            Grid.SetColumn(title, 1);
            header.Children.Add(title);

            TextBlock managedLabel = Res.Text(Texts.Managed);
            managedLabel.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(managedLabel, 2);
            header.Children.Add(managedLabel);

            var managed = Kit.Switch(entry.Managed, on =>
            {
                entry.Managed = on;
                _settings.Commit();
                _displays.UpdateHdr();
            }, Texts.Managed);
            Grid.SetColumn(managed, 3);
            header.Children.Add(managed);

            var border = new Border { Padding = new Thickness(20, 16, 20, 16), BorderThickness = new Thickness(0, 0, 0, 1), Child = header };
            border.SetResourceReference(Border.BorderBrushProperty, Res.CardStroke);
            return border;
        }

        private static Grid CardDetails(DisplayEntry entry, MonitorInfo monitor)
        {
            Grid details = Kit.PairGrid(150, 20);
            details.Margin = new Thickness(20, 14, 20, 14);
            details.VerticalAlignment = VerticalAlignment.Top;
            if (monitor == null)
            {
                Kit.AddPair(details, Res.Text(Texts.State), Res.Text(entry.IsLinked ? Texts.NotConnected : Texts.NotLinkedYet), 10);
            }
            else
            {
                int bpc = monitor.Color?.Bpc ?? monitor.BitsPerColor;
                Kit.AddPair(details, Res.Text(Texts.WindowsName), Value(monitor.GdiName), 10);
                Kit.AddPair(details, Res.Text(Texts.GraphicsCard), Value(monitor.AdapterName), 10);
                Kit.AddPair(details, Res.Text(Texts.Resolution), Value(ResolutionText(monitor)), 10);
                Kit.AddPair(details, Res.Text(Texts.RefreshRate), Value(ActionText.Hz(monitor.RefreshNumerator, monitor.RefreshDenominator)), 10);
                Kit.AddPair(details, Res.Text(Texts.ColorDepth), Value(bpc > 0 ? bpc.ToString(CultureInfo.InvariantCulture) + " bpc" : null), 10);
                Kit.AddPair(details, Res.Text(Texts.FormatAndRange), Value(FormatText(monitor.Color)), 10);
                Kit.AddPair(details, Res.Text(Texts.Hdr), Dot(monitor.HdrOn, HdrText(monitor)), 10);
                Kit.AddPair(details, Res.Text(Texts.Primary), Dot(monitor.IsPrimary, monitor.IsPrimary ? Texts.Yes : Texts.No), 10);
            }

            TextBlock path = Res.Text(string.IsNullOrEmpty(entry.DevicePath) ? "—" : entry.DevicePath, 12);
            path.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            path.TextWrapping = TextWrapping.Wrap;
            path.TextTrimming = TextTrimming.None;
            Kit.AddPair(details, Res.Text(Texts.MonitorPath), path, 10);
            return details;
        }

        private static string ResolutionText(MonitorInfo monitor)
        {
            if (monitor.Width <= 0)
                return null;
            return monitor.Width.ToString(CultureInfo.InvariantCulture) + " × " + monitor.Height.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatText(ColorSetting color) =>
            color == null ? null : ColorSetting.FormatName(color.Format) + " · " + ColorSetting.RangeName(color.Range);

        private Border CardFooter(DisplayEntry entry, MonitorInfo monitor, string name)
        {
            var buttons = new DockPanel { LastChildFill = false };
            var refresh = Kit.Button(Texts.Refresh, Kit.IconRefresh);
            refresh.Click += (s, e) => _displays.Refresh();
            DockPanel.SetDock(refresh, Dock.Left);
            buttons.Children.Add(refresh);
            if (monitor == null)
            {
                var delete = Kit.Button(Texts.Delete, Kit.IconMinus);
                delete.Appearance = Wpf.Ui.Controls.ControlAppearance.Danger;
                delete.Foreground = Brushes.White;
                delete.Margin = new Thickness(8, 0, 0, 0);
                delete.Click += (s, e) => RemoveDisplay(entry, name);
                DockPanel.SetDock(delete, Dock.Left);
                buttons.Children.Add(delete);
            }

            var footer = new Border
            {
                Padding = new Thickness(20, 12, 20, 12),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = buttons,
            };
            footer.SetResourceReference(Border.BorderBrushProperty, Res.CardStroke);
            return footer;
        }

        private void RemoveDisplay(DisplayEntry entry, string name)
        {
            if (!_settings.Current.Displays.Remove(entry))
                return;
            Log.Info($"Display removed: {name}");
            _settings.Commit();
            _displays.UpdateHdr();
            Rebuild();
        }

        private static TextBlock Value(string text) =>
            string.IsNullOrEmpty(text) ? Res.Text("—") : Res.Text(text);

        private static StackPanel Dot(bool on, string text)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new Ellipse
            {
                Width = 10,
                Height = 10,
                StrokeThickness = 1.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            if (on)
                dot.SetResourceReference(Shape.FillProperty, Res.Success);
            else
                dot.SetResourceReference(Shape.StrokeProperty, Res.TextPrimary);
            panel.Children.Add(dot);
            panel.Children.Add(Res.Text(text));
            return panel;
        }
    }
}
