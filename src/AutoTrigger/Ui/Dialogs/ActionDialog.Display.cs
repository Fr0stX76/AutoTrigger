using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AutoTrigger.Core.Displays;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Model;
using UiButton = Wpf.Ui.Controls.Button;
using UiControlAppearance = Wpf.Ui.Controls.ControlAppearance;

namespace AutoTrigger.Ui
{
    internal sealed partial class ActionDialog
    {
        private readonly DisplayService _displays;

        private StackPanel _displayPanel;
        private Guid _targetId;
        private HdrChange _hdr;
        private readonly List<UiButton> _hdrButtons = new List<UiButton>();
        private PixelSize _size;
        private RefreshRate _rate;
        private bool _rateMax;
        private int _bpc;
        private string _colorFormat = string.Empty;
        private string _colorRange = string.Empty;
        private readonly Border _sizeHost = new Border();
        private readonly Border _rateHost = new Border();
        private readonly Border _depthHost = new Border();
        private readonly Border _formatHost = new Border();
        private readonly Border _rangeHost = new Border();
        private StackPanel _colorSection;
        private DisplayCapabilities _caps;

        private void BuildDisplay(DisplayAction display)
        {
            _displayPanel = new StackPanel();
            _hdr = display?.Hdr ?? HdrChange.Unchanged;
            _size = display != null && display.Width > 0 && display.Height > 0 ? new PixelSize(display.Width, display.Height) : default;
            _rate = display != null && display.RefreshNumerator > 0 ? new RefreshRate(display.RefreshNumerator, display.RefreshDenominator) : default;
            _rateMax = display?.RefreshMax ?? false;
            _bpc = display?.ColorDepthBpc ?? 0;
            _colorFormat = display?.ColorFormat ?? string.Empty;
            _colorRange = display?.DynamicRange ?? string.Empty;
            _targetId = display != null ? display.DisplayId : DefaultTarget();

            ComboBox target = Kit.Combo(TargetChoices(), _targetId, id =>
            {
                _targetId = id;
                RefreshDisplayLists();
            });
            target.HorizontalAlignment = HorizontalAlignment.Stretch;
            _displayPanel.Children.Add(Kit.Field(Texts.Display, target));
            _displayPanel.Children.Add(Kit.Separator(new Thickness(0, 18, 0, 0)));

            var hdrRow = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (KeyValuePair<HdrChange, string> choice in new[]
            {
                new KeyValuePair<HdrChange, string>(HdrChange.Unchanged, Texts.Unchanged),
                new KeyValuePair<HdrChange, string>(HdrChange.On, Texts.On),
                new KeyValuePair<HdrChange, string>(HdrChange.Off, Texts.Off),
            })
            {
                HdrChange value = choice.Key;
                var button = new UiButton { Content = choice.Value, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), Tag = value };
                button.Click += (s, e) =>
                {
                    _hdr = value;
                    UpdateHdrButtons();
                };
                _hdrButtons.Add(button);
                hdrRow.Children.Add(button);
            }
            UpdateHdrButtons();
            _displayPanel.Children.Add(Kit.Field(Texts.Hdr, hdrRow));

            _displayPanel.Children.Add(TwoColumns(Kit.Field(Texts.DesktopResolution, _sizeHost), Kit.Field(Texts.RefreshRate, _rateHost)));
            _displayPanel.Children.Add(Kit.Separator(new Thickness(0, 20, 0, 0)));

            _colorSection = new StackPanel();
            TextBlock heading = Kit.Heading(Texts.NvidiaColor, 16);
            heading.Margin = new Thickness(0, 16, 0, 0);
            _colorSection.Children.Add(heading);
            var colors = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            for (int i = 0; i < 5; i++)
                colors.ColumnDefinitions.Add(new ColumnDefinition { Width = i % 2 == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(12) });
            AddColumn(colors, 0, Kit.Field(Texts.ColorDepth, _depthHost, 12));
            AddColumn(colors, 2, Kit.Field(Texts.OutputFormat, _formatHost, 12));
            AddColumn(colors, 4, Kit.Field(Texts.DynamicRange, _rangeHost, 12));
            _colorSection.Children.Add(colors);
            _displayPanel.Children.Add(_colorSection);

            RefreshDisplayLists();
        }

