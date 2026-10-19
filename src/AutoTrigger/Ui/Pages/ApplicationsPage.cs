using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Model;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace AutoTrigger.Ui
{
    internal sealed class ApplicationsPage : Grid, IPage
    {
        private const double NameColumn = 160;
        private const double ProfileColumn = 205;
        private const double FixedColumns = 44 + 16 + 24 + 24 + 150;
        private const double TableChrome = 16 + 16 + 2 + 16;
        private const double ComboChrome = 56;
        private const double TextSize = 14;

        private readonly SettingsController _settings;
        private readonly IProcessSystem _processes;
        private readonly AppIcons _icons;
        private readonly Func<Window> _owner;
        private readonly StackPanel _rows = new StackPanel();
        private readonly List<Grid> _rowGrids = new List<Grid>();
        private readonly Grid _root;
        private readonly Grid _headings;
        private double _nameNeed = NameColumn;
        private double _profileNeed = ProfileColumn;
        private int _measuredVersion = -1;
        private readonly Dictionary<Guid, ListRow> _rowById = new Dictionary<Guid, ListRow>();
        private readonly TextBlock _count;
        private readonly UiTextBox _search;
        private readonly UiButton _remove;
        private readonly UiButton _edit;
        private readonly UiButton _up;
        private readonly UiButton _down;
        private Guid _selectedId;
        private int _shownVersion = -1;

        public ApplicationsPage(SettingsController settings, IProcessSystem processes, AppIcons icons, Func<Window> owner)
        {
            _settings = settings;
            _processes = processes;
            _icons = icons;
            _owner = owner;
            this.Bind(BackgroundProperty, Res.Layer);
            Margin = new Thickness(0);
            var root = new Grid { Margin = new Thickness(32, 24, 32, 24) };
            _root = root;
            root.SizeChanged += (s, e) =>
            {
                if (e.WidthChanged)
                    ApplyColumns();
            };
            Loaded += (s, e) =>
            {
                MeasureNeeds();
                ApplyColumns();
            };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(Kit.Heading(Texts.Applications));
            _count = Res.Text(string.Empty);
            _count.Margin = new Thickness(12, 4, 0, 0);
            header.Children.Add(_count);
            root.Children.Add(header);

            _search = Kit.TextField(string.Empty, text => OnSearchChanged(), Texts.SearchApplications);
            _search.Width = 300;
            var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            searchRow.Children.Add(_search);
            UiButton sort = Kit.SortButton(SortApplications);
            sort.Margin = new Thickness(8, 0, 0, 0);
            searchRow.Children.Add(sort);
            Grid.SetRow(searchRow, 1);
            root.Children.Add(searchRow);

            var table = new Grid();
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid headings = Columns();
            _headings = headings;
            headings.Margin = new Thickness(16, 10, 16, 10);
            TextBlock appHeading = Kit.Caption(Texts.Application);
            Grid.SetColumnSpan(appHeading, 3);
            headings.Children.Add(appHeading);
            TextBlock profileHeading = Kit.Caption(Texts.Profile);
            Grid.SetColumn(profileHeading, 4);
            headings.Children.Add(profileHeading);
            TextBlock monitoringHeading = Kit.Caption(Texts.Monitoring);
            Grid.SetColumn(monitoringHeading, 6);
            headings.Children.Add(monitoringHeading);
            var headingBorder = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = headings };
            headingBorder.SetResourceReference(Border.BorderBrushProperty, Res.CardStroke);
            table.Children.Add(headingBorder);
            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _rows,
                Padding = new Thickness(4, 2, 4, 2),
            };
            Grid.SetRow(scroller, 1);
            table.Children.Add(scroller);
            Border card = Res.Card(table, new Thickness(0));
            card.Margin = new Thickness(0, 12, 0, 0);
            card.VerticalAlignment = VerticalAlignment.Top;
            card.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(card, 2);
            root.Children.Add(card);

            var bar = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
            var add = Kit.Button(Texts.Add, Kit.IconPlus);
            add.Click += (s, e) => AddApplication();
            _remove = Kit.Button(Texts.Delete, Kit.IconMinus);
            _remove.Click += (s, e) => RemoveApplication();
            _edit = Kit.Button(Texts.Edit, Kit.IconPencil);
            _edit.Click += (s, e) => EditApplication();
            _up = Kit.IconButton(Kit.IconUp, Texts.MoveUp);
            _up.Click += (s, e) => Move(-1);
            _down = Kit.IconButton(Kit.IconDown, Texts.MoveDown);
            _down.Click += (s, e) => Move(1);
            foreach (UiButton b in new[] { add, _remove, _edit, _up, _down })
            {
                b.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(b, Dock.Left);
                bar.Children.Add(b);
            }
            Grid.SetRow(bar, 3);
            root.Children.Add(bar);

            Children.Add(root);
        }

        private AppSettings Settings => _settings.Current;

        public void OnShown()
        {
            if (_shownVersion != _settings.Version)
            {
                _selectedId = Guid.Empty;
                Rebuild();
            }
            else
            {
                Select(Guid.Empty);
            }
        }

        private static Grid Columns()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NameColumn) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ProfileColumn) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            return grid;
        }

        private void MeasureNeeds()
        {
            _nameNeed = Settings.Applications.Select(a => TextWidth(DisplayName(a))).DefaultIfEmpty(0).Max();
            _nameNeed = Math.Max(NameColumn, _nameNeed);
            double profiles = Kit.ProfileChoices(Settings).Select(p => TextWidth(p.Value)).DefaultIfEmpty(0).Max();
            _profileNeed = Math.Max(ProfileColumn, profiles + ComboChrome);
        }

        private double TextWidth(string text)
        {
            var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var formatted = new FormattedText(text ?? string.Empty, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                typeface, TextSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return Math.Ceiling(formatted.WidthIncludingTrailingWhitespace) + 2;
        }

        private void ApplyColumns()
        {
            double available = _root.ActualWidth - TableChrome - FixedColumns;
            (double name, double profile) = FitColumns(_nameNeed, _profileNeed, available);
            foreach (Grid grid in _rowGrids.Prepend(_headings))
            {
                grid.ColumnDefinitions[2].Width = new GridLength(name);
                grid.ColumnDefinitions[4].Width = new GridLength(profile);
            }
        }

        private static (double Name, double Profile) FitColumns(double name, double profile, double available)
        {
            if (available <= 0 || name + profile <= available)
                return (name, profile);
            double extraName = name - NameColumn;
            double extraProfile = profile - ProfileColumn;
            double room = available - NameColumn - ProfileColumn;
            if (room <= 0 || extraName + extraProfile <= 0)
                return (NameColumn, ProfileColumn);
            double scale = room / (extraName + extraProfile);
            return (NameColumn + extraName * scale, ProfileColumn + extraProfile * scale);
        }

        private string SearchText => _search?.Text?.Trim() ?? string.Empty;

        private static string DisplayName(AppEntry app) => string.IsNullOrWhiteSpace(app.Name) ? app.FileName : app.Name;

        private IEnumerable<AppEntry> Visible()
        {
            string query = SearchText;
            return Settings.Applications.Where(app => query.Length == 0
                || (DisplayName(app) ?? string.Empty).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        private void OnSearchChanged() => Rebuild();

        private void Rebuild()
        {
            _shownVersion = _settings.Version;
            _rows.Children.Clear();
            _rowGrids.Clear();
            _rowById.Clear();
            _count.Text = Settings.Applications.Count.ToString(CultureInfo.CurrentCulture);
            List<KeyValuePair<Guid, string>> profiles = Kit.ProfileChoices(Settings);
            foreach (AppEntry app in Visible())
            {
                ListRow row = AppRow(app, profiles);
                _rowById[app.Id] = row;
                _rows.Children.Add(row);
            }
            if (_rowById.Count == 0)
            {
                TextBlock none = Kit.Caption(Settings.Applications.Count == 0 ? Texts.NoApplicationsYet : Texts.NothingFound);
                none.Margin = new Thickness(12);
                _rows.Children.Add(none);
            }
            if (_measuredVersion != _settings.Version)
            {
                _measuredVersion = _settings.Version;
                MeasureNeeds();
            }
            ApplyColumns();
            UpdateButtons();
        }

        private ListRow AppRow(AppEntry app, List<KeyValuePair<Guid, string>> profiles)
        {
            Grid grid = Columns();
            _rowGrids.Add(grid);
            var icon = new AppIconBox(36, 6);
            icon.ShowInitials(app.Name);
            _icons.Get(app.Path, icon.ShowImage);
            grid.Children.Add(icon);

            TextBlock name = Res.Text(DisplayName(app));
            Grid.SetColumn(name, 2);
            grid.Children.Add(name);

            ComboBox profile = ProfileCombo(app, profiles);
            Grid.SetColumn(profile, 4);
            grid.Children.Add(profile);

            StackPanel monitoring = MonitoringSwitch(app);
            Grid.SetColumn(monitoring, 6);
            grid.Children.Add(monitoring);

            var row = new ListRow(grid, new Thickness(12, 6, 12, 6)) { Margin = new Thickness(0, 2, 0, 2), IsSelected = app.Id == _selectedId };
            row.Clicked += () => Select(app.Id);
            row.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2 && !IsOnControl(e.OriginalSource, row))
                    EditApplication();
            };
            return row;
        }

        private ComboBox ProfileCombo(AppEntry app, List<KeyValuePair<Guid, string>> profiles)
        {
            ComboBox combo = Kit.LazyCombo(profiles, app.ProfileId, id =>
            {
                app.ProfileId = id;
                _settings.Commit();
                _shownVersion = _settings.Version;
            }, 0);
            Kit.WithScrollBar(combo);
            combo.HorizontalAlignment = HorizontalAlignment.Stretch;
            System.Windows.Automation.AutomationProperties.SetName(combo, Texts.Profile + " " + app.Name);
            return combo;
        }

        private StackPanel MonitoringSwitch(AppEntry app)
        {
            TextBlock state = Res.Text(app.Enabled ? Texts.Enabled : Texts.Disabled);
            state.Margin = new Thickness(10, 0, 0, 0);
            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(Kit.Switch(app.Enabled, on =>
            {
                app.Enabled = on;
                state.Text = on ? Texts.Enabled : Texts.Disabled;
                AutoTrigger.Core.Logging.Log.Info($"{DisplayName(app)}: monitoring {(on ? "enabled" : "disabled")}");
                _settings.Commit();
                _shownVersion = _settings.Version;
            }, Texts.Monitoring + " " + app.Name));
            panel.Children.Add(state);
            return panel;
        }

        private static bool IsOnControl(object source, ListRow row) =>
            source is DependencyObject element
            && Kit.HasAncestor(element, row, x => x is ComboBox || x is System.Windows.Controls.Primitives.ToggleButton);

        private void Select(Guid id)
        {
            if (_rowById.TryGetValue(_selectedId, out ListRow old))
                old.IsSelected = false;
            _selectedId = id;
            if (_rowById.TryGetValue(id, out ListRow row))
                row.IsSelected = true;
            UpdateButtons();
        }

        private AppEntry SelectedVisible() => _rowById.ContainsKey(_selectedId) ? Settings.FindApplication(_selectedId) : null;

        private void UpdateButtons()
        {
            AppEntry app = SelectedVisible();
            int index = app == null ? -1 : Settings.Applications.IndexOf(app);
            bool ordering = SearchText.Length == 0;
            _remove.IsEnabled = app != null;
            _edit.IsEnabled = app != null;
            _up.IsEnabled = ordering && index > 0;
            _down.IsEnabled = ordering && index >= 0 && index < Settings.Applications.Count - 1;
        }

        private void AddApplication()
        {
            var dialog = new ApplicationDialog(Settings, null, _processes, _icons);
            if (!dialog.Run(_owner()))
                return;
            Settings.Applications.Add(dialog.Result);
            _selectedId = dialog.Result.Id;
            _search.Text = string.Empty;
            _settings.Commit();
            Rebuild();
            ScrollToSelected();
        }

        private void ScrollToSelected()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_rowById.TryGetValue(_selectedId, out ListRow row))
                    row.BringIntoView();
            }));
        }

        private void SortApplications(bool descending)
        {
            Ordering.SortByName(Settings.Applications, DisplayName, descending);
            _settings.Commit();
            Rebuild();
            ScrollToSelected();
        }

        private void EditApplication()
        {
            AppEntry app = SelectedVisible();
            if (app == null)
                return;
            var dialog = new ApplicationDialog(Settings, app, _processes, _icons);
            if (!dialog.Run(_owner()))
                return;
            _settings.Commit();
            Rebuild();
        }

        private void RemoveApplication()
        {
            AppEntry app = SelectedVisible();
            if (app == null)
                return;
            int index = Settings.Applications.IndexOf(app);
            Settings.Applications.Remove(app);
            _selectedId = Settings.Applications.Count == 0 ? Guid.Empty
                : Settings.Applications[Math.Min(index, Settings.Applications.Count - 1)].Id;
            _settings.Commit();
            Rebuild();
        }

        private void Move(int step)
        {
            AppEntry app = SelectedVisible();
            if (app == null || SearchText.Length > 0 || !Ordering.Move(Settings.Applications, app, step))
                return;
            _settings.Commit();
            Rebuild();
            ScrollToSelected();
        }
    }
}
