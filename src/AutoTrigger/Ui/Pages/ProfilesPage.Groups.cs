using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Text;

namespace AutoTrigger.Ui
{
    // Groups only sort the list of this page: the engine and the other pages never see them.
    internal sealed partial class ProfilesPage
    {
        // Group names are cleaned of control characters, so this key never names a group.
        private const string NewGroupKey = "\u0001";
        private const double GroupFieldWidth = 280;

        private TextBlock _groupLabel;

        private ProfileFilter Filter => Settings.ProfilesShown ?? ProfileFilter.All;

        // Shown once a group exists, so that a page without groups stays as it was.
        private Grid BuildGroupRow()
        {
            var row = new Grid { Margin = Spacing.Top(Spacing.Small), Visibility = Visibility.Collapsed };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _groupLabel = Res.Text(Texts.Group);
            _groupLabel.VerticalAlignment = VerticalAlignment.Center;
            _groupLabel.Margin = Spacing.Right(Spacing.Medium);
            row.Children.Add(_groupLabel);
            Grid.SetColumn(_groupFilterHost, 1);
            row.Children.Add(_groupFilterHost);
            _rename.Margin = Spacing.Left(Spacing.Small);
            Grid.SetColumn(_rename, 2);
            row.Children.Add(_rename);
            return row;
        }

        // The list of groups is built again only when it changes, never under the pointer of someone choosing in it.
        private void RefreshGroups()
        {
            List<string> names = ProfileGroups.Names(Settings.Profiles);
            ProfileFilter filter = Filter.Within(names);
            if (!ReferenceEquals(filter, Settings.ProfilesShown))
                ChangeFilter(filter);
            if (_filterShown == null || !ReferenceEquals(filter, _filterShown) || !names.SequenceEqual(_groupNames, StringComparer.Ordinal))
            {
                _groupNames = names;
                _filterShown = filter;
                _groupFilterHost.Child = GroupFilter(names, filter);
            }
            _groupRow.Visibility = names.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _rename.IsEnabled = filter.IsGroup;
        }

        private ComboBox GroupFilter(List<string> names, ProfileFilter selected)
        {
            var items = new List<KeyValuePair<ProfileFilter, string>>
            {
                new KeyValuePair<ProfileFilter, string>(ProfileFilter.All, Texts.AllProfiles),
            };
            items.AddRange(names.Select(name => new KeyValuePair<ProfileFilter, string>(ProfileFilter.Of(name), name)));
            items.Add(new KeyValuePair<ProfileFilter, string>(ProfileFilter.Ungrouped, Texts.NoGroup));
            ComboBox combo = Kit.Combo(items, selected, ShowGroup, 0);
            Kit.LabelFor(combo, _groupLabel);
            return combo;
        }

        private void ChangeFilter(ProfileFilter filter)
        {
            Settings.ProfilesShown = filter;
            _settings.SaveOnly();
        }

        private void ShowGroup(ProfileFilter filter)
        {
            ChangeFilter(filter);
            _filterShown = filter;
            ShowListed();
        }

        // Applied once chosen, not at each arrow key in the closed list: each step would take the profile out of a
        // filtered list.
        private ComboBox GroupField(Profile profile)
        {
            string current = ProfileGroups.Clean(profile.Group);
            var items = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(string.Empty, Texts.NoGroup) };
            items.AddRange(ProfileGroups.Names(Settings.Profiles).Select(name => new KeyValuePair<string, string>(name, name)));
            items.Add(new KeyValuePair<string, string>(NewGroupKey, Texts.NewGroupChoice));
            ComboBox combo = Kit.Combo(items, current, null, GroupFieldWidth);
            combo.HorizontalAlignment = HorizontalAlignment.Left;
            bool pending = false;

            void Restore()
            {
                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == current);
            }

            void Apply(bool chosen)
            {
                string key = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
                if (pending || key == null || string.Equals(key, current, StringComparison.Ordinal))
                    return;
                if (key == NewGroupKey && !chosen)
                {
                    Restore();
                    return;
                }
                pending = true;
                // After the list has closed and the click is over, since the editor is built again.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    string group = key == NewGroupKey ? AskGroupName(Texts.NewGroup, string.Empty, null) : key;
                    pending = false;
                    if (group == null)
                        Restore();
                    else
                        MoveToGroup(profile, group);
                }));
            }

            combo.DropDownClosed += (s, e) => Apply(true);
            combo.PreviewKeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter || combo.IsDropDownOpen)
                    return;
                Apply(true);
                e.Handled = true;
            };
            combo.LostKeyboardFocus += (s, e) =>
            {
                if (!combo.IsDropDownOpen && !combo.IsKeyboardFocusWithin)
                    Apply(false);
            };
            return combo;
        }

        // A profile that leaves the listed group gives way to the next one, so that a series is sorted without a click
        // in between.
        private void MoveToGroup(Profile profile, string group)
        {
            string name = ProfileGroups.Clean(group);
            if (Settings.FindProfile(profile.Id) == null || string.Equals(ProfileGroups.Clean(profile.Group), name, StringComparison.Ordinal))
                return;
            Guid next = NeighbourShown(profile);
            profile.Group = name;
            _settings.Commit();
            RefreshGroups();
            // Leaving the field by a click on another profile has already chosen the profile to show.
            if (profile.Id == _selectedId && !Filter.Shows(profile))
                _selectedId = next;
            SelectListed();
            RebuildList();
            ShowEditor();
            ScrollToSelected();
        }

        private void RenameGroup()
        {
            if (!Filter.IsGroup)
                return;
            string from = Filter.Group;
            string to = AskGroupName(Texts.RenameGroup, from, from);
            if (to == null || string.Equals(to, from, StringComparison.Ordinal) || !ProfileGroups.Rename(Settings.Profiles, from, to))
                return;
            ChangeFilter(ProfileFilter.Of(to));
            _settings.Commit();
            RebuildList();
            ShowEditor();
        }

        private string AskGroupName(string title, string name, string renamed)
        {
            var dialog = new GroupNameDialog(title, name, typed => ProfileGroups.IsTaken(Settings.Profiles, typed, renamed));
            return dialog.Run(_owner()) ? dialog.GroupName : null;
        }
    }
}