        private static Grid TwoColumns(UIElement left, UIElement right)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddColumn(grid, 0, left);
            AddColumn(grid, 2, right);
            return grid;
        }

        private static void AddColumn(Grid grid, int column, UIElement element)
        {
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }

        private void UpdateHdrButtons()
        {
            foreach (UiButton button in _hdrButtons)
                button.Appearance = (HdrChange)button.Tag == _hdr ? UiControlAppearance.Primary : UiControlAppearance.Secondary;
        }

        private Guid DefaultTarget()
        {
            foreach (DisplayEntry entry in _settings.Displays)
            {
                MonitorInfo monitor = _displays?.MonitorOf(entry);
                if (monitor != null && monitor.IsPrimary)
                    return entry.Id;
            }
            return _settings.Displays.FirstOrDefault()?.Id ?? Guid.Empty;
        }

        private List<KeyValuePair<Guid, string>> TargetChoices()
        {
            var choices = new List<KeyValuePair<Guid, string>> { new KeyValuePair<Guid, string>(Guid.Empty, Texts.AllManagedDisplays) };
            foreach (DisplayEntry entry in _settings.Displays)
            {
                MonitorInfo monitor = _displays?.MonitorOf(entry);
                string name = !string.IsNullOrEmpty(monitor?.FriendlyName) ? monitor.FriendlyName
                    : !string.IsNullOrEmpty(entry.FriendlyName) ? entry.FriendlyName
                    : entry.LegacyUid >= 0 ? Texts.Format(Texts.ImportedDisplay, entry.LegacyUid) : Texts.Display;
                if (monitor == null)
                    name += " · " + Texts.NotConnectedShort;
                else if (monitor.IsPrimary)
                    name += " · " + Texts.PrimaryShort;
                choices.Add(new KeyValuePair<Guid, string>(entry.Id, name));
            }
            if (_targetId != Guid.Empty && choices.All(c => c.Key != _targetId))
                choices.Add(new KeyValuePair<Guid, string>(_targetId, Texts.MissingDisplay));
            return choices;
        }

        private DisplayCapabilities TargetCapabilities()
        {
            if (_displays == null)
                return new DisplayCapabilities();
            if (_targetId == Guid.Empty)
            {
                List<DisplayCapabilities> all = _settings.Displays.Where(d => d.Managed)
                    .Select(d => _displays.CapabilitiesOf(d)).Where(c => c != null).ToList();
                return DisplayCapabilities.Common(all);
            }
            DisplayEntry entry = _settings.Displays.FirstOrDefault(d => d.Id == _targetId);
            return _displays.CapabilitiesOf(entry) ?? new DisplayCapabilities();
        }

        private ColorSetting TargetColor()
        {
            DisplayEntry entry = _settings.Displays.FirstOrDefault(d => d.Id == _targetId);
            return _displays?.MonitorOf(entry)?.Color;
        }

        private void RefreshDisplayLists()
        {
            _caps = TargetCapabilities();

            var sizes = new List<KeyValuePair<PixelSize, string>> { new KeyValuePair<PixelSize, string>(default, Texts.Unchanged) };
            sizes.AddRange(_caps.DesktopSizes.Select(s => new KeyValuePair<PixelSize, string>(s, s.ToString())));
            if (_size.IsSet && !_caps.DesktopSizes.Contains(_size))
                sizes.Add(new KeyValuePair<PixelSize, string>(_size, NotOffered(_size.ToString())));
            _sizeHost.Child = Stretch(Kit.Combo(sizes, _size, v => _size = v, 0));

            var rates = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(string.Empty, Texts.Unchanged),
                new KeyValuePair<string, string>(MaxKey, Texts.Maximum),
            };
            string selectedRate = _rateMax ? MaxKey : string.Empty;
            RefreshRate matched = default;
            foreach (RefreshRate r in _caps.RefreshRates)
            {
                rates.Add(new KeyValuePair<string, string>(RateKey(r), r.ToString()));
                if (!_rateMax && _rate.IsSet && !matched.IsSet && r.SameAs(_rate))
                {
                    selectedRate = RateKey(r);
                    matched = r;
                }
            }
            if (matched.IsSet)
                _rate = matched;
            if (!_rateMax && _rate.IsSet && !_caps.RefreshRates.Any(r => r.SameAs(_rate)))
            {
                rates.Add(new KeyValuePair<string, string>(RateKey(_rate), NotOffered(_rate.ToString())));
                selectedRate = RateKey(_rate);
            }
            _rateHost.Child = Stretch(Kit.Combo(rates, selectedRate, ChooseRate, 0));

            bool anyColor = _bpc > 0 || _colorFormat.Length > 0 || _colorRange.Length > 0;
            _colorSection.Visibility = _caps.Nvidia || anyColor ? Visibility.Visible : Visibility.Collapsed;
            RefreshColorLists();
        }

        private void RefreshColorLists()
        {
            List<ColorSetting> offered = _caps.Colors;
            string format = _colorFormat.Length > 0 ? _colorFormat : TargetColor()?.Format ?? string.Empty;
            List<ColorSetting> forFormat = format.Length > 0 ? offered.Where(c => c.Format == format).ToList() : offered;
            List<ColorSetting> forDepth = _bpc > 0 ? forFormat.Where(c => c.Bpc == _bpc).ToList() : forFormat;

            var formats = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(string.Empty, Texts.Unchanged) };
            foreach (string f in ColorSetting.Formats.Where(f => offered.Any(c => c.Format == f)))
                formats.Add(new KeyValuePair<string, string>(f, ColorSetting.FormatName(f)));
            if (_colorFormat.Length > 0 && formats.All(f => f.Key != _colorFormat))
                formats.Add(new KeyValuePair<string, string>(_colorFormat, NotOffered(ColorSetting.FormatName(_colorFormat))));
            _formatHost.Child = Stretch(Kit.Combo(formats, _colorFormat, v =>
            {
                _colorFormat = v;
                RefreshColorLists();
            }, 0));

            var depths = new List<KeyValuePair<int, string>> { new KeyValuePair<int, string>(0, Texts.Unchanged) };
            foreach (int d in forFormat.Select(c => c.Bpc).Distinct().OrderBy(d => d))
                depths.Add(new KeyValuePair<int, string>(d, d + " bpc"));
            if (_bpc > 0 && depths.All(d => d.Key != _bpc))
                depths.Add(new KeyValuePair<int, string>(_bpc, NotOffered(_bpc + " bpc")));
            _depthHost.Child = Stretch(Kit.Combo(depths, _bpc, v =>
            {
                _bpc = v;
                RefreshColorLists();
            }, 0));

            var ranges = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(string.Empty, Texts.Unchanged) };
            foreach (string r in ColorSetting.Ranges.Where(r => forDepth.Any(c => c.Range == r)))
                ranges.Add(new KeyValuePair<string, string>(r, ColorSetting.RangeName(r)));
            if (_colorRange.Length > 0 && ranges.All(r => r.Key != _colorRange))
                ranges.Add(new KeyValuePair<string, string>(_colorRange, NotOffered(ColorSetting.RangeName(_colorRange))));
            _rangeHost.Child = Stretch(Kit.Combo(ranges, _colorRange, v => _colorRange = v, 0));
        }

        private const string MaxKey = "max";

        private static string RateKey(RefreshRate r) => r.Numerator + "/" + r.Denominator;

        private void ChooseRate(string key)
        {
            _rateMax = key == MaxKey;
            _rate = default;
            if (_rateMax || key.Length == 0)
                return;
            string[] parts = key.Split('/');
            if (parts.Length == 2 && uint.TryParse(parts[0], out uint n) && uint.TryParse(parts[1], out uint d))
                _rate = new RefreshRate(n, d);
        }

        private static ComboBox Stretch(ComboBox combo)
        {
            combo.HorizontalAlignment = HorizontalAlignment.Stretch;
            return combo;
        }

        private static string NotOffered(string text) => text + " (" + Texts.NotOffered + ")";

        private bool TrySaveDisplay(int delay)
        {
            var action = new DisplayAction
            {
                DisplayId = _targetId,
                Hdr = _hdr,
                Width = _size.IsSet ? _size.Width : 0,
                Height = _size.IsSet ? _size.Height : 0,
                RefreshNumerator = !_rateMax && _rate.IsSet ? _rate.Numerator : 0,
                RefreshDenominator = !_rateMax && _rate.IsSet ? _rate.Denominator : 1,
                RefreshMax = _rateMax,
                ColorDepthBpc = _bpc,
                ColorFormat = _colorFormat,
                DynamicRange = _colorRange,
                DelayMs = delay,
            };
            if (!DisplayActionRunner.HasChanges(action))
            {
                ShowError(_error, Texts.ErrorNothingToChange);
                return false;
            }
            Result = action;
            return true;
        }
    }
}
