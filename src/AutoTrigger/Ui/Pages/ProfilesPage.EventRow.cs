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
    internal sealed partial class ProfilesPage
    {
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
                HashSet<Guid> plans = Actions.Any(a => a is WindowsSettingsAction w && w.ChangesPowerPlan) ? _page.PowerPlanIds() : null;
                for (int i = 0; i < Actions.Count; i++)
                {
                    ValidationIssue issue = issues.FirstOrDefault(x => x.ActionIndex == i);
                    if (issue == null && plans != null && Actions[i] is WindowsSettingsAction plan && plan.ChangesPowerPlan && !plans.Contains(plan.PlanId))
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

            private Border Card(ProfileAction action, int index, ValidationIssue issue)
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
