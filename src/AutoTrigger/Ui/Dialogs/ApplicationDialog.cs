using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Text;
using Microsoft.Win32;
using UiButton = Wpf.Ui.Controls.Button;
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
        private readonly TextBlock _iconError = ErrorLine();
        private readonly TextBlock _iconHint;
        private readonly UiButton _removeIcon;
        private Guid _profileId;
        private string _launch;
        private string _autoName = string.Empty;
        private string _iconPath = string.Empty;

        // Kept here until Save, so that Cancel leaves the icon as it was.
        private byte[] _pendingIcon;
        private bool _iconRemoved;

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
            browse.Margin = Spacing.Left(Spacing.Small);
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
            running.Margin = Spacing.Top(Spacing.Small);
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

            _name = Kit.TextField(existing?.Name ?? string.Empty, null, Texts.NameFromProgram);
            Body.Children.Add(Kit.Field(Texts.DisplayName, _name));

            // The buttons sit next to the icon they change, under its label.
            var iconRow = new StackPanel { Orientation = Orientation.Horizontal };
            iconRow.Children.Add(_icon);
            var importIcon = Kit.Button(Texts.ImportIcon);
            importIcon.Margin = Spacing.Left(Spacing.Medium);
            importIcon.VerticalAlignment = VerticalAlignment.Center;
            importIcon.Click += (s, e) => ImportIcon();
            iconRow.Children.Add(importIcon);
            _removeIcon = Kit.Button(Texts.RemoveIcon);
            _removeIcon.Margin = Spacing.Left(Spacing.Small);
            _removeIcon.VerticalAlignment = VerticalAlignment.Center;
            _removeIcon.Click += (s, e) => IconChanged(null, true);
            iconRow.Children.Add(_removeIcon);
            StackPanel iconField = Kit.Field(Texts.Icon, iconRow);
            _iconHint = Kit.Wrapped(Texts.IconShortcutHint, 12, Res.TextSecondary);
            _iconHint.Margin = Spacing.Top(Spacing.Tight);
            _iconHint.Visibility = Visibility.Collapsed;
            iconField.Children.Add(_iconHint);
            iconField.Children.Add(_iconError);
            Body.Children.Add(iconField);

            bool byName = existing != null && existing.MatchByFileName;
            _byPath = new RadioButton { Content = Texts.FullPath, GroupName = "recognize", IsChecked = !byName };
            _byName = new RadioButton
            {
                Content = Texts.FileNameOnly,
                GroupName = "recognize",
                IsChecked = byName,
                Margin = Spacing.Left(Spacing.Large),
            };
            var recognize = new StackPanel { Orientation = Orientation.Horizontal };
            recognize.Children.Add(_byPath);
            recognize.Children.Add(_byName);
            Body.Children.Add(Kit.Field(Texts.RecognizeBy, recognize));

            ComboBox profile = Kit.WithScrollBar(Kit.Combo(Kit.ProfileChoices(settings), _profileId, id => _profileId = id));
            profile.HorizontalAlignment = HorizontalAlignment.Stretch;
            Body.Children.Add(Kit.Field(Texts.Profile, profile));

            _launch = existing == null ? LaunchChoices.None : LaunchChoices.KeyOf(existing);
            ComboBox launch = Kit.WithScrollBar(Kit.Combo(LaunchChoices.For(settings), _launch, key => _launch = key));
            launch.HorizontalAlignment = HorizontalAlignment.Stretch;
            Body.Children.Add(Kit.Field(Texts.LaunchButton, launch));
            Body.Children.Add(_error);

            _icon.ShowInitials(existing?.Name);
            ShowIcon();
            _removeIcon.IsEnabled = UsesImportedIcon;
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

        private bool UsesImportedIcon => _pendingIcon != null || (!_iconRemoved && _icons.Custom.Existing(_existing) != null);

        private void ShowIcon(bool force = false)
        {
            string path = ActionExecutor.ExpandPath(_path.Text);
            if (path == _iconPath && !force)
                return;
            _iconPath = path;
            _icon.ShowInitials(_name.Text.Length > 0 ? _name.Text : SafeName(path));
            if (_pendingIcon != null)
                _icon.ShowImage(AppIcons.FromIco(_pendingIcon));
            else if (UsesImportedIcon)
                _icons.Get(_existing, _icon.ShowImage);
            else if (path.Length > 0)
                _icons.Get(path, image => { if (_iconPath == path && !UsesImportedIcon) _icon.ShowImage(image); });
        }

        private void ImportIcon()
        {
            string types = string.Join(";", IconImport.Extensions.Select(e => "*" + e));
            var dialog = new OpenFileDialog
            {
                CheckFileExists = true,
                Filter = ActionDialog.FilterName(Texts.FileTypeIcons) + " (" + types + ")|" + types,
            };
            if (dialog.ShowDialog(this) != true)
                return;
            byte[] ico = IconImport.FromFile(dialog.FileName);
            if (ico == null)
            {
                ShowError(_iconError, Texts.IconUnusable);
                return;
            }
            IconChanged(ico, false);
        }

        private void IconChanged(byte[] ico, bool removed)
        {
            _pendingIcon = ico;
            _iconRemoved = removed;
            ShowError(_iconError, null);
            // A shortcut already on the desktop keeps the icon it was made with.
            _iconHint.Visibility = _existing != null ? Visibility.Visible : Visibility.Collapsed;
            _removeIcon.IsEnabled = UsesImportedIcon;
            ShowIcon(true);
        }

        private bool SaveIcon(AppEntry entry)
        {
            try
            {
                if (_pendingIcon != null)
                    _icons.Custom.Save(entry.Id, _pendingIcon);
                else if (_iconRemoved)
                    _icons.Custom.Delete(entry.Id);
                else
                    return true;
                entry.CustomIcon = _pendingIcon != null;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Error("Imported icon not saved", ex);
                ShowError(_iconError, Texts.IconSaveFailed(ex.Message));
                return false;
            }
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
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
            }
            return SafeName(path);
        }

        private static string SafeName(string path)
        {
            try
            {
                return Path.GetFileNameWithoutExtension(path);
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
            if (!SaveIcon(entry))
                return false;
            entry.Path = path;
            entry.Name = name;
            entry.MatchByFileName = _byName.IsChecked == true;
            entry.ProfileId = _profileId;
            LaunchChoices.Apply(entry, _launch);
            Result = entry;
            return true;
        }
    }
}
