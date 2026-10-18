using System;
using System.Collections.Generic;
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
    internal sealed class ProfilesPage : Grid, IPage
    {
        private const double CardWidth = 200;
        private const double CardHeight = 108;
        private const string DragFormat = "AutoTrigger.ProfileAction";

        private readonly SettingsController _settings;
        private readonly IProcessSystem _processes;
        private readonly DisplayService _displays;
        private readonly IAudioHardware _audio;
        private readonly IPowerPlans _powerPlans;
        private readonly Func<Window> _owner;
        private readonly StackPanel _list = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
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
            IAudioHardware audio, IPowerPlans powerPlans, Func<Window> owner)
        {
            _settings = settings;
            _processes = processes;
            _displays = displays;
            _audio = audio;
            _powerPlans = powerPlans;
            _owner = owner;
            this.Bind(BackgroundProperty, Res.Layer);

            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 400, MaxWidth = 440 });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star), MinWidth = 480 });

            var left = new Grid { Margin = new Thickness(32, 24, 0, 24) };
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.Children.Add(Kit.Heading(Texts.TabProfiles));

            _search = Kit.TextField(string.Empty, text => OnSearchChanged(), Texts.SearchProfiles);
            var searchRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchRow.Children.Add(_search);
            UiButton sort = Kit.SortButton(SortProfiles);
            sort.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(sort, 1);
            searchRow.Children.Add(sort);
            Grid.SetRow(searchRow, 1);
            left.Children.Add(searchRow);

            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _list,
            };
            Border listCard = Res.Card(scroller, new Thickness(4));
            listCard.Margin = new Thickness(0, 12, 0, 0);
            listCard.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetRow(listCard, 2);
            left.Children.Add(listCard);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var add = Kit.Button(Texts.Add, Kit.IconPlus);
            add.Click += (s, e) => NewProfile();
            _duplicate = Kit.Button(Texts.Duplicate, Kit.IconCopy);
            _duplicate.Margin = new Thickness(8, 0, 0, 0);
            _duplicate.Click += (s, e) => DuplicateProfile();
            _delete = Kit.Button(Texts.Delete, Kit.IconMinus);
            _delete.Margin = new Thickness(8, 0, 0, 0);
            _delete.Click += (s, e) => DeleteProfile();
            _up = Kit.IconButton(Kit.IconUp, Texts.MoveUp);
            _up.Margin = new Thickness(8, 0, 0, 0);
            _up.Click += (s, e) => MoveProfile(-1);
            _down = Kit.IconButton(Kit.IconDown, Texts.MoveDown);
            _down.Margin = new Thickness(8, 0, 0, 0);
            _down.Click += (s, e) => MoveProfile(1);
            buttons.Children.Add(add);
            buttons.Children.Add(_duplicate);
            buttons.Children.Add(_delete);
            buttons.Children.Add(_up);
            buttons.Children.Add(_down);
            Grid.SetRow(buttons, 3);
            left.Children.Add(buttons);
            Children.Add(left);

            var right = new Grid { Margin = new Thickness(0, 24, 32, 24) };
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.Children.Add(Kit.Heading(Texts.CurrentProfile));
            var editorScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _editorHost,
                Margin = new Thickness(0, 12, 0, 0),
            };
            Grid.SetRow(editorScroll, 1);
            right.Children.Add(editorScroll);
            Grid.SetColumn(right, 2);
            Children.Add(right);
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
            catch (Exception ex)
            {
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
                none.Margin = new Thickness(12);
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

        private static string DisplayName(Profile p) => string.IsNullOrWhiteSpace(p.Name) ? Texts.Unnamed : p.Name;

        private void NewProfile()
        {
            string name = Texts.NewProfileName;
            int number = 2;
            while (Settings.Profiles.Any(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase)))
                name = Texts.NewProfileName + " " + number++;
            var profile = new Profile { Name = name };
            Settings.Profiles.Add(profile);
            _settings.Commit();
            _selectedId = profile.Id;
            _search.Text = string.Empty;
            RebuildList();
            ShowEditor();
            ScrollToSelected();
            _nameBox?.Focus();
            _nameBox?.SelectAll();
        }

        private void DuplicateProfile()
        {
            Profile source = _rows.ContainsKey(_selectedId) ? Settings.FindProfile(_selectedId) : null;
            if (source == null)
                return;
            string name = ProfileCopy.CopyName(Settings.Profiles.Select(p => p.Name), DisplayName(source), Texts.CopyWord);
            Profile copy = ProfileCopy.Duplicate(source, name);
            Settings.Profiles.Add(copy);
            _settings.Commit();
            _selectedId = copy.Id;
            _search.Text = string.Empty;
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
                empty.Margin = new Thickness(4);
                _editorHost.Child = empty;
                return;
            }

            var content = new StackPanel();
            Grid top = Kit.PairGrid();
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
            content.Children.Add(top);

            content.Children.Add(Kit.Separator(new Thickness(0, 20, 0, 0)));
            TextBlock actions = Kit.Heading(Texts.Actions, 16);
            actions.Margin = new Thickness(0, 16, 0, 0);
            content.Children.Add(actions);

            Grid events = Kit.PairGrid();
            events.ColumnDefinitions[0].Width = GridLength.Auto;
            events.ColumnDefinitions[1].Width = new GridLength(16);
            events.Margin = new Thickness(0, 16, 0, 0);
            foreach (EventKind kind in ActionText.Events)
            {
                var row = new EventRow(this, profile, kind);
                Kit.AddPair(events, row.Label, row, 16);
                row.Label.VerticalAlignment = VerticalAlignment.Top;
                row.VerticalAlignment = VerticalAlignment.Top;
            }
            content.Children.Add(events);

            _editorHost.Child = Res.Card(content, new Thickness(24, 20, 24, 20));
        }

        private sealed class EventRow : Grid
        {
            private readonly ProfilesPage _page;
            private readonly Profile _profile;
            private readonly EventKind _kind;
            private const double CardGap = 8;
            private const double MarkerWidth = 3;

            private readonly StackPanel _cards = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
            private readonly Grid _dropArea = new Grid { Background = Brushes.Transparent, AllowDrop = true };
            private readonly Canvas _overlay = new Canvas { IsHitTestVisible = false };
            private readonly Rectangle _marker = new Rectangle
            {
                Width = MarkerWidth,
                Height = CardHeight,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Visibility = Visibility.Collapsed,
            };
            private readonly ScrollViewer _scroller;
            private readonly TextBlock _count;

            public EventRow(ProfilesPage page, Profile profile, EventKind kind)
            {
                _page = page;
                _profile = profile;
                _kind = kind;

                var label = new StackPanel();
                label.Children.Add(Res.Text(ActionText.EventName(kind)));
                _count = Kit.Caption(string.Empty);
                label.Children.Add(_count);
                Label = new Border { Padding = new Thickness(0, 10, 0, 0), Child = label };

                ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var add = new AddTileButton(CardHeight) { VerticalAlignment = VerticalAlignment.Top };
                System.Windows.Automation.AutomationProperties.SetName(add, Texts.AddAction + " " + ActionText.EventName(kind));
                add.Click += (s, e) => AddAction();
                Children.Add(add);

                _marker.SetResourceReference(Shape.FillProperty, Res.AccentFill);
                _overlay.Children.Add(_marker);
                _dropArea.Children.Add(_cards);
                _dropArea.Children.Add(_overlay);
                _dropArea.PreviewDragOver += OnDragOver;
                _dropArea.DragLeave += (s, e) =>
                {
                    Point p = e.GetPosition(_dropArea);
                    if (p.X < 0 || p.Y < 0 || p.X >= _dropArea.ActualWidth || p.Y >= _dropArea.ActualHeight)
                        HideMarker();
                };
                _dropArea.PreviewDrop += OnDrop;

                _scroller = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = _dropArea,
                    Margin = new Thickness(2, 0, 0, 0),
                    Padding = new Thickness(0, 0, 0, 8),
                };
                _scroller.PreviewMouseWheel += (s, e) =>
                {
                    e.Handled = true;
                    if (Keyboard.Modifiers == ModifierKeys.Shift)
                    {
                        if (_scroller.ScrollableWidth > 0)
                            _scroller.ScrollToHorizontalOffset(_scroller.HorizontalOffset - e.Delta);
                        return;
                    }
                    RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = MouseWheelEvent, Source = this });
                };
                Grid.SetColumn(_scroller, 1);
                Children.Add(_scroller);
                Rebuild();
            }

            public Border Label { get; }

            private List<ProfileAction> Actions => _profile.For(_kind);

            private void Rebuild()
            {
                _cards.Children.Clear();
                _count.Text = ActionText.Count(Actions.Count);
                if (Actions.Count == 0)
                {
                    var empty = new Rectangle
                    {
                        Height = CardHeight,
                        Width = CardWidth,
                        RadiusX = 6,
                        RadiusY = 6,
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 4, 3 },
                    };
                    empty.SetResourceReference(Shape.StrokeProperty, Res.CardStroke);
                    _cards.Children.Add(empty);
                    return;
                }
                List<ValidationIssue> issues = _page._settings.Issues
                    .Where(i => i.ProfileId == _profile.Id && i.Event == _kind)
                    .ToList();
                HashSet<Guid> plans = Actions.Any(a => a is PowerPlanAction) ? _page.PowerPlanIds() : null;
                for (int i = 0; i < Actions.Count; i++)
                {
                    ValidationIssue issue = issues.FirstOrDefault(x => x.ActionIndex == i);
                    if (issue == null && plans != null && Actions[i] is PowerPlanAction plan && !plans.Contains(plan.PlanId))
                        issue = new ValidationIssue { Kind = IssueKind.MissingPowerPlan, ProfileId = _profile.Id, Event = _kind, ActionIndex = i };
                    _cards.Children.Add(Card(Actions[i], i, issue));
                }
            }

            private void Changed()
            {
                _page._settings.Commit();
                Rebuild();
            }

            private void AddAction()
            {
                ActionDialog dialog = NewDialog(null, Actions.Count + 1);
                if (!dialog.Run(_page._owner()))
                    return;
                Actions.Add(dialog.Result);
                Changed();
                _scroller.ScrollToRightEnd();
            }

            private void Edit(ProfileAction action, int position)
            {
                if (action is UnknownAction)
                {
                    MessageDialog.Show(_page._owner(), Texts.EditAction, Texts.EditLater, Texts.Ok);
                    return;
                }
                ActionDialog dialog = NewDialog(action, position);
                if (!dialog.Run(_page._owner()))
                    return;
                int index = Actions.IndexOf(action);
                if (index < 0)
                    return;
                Actions[index] = dialog.Result;
                Changed();
            }

            private ActionDialog NewDialog(ProfileAction action, int position) =>
                new ActionDialog(_page.Settings, _profile, _kind, action, position,
                    _page._processes, _page._displays, _page._audio, _page._powerPlans);

            private UIElement Card(ProfileAction action, int index, ValidationIssue issue)
            {
                var card = new Border
                {
                    Width = CardWidth,
                    Height = CardHeight,
                    Margin = new Thickness(0, 0, CardGap, 0),
                    Padding = new Thickness(12, 8, 10, 10),
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    Tag = action,
                };
                card.SetResourceReference(Border.BackgroundProperty, Res.ControlFillDefault);
                card.SetResourceReference(Border.BorderBrushProperty, issue != null ? Res.Critical : Res.CardStroke);

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                grid.Children.Add(CardHeader(action, index, issue));

                TextBlock name = Res.Text(ActionText.Summary(action, _page.Settings), 14, FontWeights.SemiBold);
                name.Margin = new Thickness(0, 4, 0, 0);
                string details = ActionText.Details(action, _page.Settings);
                if (!string.IsNullOrEmpty(details))
                    name.ToolTip = details;
                Grid.SetRow(name, 1);
                grid.Children.Add(name);

                UIElement delay = DelayRow(action);
                Grid.SetRow(delay, 3);
                grid.Children.Add(delay);

                card.Child = grid;
                EnableDrag(card, action);
                return card;
            }

            private Grid CardHeader(ProfileAction action, int index, ValidationIssue issue)
            {
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.Children.Add(PositionBadge(index));

                string typeText = issue != null ? IssueText(issue.Kind) : ActionText.TypeName(action);
                TextBlock type = Res.Text(typeText, 12, null, issue != null ? Res.Critical : Res.TextPrimary);
                type.Margin = new Thickness(8, 0, 4, 0);
                Grid.SetColumn(type, 1);
                header.Children.Add(type);

                var edit = Kit.IconButton(Kit.IconPencil, Texts.EditAction, 28, true, 14);
                edit.Click += (s, e) => Edit(action, index + 1);
                Grid.SetColumn(edit, 2);
                header.Children.Add(edit);

                var remove = Kit.IconButton(Kit.IconMinusSmall, Texts.RemoveAction, 28, true, 14);
                remove.Click += (s, e) =>
                {
                    Actions.Remove(action);
                    Changed();
                };
                Grid.SetColumn(remove, 3);
                header.Children.Add(remove);
                return header;
            }

            private static Border PositionBadge(int index)
            {
                var badge = new Border
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = new CornerRadius(9),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                badge.SetResourceReference(Border.BorderBrushProperty, Res.AccentFill);
                string position = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                TextBlock text = Res.Text(position, 11, FontWeights.SemiBold, Res.AccentFill);
                text.HorizontalAlignment = HorizontalAlignment.Center;
                text.TextTrimming = TextTrimming.None;
                badge.Child = text;
                return badge;
            }

            private static string IssueText(IssueKind kind) => kind switch
            {
                IssueKind.Loop => Texts.LoopIgnored,
                IssueKind.MissingDisplay => Texts.MissingDisplayIgnored,
                IssueKind.MissingPowerPlan => Texts.PowerPlanNotFound,
                _ => Texts.MissingIgnored,
            };

            private StackPanel DelayRow(ProfileAction action)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
                row.Children.Add(Kit.Caption(Texts.Delay));
                TextBox box = Kit.SecondsBox(action.DelayMs, value =>
                {
                    action.DelayMs = value;
                    _page._settings.Commit();
                });
                box.Padding = new Thickness(8, 4, 8, 4);
                box.MinHeight = 0;
                box.Margin = new Thickness(6, 0, 6, 0);
                System.Windows.Automation.AutomationProperties.SetName(box, Texts.Delay);
                row.Children.Add(box);
                row.Children.Add(Kit.Caption(Texts.Seconds));
                return row;
            }

            private void EnableDrag(Border card, ProfileAction action)
            {
                Point start = default;
                bool pressed = false;
                card.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    pressed = !(e.OriginalSource is DependencyObject d && Kit.HasAncestor(d, card, x => x is TextBox || x is ButtonBase));
                    start = e.GetPosition(card);
                };
                card.PreviewMouseLeftButtonUp += (s, e) => pressed = false;
                card.MouseMove += (s, e) =>
                {
                    if (!pressed || e.LeftButton != MouseButtonState.Pressed)
                        return;
                    Vector moved = e.GetPosition(card) - start;
                    if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
                        && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
                        return;
                    pressed = false;
                    card.Opacity = 0.5;
                    try
                    {
                        DragDrop.DoDragDrop(card, new DataObject(DragFormat, action), DragDropEffects.Move);
                    }
                    finally
                    {
                        card.Opacity = 1;
                        HideMarker();
                    }
                };
            }

            private ProfileAction Dragged(DragEventArgs e) =>
                e.Data.GetData(DragFormat) is ProfileAction dragged && Actions.Contains(dragged) ? dragged : null;

            private void OnDragOver(object sender, DragEventArgs e)
            {
                e.Handled = true;
                ProfileAction dragged = Dragged(e);
                if (dragged == null)
                {
                    e.Effects = DragDropEffects.None;
                    HideMarker();
                    return;
                }
                e.Effects = DragDropEffects.Move;
                ScrollNearEdge(e);
                int from = Actions.IndexOf(dragged);
                int to = DropIndex(e, out double x);
                if (to == from || to == from + 1)
                {
                    HideMarker();
                    return;
                }
                Canvas.SetLeft(_marker, x);
                Canvas.SetTop(_marker, 0);
                _marker.Visibility = Visibility.Visible;
            }

            private void OnDrop(object sender, DragEventArgs e)
            {
                e.Handled = true;
                HideMarker();
                ProfileAction dragged = Dragged(e);
                if (dragged == null)
                    return;
                int from = Actions.IndexOf(dragged);
                int to = DropIndex(e, out _);
                if (to == from || to == from + 1)
                    return;
                Actions.RemoveAt(from);
                Actions.Insert(to > from ? to - 1 : to, dragged);
                Changed();
            }

            private int DropIndex(DragEventArgs e, out double x)
            {
                double mouse = e.GetPosition(_dropArea).X;
                List<Border> cards = _cards.Children.OfType<Border>().Where(b => b.Tag is ProfileAction).ToList();
                for (int i = 0; i < cards.Count; i++)
                {
                    double left = cards[i].TranslatePoint(new Point(0, 0), _dropArea).X;
                    if (mouse < left + cards[i].ActualWidth / 2)
                    {
                        x = Math.Max(0, left - CardGap / 2 - MarkerWidth / 2);
                        return i;
                    }
                }
                Border last = cards[cards.Count - 1];
                x = last.TranslatePoint(new Point(last.ActualWidth, 0), _dropArea).X + CardGap / 2 - MarkerWidth / 2;
                return cards.Count;
            }

            private void ScrollNearEdge(DragEventArgs e)
            {
                if (_scroller.ScrollableWidth <= 0)
                    return;
                double x = e.GetPosition(_scroller).X;
                if (x < 40)
                    _scroller.ScrollToHorizontalOffset(_scroller.HorizontalOffset - 20);
                else if (x > _scroller.ViewportWidth - 40)
                    _scroller.ScrollToHorizontalOffset(_scroller.HorizontalOffset + 20);
            }

            private void HideMarker() => _marker.Visibility = Visibility.Collapsed;
        }
    }
}
