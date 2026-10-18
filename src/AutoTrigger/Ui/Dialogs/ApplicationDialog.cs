using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Model;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace AutoTrigger.Ui
{
    internal sealed class ApplicationDialog : FormDialog
    {
        private readonly AppSettings _settings;
        private readonly AppEntry _existing;
        private readonly AppIcons _icons;
        private readonly UiTextBox _path;
        private readonly UiTextBox _name;
        private readonly AppIconBox _icon = new AppIconBox(44, 8);
        private readonly RadioButton _byPath;
        private readonly RadioButton _byName;
        private readonly TextBlock _error = ErrorLine();
        private Guid _profileId;
        private string _autoName = string.Empty;
        private string _iconPath = string.Empty;

        public ApplicationDialog(AppSettings settings, AppEntry existing, IProcessSystem processes, AppIcons icons)
            : base(null,
                existing == null ? Texts.AddApplication : Texts.EditApplication,
                existing == null ? Texts.Add : Texts.Save,
                600)
        {
            _settings = settings;
            _existing = existing;
            _icons = icons;
            _profileId = existing?.ProfileId ?? Guid.Empty;
            if (settings.FindProfile(_profileId) == null)
                _profileId = Guid.Empty;

            _path = Kit.TextField(existing?.Path ?? string.Empty, null, Texts.ChooseExe);
            _path.LostKeyboardFocus += (s, e) => ProgramChanged();
            var pathRow = new Grid();
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pathRow.Children.Add(_path);
            var browse = Kit.Button(Texts.Browse);
            browse.Margin = new Thickness(8, 0, 0, 0);
            browse.Click += (s, e) =>
            {
                string chosen = ActionDialog.BrowseForFile(_path.Text, true);
                if (chosen == null)
                    return;
                _path.Text = chosen;
                ProgramChanged();
            };
            Grid.SetColumn(browse, 1);
            pathRow.Children.Add(browse);
            var program = new StackPanel();
            program.Children.Add(pathRow);
            var running = Kit.Button(Texts.ChooseRunning, Kit.IconList);
            running.HorizontalAlignment = HorizontalAlignment.Left;
            running.Margin = new Thickness(0, 8, 0, 0);
            running.Click += (s, e) =>
            {
                var picker = new ProcessPickerDialog(processes);
                if (!picker.Run(this))
                    return;
                _path.Text = picker.SelectedPath;
                ProgramChanged();
            };
            program.Children.Add(running);
            Body.Children.Add(Kit.Field(Texts.Program, program));

            var nameRow = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var iconField = Kit.Field(Texts.Icon, _icon, 0);
            nameRow.Children.Add(iconField);
            _name = Kit.TextField(existing?.Name ?? string.Empty, null, Texts.NameFromProgram);
            var nameField = Kit.Field(Texts.DisplayName, _name, 0);
            nameField.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(nameField, 2);
            nameRow.Children.Add(nameField);
            Body.Children.Add(nameRow);

            bool byName = existing != null && existing.MatchByFileName;
            _byPath = new RadioButton { Content = Texts.FullPath, GroupName = "recognize", IsChecked = !byName };
            _byName = new RadioButton
            {
                Content = Texts.FileNameOnly,
                GroupName = "recognize",
                IsChecked = byName,
                Margin = new Thickness(16, 0, 0, 0),
            };
            var recognize = new StackPanel { Orientation = Orientation.Horizontal };
            recognize.Children.Add(_byPath);
            recognize.Children.Add(_byName);
            Body.Children.Add(Kit.Field(Texts.RecognizeBy, recognize));

            ComboBox profile = Kit.WithScrollBar(Kit.Combo(Kit.ProfileChoices(settings), _profileId, id => _profileId = id));
            profile.HorizontalAlignment = HorizontalAlignment.Stretch;
            Body.Children.Add(Kit.Field(Texts.Profile, profile));
            Body.Children.Add(_error);

            _icon.ShowInitials(existing?.Name);
            ShowIcon();
            Loaded += (s, e) => _path.Focus();
        }

        public AppEntry Result { get; private set; }

        private void ProgramChanged()
        {
            string path = ActionExecutor.ExpandPath(_path.Text);
            ShowIcon();
            if (path.Length == 0)
                return;
            if (_name.Text.Trim().Length == 0 || _name.Text == _autoName)
            {
                _autoName = SuggestedName(path);
                _name.Text = _autoName;
            }
        }

        private void ShowIcon()
        {
            string path = ActionExecutor.ExpandPath(_path.Text);
            if (path == _iconPath)
                return;
            _iconPath = path;
            _icon.ShowInitials(_name.Text.Length > 0 ? _name.Text : Path.GetFileNameWithoutExtension(SafeName(path)));
            if (path.Length > 0)
                _icons.Get(path, image => { if (_iconPath == path) _icon.ShowImage(image); });
        }

        private static string SuggestedName(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                    if (!string.IsNullOrWhiteSpace(info.FileDescription))
                        return info.FileDescription.Trim();
                    if (!string.IsNullOrWhiteSpace(info.ProductName))
                        return info.ProductName.Trim();
                }
            }
            catch (Exception)
            {
            }
            return Path.GetFileNameWithoutExtension(SafeName(path));
        }

        private static string SafeName(string path)
        {
            try
            {
                return Path.GetFileName(path);
            }
            catch (ArgumentException)
            {
                return path;
            }
        }

        protected override bool TrySave()
        {
            string path = ActionExecutor.ExpandPath(_path.Text);
            if (path.Length == 0 || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                ShowError(_error, Texts.ErrorChooseExe);
                return false;
            }
            bool pathChanged = _existing == null || !PathMatcher.SamePath(_existing.Path, path);
            if (pathChanged && !File.Exists(path))
            {
                ShowError(_error, Texts.ErrorFileMissing);
                return false;
            }
            string name = _name.Text.Trim();
            if (name.Length == 0)
                name = SuggestedName(path);

            AppEntry entry = _existing ?? new AppEntry();
            entry.Path = path;
            entry.Name = name;
            entry.MatchByFileName = _byName.IsChecked == true;
            entry.ProfileId = _profileId;
            Result = entry;
            return true;
        }
    }
}
