using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Model;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace AutoTrigger.Ui
{
    internal sealed partial class ProfilesPage : Grid, IPage
    {
        private const double CardWidth = 200;
        private const double CardHeight = 108;
        private const string DragFormat = "AutoTrigger.ProfileAction";

        private readonly SettingsController _settings;
        private readonly IProcessSystem _processes;
        private readonly DisplayService _displays;
        private readonly IAudioHardware _audio;
        private readonly IPowerPlans _powerPlans;
        private readonly HotkeyService _hotkeys;
        private readonly Func<Window> _owner;
        private readonly StackPanel _list = new StackPanel { Margin = Spacing.Right(Spacing.Medium) };
        private readonly Dictionary<Guid, ListRow> _rows = new Dictionary<Guid, ListRow>();
        private readonly Border _editorHost = new Border();
        private readonly UiTextBox _search;
        private readonly UiButton _duplicate;
        private readonly UiButton _delete;
        private readonly UiButton _up;
        private readonly UiButton _down;
        private Guid _selectedId;
        private UiTextBox _nameBox;

        public ProfilesPage(SettingsController settings, IProcessSystem processes, DisplayService displays,
            IAudioHardware audio, IPowerPlans powerPlans, HotkeyService hotkeys, Func<Window> owner)
        {
            _settings = settings;
            _processes = processes;
            _displays = displays;
            _audio = audio;
            _powerPlans = powerPlans;
            _hotkeys = hotkeys;
            _owner = owner;
            this.Bind(BackgroundProperty, Res.Layer);

            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 400, MaxWidth = 440 });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Spacing.Section) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MinWidth = 480 });

            _search = Kit.TextField(string.Empty, text => OnSearchChanged(), Texts.SearchProfiles);
            _duplicate = Kit.Button(Texts.Duplicate, Kit.IconCopy);
            _duplicate.Click += (s, e) => DuplicateProfile();
            _delete = Kit.Button(Texts.Delete, Kit.IconMinus);
            _delete.Click += (s, e) => DeleteProfile();
            _up = Kit.IconButton(Kit.IconUp, Texts.MoveUp);
            _up.Click += (s, e) => MoveProfile(-1);
            _down = Kit.IconButton(Kit.IconDown, Texts.MoveDown);
            _down.Click += (s, e) => MoveProfile(1);

            Children.Add(BuildListColumn());
            Grid editorColumn = BuildEditorColumn();
            Grid.SetColumn(editorColumn, 2);
            Children.Add(editorColumn);
        }

        private Grid BuildListColumn()
        {
            var left = new Grid { Margin = Spacing.PageLeftColumn };
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.Children.Add(Kit.Heading(Texts.TabProfiles));

            Grid searchRow = BuildSearchRow();
            Grid.SetRow(searchRow, 1);
            left.Children.Add(searchRow);

            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _list,
            };
            Border listCard = Res.Card(scroller, Spacing.ListInset);
            listCard.Margin = Spacing.Top(Spacing.Medium);
            listCard.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(listCard, 2);
            left.Children.Add(listCard);

            StackPanel buttons = BuildButtonRow();
            Grid.SetRow(buttons, 3);
            left.Children.Add(buttons);
            return left;
        }

        private Grid BuildSearchRow()
        {
            var searchRow = new Grid { Margin = Spacing.Top(Spacing.Medium) };
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchRow.Children.Add(_search);
            UiButton sort = Kit.SortButton(SortProfiles);
            sort.Margin = Spacing.Left(Spacing.Small);
            Grid.SetColumn(sort, 1);
            searchRow.Children.Add(sort);
            return searchRow;
        }

        private StackPanel BuildButtonRow()
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = Spacing.Top(Spacing.Medium) };
            var add = Kit.Button(Texts.Add, Kit.IconPlus);
            add.Click += (s, e) => NewProfile();
            buttons.Children.Add(add);
            foreach (UiButton button in new[] { _duplicate, _delete, _up, _down })
            {
                button.Margin = Spacing.Left(Spacing.Small);
                buttons.Children.Add(button);
            }
            return buttons;
        }

        private Grid BuildEditorColumn()
        {
            var right = new Grid { Margin = Spacing.PageRightColumn };
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.Children.Add(Kit.Heading(Texts.CurrentProfile));
            var editorScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _editorHost,
                Margin = Spacing.Top(Spacing.Medium),
            };
            Grid.SetRow(editorScroll, 1);
            right.Children.Add(editorScroll);
            return right;
        }

        private AppSettings Settings => _settings.Current;

        private HashSet<Guid> PowerPlanIds()
        {
            if (_powerPlans == null)
                return null;
            try
            {
                return new HashSet<Guid>(_powerPlans.GetPlans().Select(p => p.Id));
            }
            catch (Win32Exception ex)
            {
                // Without the list, power plan references are not checked.
                AutoTrigger.Core.Logging.Log.Warning("Power plans cannot be listed", ex);
                return null;
            }
        }

        public void OnShown()
        {
            if (Settings.FindProfile(_selectedId) == null)
                _selectedId = Settings.Profiles.FirstOrDefault()?.Id ?? Guid.Empty;
            RebuildList();
            ShowEditor();
        }

        public void Select(Guid profileId)
        {
            if (Settings.FindProfile(profileId) == null)
                return;
            _selectedId = profileId;
            _search.Text = string.Empty;
            RebuildList();
            ShowEditor();
            ScrollToSelected();
        }

        private string SearchText => _search?.Text?.Trim() ?? string.Empty;

        private IEnumerable<Profile> Visible()
        {
            string query = SearchText;
            return Settings.Profiles.Where(profile => query.Length == 0
                || (profile.Name ?? string.Empty).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        private void OnSearchChanged()
        {
            RebuildList();
            if (_rows.ContainsKey(_selectedId))
                return;
            Profile first = Visible().FirstOrDefault();
            if (first != null)
                SelectFromList(first.Id);
        }

        private void RebuildList()
        {
            _list.Children.Clear();
            _rows.Clear();
            foreach (Profile profile in Visible())
            {
                TextBlock name = Res.Text(DisplayName(profile));
                var row = new ListRow(name, new Thickness(12, 7, 12, 7)) { IsSelected = profile.Id == _selectedId };
                Guid id = profile.Id;
                row.Clicked += () => SelectFromList(id);
                _rows[id] = row;
                _list.Children.Add(row);
            }
            if (_list.Children.Count == 0)
            {
                TextBlock none = Kit.Caption(Settings.Profiles.Count == 0 ? Texts.NoProfiles : Texts.NothingFound);
                none.Margin = new Thickness(Spacing.Medium);
                none.TextWrapping = TextWrapping.Wrap;
                _list.Children.Add(none);
            }
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            Profile profile = _rows.ContainsKey(_selectedId) ? Settings.FindProfile(_selectedId) : null;
            int index = profile == null ? -1 : Settings.Profiles.IndexOf(profile);
            bool ordering = SearchText.Length == 0;
            _duplicate.IsEnabled = profile != null;
            _delete.IsEnabled = profile != null;
            _up.IsEnabled = ordering && index > 0;
            _down.IsEnabled = ordering && index >= 0 && index < Settings.Profiles.Count - 1;
        }

        private void SelectFromList(Guid id)
        {
            if (id == _selectedId)
                return;
            if (_rows.TryGetValue(_selectedId, out ListRow old))
                old.IsSelected = false;
            _selectedId = id;
            if (_rows.TryGetValue(id, out ListRow row))
                row.IsSelected = true;
            UpdateButtons();
            ShowEditor();
        }

        private void ScrollToSelected()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (_rows.TryGetValue(_selectedId, out ListRow row))
                    row.BringIntoView();
            }));
        }

        private void MoveProfile(int step)
        {
            Profile profile = Settings.FindProfile(_selectedId);
            if (profile == null || SearchText.Length > 0 || !Ordering.Move(Settings.Profiles, profile, step))
                return;
            _settings.Commit();
            RebuildList();
            ScrollToSelected();
        }

        private void SortProfiles(bool descending)
        {
            Ordering.SortByName(Settings.Profiles, DisplayName, descending);
            _settings.Commit();
            RebuildList();
            ScrollToSelected();
        }

        private static string DisplayName(Profile p) => ActionText.ProfileName(p.Name);

        private void NewProfile()
        {
            string name = Texts.NewProfileName;
            int number = 2;
            while (Settings.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                name = Texts.NewProfileName + " " + number++;
            ShowNewProfile(new Profile { Name = name });
        }

        private void DuplicateProfile()
        {
            Profile source = _rows.ContainsKey(_selectedId) ? Settings.FindProfile(_selectedId) : null;
            if (source == null)
                return;
            string name = ProfileCopy.CopyName(Settings.Profiles.Select(p => p.Name), DisplayName(source), Texts.CopyWord);
            ShowNewProfile(ProfileCopy.Duplicate(source, name));
        }

        private void ShowNewProfile(Profile profile)
        {
            Settings.Profiles.Add(profile);
            _settings.Commit();
            _selectedId = profile.Id;
            if (_search.Text.Length > 0)
                _search.Text = string.Empty;
            else
                RebuildList();
            ShowEditor();
            ScrollToSelected();
            _nameBox?.Focus();
            _nameBox?.SelectAll();
        }

        private void DeleteProfile()
        {
            Profile profile = _rows.ContainsKey(_selectedId) ? Settings.FindProfile(_selectedId) : null;
            if (profile == null)
                return;

            int index = Settings.Profiles.IndexOf(profile);
            foreach (AppEntry app in Settings.Applications.Where(a => a.ProfileId == profile.Id))
                app.ProfileId = Guid.Empty;
            Settings.Profiles.Remove(profile);
            _settings.Commit();
            _selectedId = Settings.Profiles.Count == 0 ? Guid.Empty
                : Settings.Profiles[Math.Min(index, Settings.Profiles.Count - 1)].Id;
            RebuildList();
            ShowEditor();
        }

        private void ShowEditor()
        {
            Profile profile = Settings.FindProfile(_selectedId);
            if (profile == null)
            {
                _nameBox = null;
                TextBlock empty = Kit.Caption(Texts.NoProfileSelected);
                empty.Margin = new Thickness(Spacing.Tight);
                _editorHost.Child = empty;
                return;
            }

            var content = new StackPanel();
            Grid top = Kit.PairGrid(200);
            _nameBox = Kit.TextField(profile.Name, text =>
            {
                profile.Name = text;
                if (_rows.TryGetValue(profile.Id, out ListRow row) && row.Child is Grid g && g.Children.Count > 1 && g.Children[1] is TextBlock t)
                    t.Text = DisplayName(profile);
                _settings.Commit();
            });
            Kit.AddPair(top, Res.Text(Texts.Name), _nameBox, 14);
            var restart = Kit.Switch(profile.RestartApp, on =>
            {
                profile.RestartApp = on;
                _settings.Commit();
            }, Texts.RestartApp);
            restart.HorizontalAlignment = HorizontalAlignment.Left;
            Kit.AddPair(top, Res.Text(Texts.RestartApp), restart, 14);
            AddShortcut(top, profile, EventKind.Started, Texts.KeyboardShortcutStarted);
            AddShortcut(top, profile, EventKind.Closed, Texts.KeyboardShortcutClosed);
            content.Children.Add(top);

            content.Children.Add(Kit.Separator(new Thickness(0, 20, 0, 0)));
            TextBlock actions = Kit.Heading(Texts.Actions, 16);
            actions.Margin = Spacing.Top(Spacing.Large);
            content.Children.Add(actions);

            Grid events = Kit.PairGrid();
            events.ColumnDefinitions[0].Width = GridLength.Auto;
            events.ColumnDefinitions[1].Width = new GridLength(Spacing.Large);
            events.Margin = Spacing.Top(Spacing.Large);
            foreach (EventKind kind in ActionText.Events)
            {
                var row = new EventRow(this, profile, kind);
                Kit.AddPair(events, row.Label, row, 16);
                row.Label.VerticalAlignment = VerticalAlignment.Top;
                row.VerticalAlignment = VerticalAlignment.Top;
            }
            content.Children.Add(events);

            _editorHost.Child = Res.Card(content, Spacing.Panel);
        }

        private void AddShortcut(Grid top, Profile profile, EventKind kind, string label)
        {
            TextBlock shortcutLabel = Res.Text(label);
            var shortcut = new ShortcutEditor(profile, kind, _settings, _hotkeys);
            Kit.AddPair(top, shortcutLabel, shortcut, 14);
            shortcutLabel.VerticalAlignment = VerticalAlignment.Top;
            shortcutLabel.Margin = new Thickness(0, 14 + 7, 0, 0);
            shortcut.VerticalAlignment = VerticalAlignment.Top;
        }
    }
}
