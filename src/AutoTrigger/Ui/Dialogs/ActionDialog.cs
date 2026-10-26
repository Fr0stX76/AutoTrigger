using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
        private UiTextBox _name;
        private StackPanel _nameField;
        private readonly Dictionary<string, string> _names = new Dictionary<string, string>();

        private UiTextBox _runPath;
        private UiTextBox _runArguments;
        private ToggleSwitch _hide;
        private Grid _hideRow;
        private DispatcherTimer _hideCheck;
        private int _hideVersion;
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
                // Each type keeps its own name, like its other fields: another type is another action.
                _names[_type] = _name.Text;
                _type = t;
                _name.Text = _names.TryGetValue(t, out string name) ? name : string.Empty;
                ShowPanel();
            });
            typeBox.HorizontalAlignment = HorizontalAlignment.Stretch;
            Body.Children.Add(Kit.Field(Texts.Type, typeBox));
            _name = Kit.TextField(original != null && original.CanBeNamed ? original.Name : string.Empty, null, Texts.Optional);
            _nameField = Kit.Field(Texts.Name, _name);
            Body.Children.Add(_nameField);
            Body.Children.Add(Kit.Separator(Spacing.Top(Spacing.Field)));
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
            string text = ActionText.ProfileName(profile.Name) + " · " + ActionText.EventName(kind);
            return original == null ? text : text + " · " + Texts.Format(Texts.ActionNumber, position);
        }

        private StackPanel _runPanel;
        private StackPanel _closePanel;
        private StackPanel _referencePanel;

        private void ShowPanel()
        {
            ShowError(_error, null);
            _nameField.Visibility = _type == RunType || _type == CloseType || _type == HotkeyType ? Visibility.Visible : Visibility.Collapsed;
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
            _runPanel.Children.Add(Kit.Field(Texts.Program, WithRunning(_runPath, false)));
            _runArguments = Kit.TextField(run.Arguments, null, Texts.None);
            _runPanel.Children.Add(Kit.Field(Texts.Arguments, _runArguments));
            _runPanel.Children.Add(Kit.Separator(Spacing.Top(Spacing.Field)));

            _hide = Kit.Switch(run.HideConsole, _ => { }, Texts.HideConsoleWindow);
            _hideRow = SwitchRow(Texts.HideConsoleWindow, _hide);
            // Until the program is read, a saved switch is the best guess: it was only saved for a console program.
            _hideRow.Visibility = run.HideConsole ? Visibility.Visible : Visibility.Collapsed;
            _runPanel.Children.Add(_hideRow);
            // The program's header is read once typing pauses, not on every key.
            _hideCheck = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _hideCheck.Tick += (s, e) => UpdateHide();
            _runPath.TextChanged += (s, e) =>
            {
                _hideCheck.Stop();
                _hideCheck.Start();
            };
            UpdateHide();

            _holdCapValue = run.HoldCapSec;
            _hold = Kit.Switch(run.HoldNext, on => UpdateHold(), Texts.HoldNext);
            _runPanel.Children.Add(SwitchRow(Texts.HoldNext, _hold));
            TextBox holdCap = Kit.NumberBox(run.HoldCapSec, 1, 3600, v => _holdCapValue = v, 72);
            _holdCapRow = NumberRow(Res.Text(Texts.StopHoldingAfter), holdCap, Texts.Seconds);
            _runPanel.Children.Add(_holdCapRow);
            UpdateHold();
        }

        // Shown only for a console program or script: hiding a window program would leave it invisible.
        // The file is read off the UI thread, as a path on an unreachable share can take seconds to answer.
        private void UpdateHide()
        {
            _hideCheck.Stop();
            string path = ActionExecutor.ExpandPath(_runPath.Text);
            int version = ++_hideVersion;
            Task.Run(() => ConsoleProgram.Is(path)).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (version == _hideVersion && t.Status == TaskStatus.RanToCompletion)
                    _hideRow.Visibility = t.Result ? Visibility.Visible : Visibility.Collapsed;
            })), TaskScheduler.Default);
        }

        private void UpdateHold()
        {
            _holdCapRow.Visibility = _hold.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BuildClose(CloseProgramAction close)
        {
            _closePanel = new StackPanel();
            _closeTarget = Kit.TextField(close.Target, null, Texts.CloseTargetHint);
            _closePanel.Children.Add(Kit.Field(Texts.Program, WithRunning(_closeTarget, true)));
            _closePanel.Children.Add(Kit.Separator(Spacing.Top(Spacing.Field)));

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
                .Select(p => new KeyValuePair<Guid, string>(p.Id, ActionText.ProfileName(p.Name)))
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
                        Name = _name.Text.Trim(),
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
                    // The switch row can trail a path typed just before saving; the program is checked again before it is hidden.
                    Result = new RunProgramAction
                    {
                        Name = _name.Text.Trim(),
                        Path = path,
                        Arguments = _runArguments.Text.Trim(),
                        HideConsole = _hideRow.Visibility == Visibility.Visible && _hide.IsChecked == true,
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

        private StackPanel WithRunning(TextBox box, bool exeOnly)
        {
            var program = new StackPanel();
            program.Children.Add(WithBrowse(box, exeOnly));
            var running = Kit.Button(Texts.ChooseRunning, Kit.IconList);
            running.Margin = Spacing.Top(Spacing.Small);
            running.HorizontalAlignment = HorizontalAlignment.Left;
            running.Click += (s, e) =>
            {
                var picker = new ProcessPickerDialog(_processes);
                if (picker.Run(this))
                    box.Text = picker.SelectedPath;
            };
            program.Children.Add(running);
            return program;
        }

        private static Grid WithBrowse(TextBox box, bool exeOnly)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(box);
            var browse = Kit.Button(Texts.Browse);
            browse.Margin = Spacing.Left(Spacing.Small);
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
            var grid = new Grid { Margin = Spacing.Top(Spacing.Field) };
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
            var grid = new Grid { Margin = Spacing.Top(Spacing.Medium) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(label);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            TextBlock unitText = Kit.Caption(unit);
            unitText.Margin = Spacing.Left(Spacing.Medium);
            Grid.SetColumn(unitText, 2);
            grid.Children.Add(unitText);
            return grid;
        }
    }
}
