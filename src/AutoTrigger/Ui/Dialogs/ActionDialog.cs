using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Model;
using Microsoft.Win32;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace AutoTrigger.Ui
{
    internal sealed partial class ActionDialog : FormDialog
    {
        private const string RunType = "run";
        private const string CloseType = "close";
        private const string ReferenceType = "reference";
        private const string DisplayType = "display";
        private const string WindowsType = "windows";
        private const string HotkeyType = "hotkey";

        private readonly AppSettings _settings;
        private readonly Profile _profile;
        private readonly EventKind _kind;
        private readonly IProcessSystem _processes;
        private readonly ProfileAction _original;
        private readonly Border _panelHost = new Border();
        private readonly TextBlock _error = ErrorLine();
        private string _type;

        private UiTextBox _runPath;
        private UiTextBox _runArguments;
        private ToggleSwitch _hold;
        private Grid _holdCapRow;
        private int _holdCapValue;

        private UiTextBox _closeTarget;
        private ToggleSwitch _force;
        private Grid _forceAfterRow;
        private int _forceAfterValue;

        private ComboBox _reference;
        private Guid _referenceId;

        public ActionDialog(AppSettings settings, Profile profile, EventKind kind, ProfileAction original, int position,
            IProcessSystem processes, DisplayService displays, IAudioHardware audio, IPowerPlans powerPlans)
            : base(Context(profile, kind, original, position), original == null ? Texts.AddAction : Texts.EditAction,
                original == null ? Texts.Add : Texts.Save, 640)
        {
            _settings = settings;
            _profile = profile;
            _kind = kind;
            _processes = processes;
            _original = original;
            _displays = displays;
            _audio = audio;
            _powerPlans = powerPlans;

            _type = original is RunProgramAction ? RunType
                : original is CloseProgramAction ? CloseType
                : original is IncludeProfileAction ? ReferenceType
                : original is WindowsSettingsAction ? WindowsType
                : original is PressHotkeyAction ? HotkeyType
                : DisplayType;
            var types = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(DisplayType, Texts.TypeDisplay),
                new KeyValuePair<string, string>(WindowsType, Texts.TypeWindowsSettings),
                new KeyValuePair<string, string>(RunType, Texts.TypeRunProgram),
                new KeyValuePair<string, string>(CloseType, Texts.TypeCloseProgram),
                new KeyValuePair<string, string>(HotkeyType, Texts.TypeHotkey),
                new KeyValuePair<string, string>(ReferenceType, Texts.TypeReferenceProfile),
            };
            ComboBox typeBox = Kit.Combo(types, _type, t =>
            {
                _type = t;
                ShowPanel();
            });
            typeBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            Body.Children.Add(Kit.Field(Texts.Type, typeBox));
            Body.Children.Add(Kit.Separator(new Thickness(0, 18, 0, 0)));
            Body.Children.Add(_panelHost);
            Body.Children.Add(_error);

            BuildRun(original as RunProgramAction ?? new RunProgramAction());
            BuildClose(original as CloseProgramAction ?? new CloseProgramAction());
            BuildReference(original as IncludeProfileAction);
            ShowPanel();
        }

        public ProfileAction Result { get; private set; }

        private static string Context(Profile profile, EventKind kind, ProfileAction original, int position)
        {
            string text = (string.IsNullOrWhiteSpace(profile.Name) ? Texts.Unnamed : profile.Name) + " · " + ActionText.EventName(kind);
            return original == null ? text : text + " · " + Texts.Format(Texts.ActionNumber, position);
        }

        private StackPanel _runPanel;
        private StackPanel _closePanel;
        private StackPanel _referencePanel;

        private void ShowPanel()
        {
            ShowError(_error, null);
            switch (_type)
            {
                case RunType: _panelHost.Child = _runPanel; break;
                case CloseType: _panelHost.Child = _closePanel; break;
                case ReferenceType: _panelHost.Child = _referencePanel; break;
                case WindowsType:
                    if (_windowsPanel == null)
                        BuildWindows(_original as WindowsSettingsAction);
                    _panelHost.Child = _windowsPanel;
                    break;
                case HotkeyType:
                    if (_hotkeyPanel == null)
                        BuildHotkey(_original as PressHotkeyAction);
                    _panelHost.Child = _hotkeyPanel;
                    break;
                default:
                    if (_displayPanel == null)
                        BuildDisplay(_original as DisplayAction);
                    _panelHost.Child = _displayPanel;
                    break;
            }
        }

        private void BuildRun(RunProgramAction run)
        {
            _runPanel = new StackPanel();
            _runPath = Kit.TextField(run.Path, null, Texts.ChooseProgramFile);
            _runPanel.Children.Add(Kit.Field(Texts.Program, WithBrowse(_runPath, false)));
            _runArguments = Kit.TextField(run.Arguments, null, Texts.None);
            _runPanel.Children.Add(Kit.Field(Texts.Arguments, _runArguments));
            _runPanel.Children.Add(Kit.Separator(new Thickness(0, 18, 0, 0)));

            _holdCapValue = run.HoldCapSec;
            _hold = Kit.Switch(run.HoldNext, on => UpdateHold(), Texts.HoldNext);
            _runPanel.Children.Add(SwitchRow(Texts.HoldNext, _hold));
            TextBox holdCap = Kit.NumberBox(run.HoldCapSec, 1, 3600, v => _holdCapValue = v, 72);
            _holdCapRow = NumberRow(Res.Text(Texts.StopHoldingAfter), holdCap, Texts.Seconds);
            _runPanel.Children.Add(_holdCapRow);
            UpdateHold();
        }

        private void UpdateHold()
        {
            _holdCapRow.Visibility = _hold.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BuildClose(CloseProgramAction close)
        {
            _closePanel = new StackPanel();
            _closeTarget = Kit.TextField(close.Target, null, Texts.CloseTargetHint);
            var program = new StackPanel();
            program.Children.Add(WithBrowse(_closeTarget, true));
            var running = Kit.Button(Texts.ChooseRunning, Kit.IconList);
            running.Margin = new Thickness(0, 8, 0, 0);
            running.HorizontalAlignment = HorizontalAlignment.Left;
            running.Click += (s, e) =>
            {
                var picker = new ProcessPickerDialog(_processes);
                if (picker.Run(this))
                    _closeTarget.Text = picker.SelectedPath;
            };
            program.Children.Add(running);
            _closePanel.Children.Add(Kit.Field(Texts.Program, program));
            _closePanel.Children.Add(Kit.Separator(new Thickness(0, 18, 0, 0)));

            _forceAfterValue = close.ForceAfterSec;
            _force = Kit.Switch(close.Force, on => UpdateForce(), Texts.ForceClose);
            _closePanel.Children.Add(SwitchRow(Texts.ForceClose, _force));
            TextBox forceAfter = Kit.NumberBox(close.ForceAfterSec, 0, 600, v => _forceAfterValue = v, 72);
            _forceAfterRow = NumberRow(Res.Text(Texts.ForceAfter), forceAfter, Texts.Seconds);
            _closePanel.Children.Add(_forceAfterRow);
            UpdateForce();
        }

        private void UpdateForce()
        {
            _forceAfterRow.Visibility = _force.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BuildReference(IncludeProfileAction include)
        {
            _referencePanel = new StackPanel();
            List<Profile> allowed = SettingsValidator.AllowedReferences(_settings, _profile, _kind);
            _referenceId = include?.ProfileId ?? Guid.Empty;
            var items = allowed
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => new KeyValuePair<Guid, string>(p.Id, string.IsNullOrWhiteSpace(p.Name) ? Texts.Unnamed : p.Name))
                .ToList();
            if (_referenceId != Guid.Empty && items.All(i => i.Key != _referenceId))
            {
                Profile current = _settings.FindProfile(_referenceId);
                string label = (current?.Name ?? Texts.MissingProfile) + " (" + Texts.NotAllowed + ")";
                items.Insert(0, new KeyValuePair<Guid, string>(_referenceId, label));
            }
            if (_referenceId == Guid.Empty && items.Count > 0)
                _referenceId = items[0].Key;
            _reference = Kit.WithScrollBar(Kit.Combo(items, _referenceId, id => _referenceId = id));
            _reference.HorizontalAlignment = HorizontalAlignment.Stretch;
            _referencePanel.Children.Add(Kit.Field(Texts.Profile, _reference));
        }

        protected override bool TrySave()
        {
            int delay = _original?.DelayMs ?? 0;
            switch (_type)
            {
                case CloseType:
                    MoveFocus();
                    string target = _closeTarget.Text.Trim();
                    if (target.Length == 0)
                    {
                        ShowError(_error, Texts.ErrorNoProgram);
                        return false;
                    }
                    Result = new CloseProgramAction
                    {
                        Target = target,
                        Force = _force.IsChecked == true,
                        ForceAfterSec = _forceAfterValue,
                        DelayMs = delay,
                    };
                    return true;

                case WindowsType:
                    return TrySaveWindows(delay);

                case HotkeyType:
                    return TrySaveHotkey(delay);

                case ReferenceType:
                    List<Profile> allowed = SettingsValidator.AllowedReferences(_settings, _profile, _kind);
                    if (allowed.All(p => p.Id != _referenceId))
                    {
                        ShowError(_error, allowed.Count == 0 ? Texts.ErrorNoReference : Texts.ErrorReferenceLoop);
                        return false;
                    }
                    Result = new IncludeProfileAction { ProfileId = _referenceId, DelayMs = delay };
                    return true;

                case RunType:
                    MoveFocus();
                    string path = _runPath.Text.Trim();
                    if (path.Length == 0)
                    {
                        ShowError(_error, Texts.ErrorNoProgram);
                        return false;
                    }
                    Result = new RunProgramAction
                    {
                        Path = path,
                        Arguments = _runArguments.Text.Trim(),
                        HoldNext = _hold.IsChecked == true,
                        HoldCapSec = _holdCapValue,
                        DelayMs = delay,
                    };
                    return true;

                default:
                    return TrySaveDisplay(delay);
            }
        }

        private static void MoveFocus()
        {
            if (System.Windows.Input.Keyboard.FocusedElement is TextBox box)
                box.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
        }

        private static Grid WithBrowse(TextBox box, bool exeOnly)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(box);
            var browse = Kit.Button(Texts.Browse);
            browse.Margin = new Thickness(8, 0, 0, 0);
            browse.Click += (s, e) =>
            {
                string chosen = BrowseForFile(box.Text, exeOnly);
                if (chosen != null)
                    box.Text = chosen;
            };
            Grid.SetColumn(browse, 1);
            grid.Children.Add(browse);
            return grid;
        }

        internal static string BrowseForFile(string current, bool exeOnly)
        {
            var dialog = new OpenFileDialog
            {
                CheckFileExists = true,
                Filter = exeOnly ? Texts.FilterExe : Texts.FilterPrograms,
            };
            try
            {
                string folder = ActionExecutor.ProgramFolder(ActionExecutor.ExpandPath(current ?? string.Empty));
                if (folder != null)
                    dialog.InitialDirectory = folder;
            }
            catch (ArgumentException)
            {
            }
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        private static Grid SwitchRow(string text, ToggleSwitch toggle)
        {
            var grid = new Grid { Margin = new Thickness(0, 18, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock label = Kit.Wrapped(text);
            label.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(label);
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);
            return grid;
        }

        private static Grid NumberRow(TextBlock label, TextBox box, string unit)
        {
            var grid = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(label);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            TextBlock unitText = Kit.Caption(unit);
            unitText.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(unitText, 2);
            grid.Children.Add(unitText);
            return grid;
        }
    }
}
