using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;
using UiButton = Wpf.Ui.Controls.Button;
using UiControlAppearance = Wpf.Ui.Controls.ControlAppearance;

namespace AutoTrigger.Ui
{
    internal sealed partial class ActionDialog
    {
        private readonly IAudioHardware _audio;
        private readonly IPowerPlans _powerPlans;

        private StackPanel _windowsPanel;
        private AudioDevice _playback;
        private AudioDevice _recording;
        private string _playbackStored = string.Empty;
        private string _recordingStored = string.Empty;
        private string _playbackName = string.Empty;
        private string _recordingName = string.Empty;
        private List<PowerPlan> _plans = new List<PowerPlan>();
        private Guid _planId;
        private string _planName = string.Empty;
        private bool? _screenSaverOn;
        private readonly List<UiButton> _screenSaverButtons = new List<UiButton>();

        private void BuildWindows(WindowsSettingsAction windows)
        {
            _windowsPanel = new StackPanel();
            _playbackStored = windows?.PlaybackDevice ?? string.Empty;
            _recordingStored = windows?.RecordDevice ?? string.Empty;
            _playbackName = windows?.PlaybackName ?? string.Empty;
            _recordingName = windows?.RecordName ?? string.Empty;
            _planId = windows?.PlanId ?? Guid.Empty;
            _planName = windows?.PlanName ?? string.Empty;
            _screenSaverOn = windows?.ScreenSaverOn;

            _windowsPanel.Children.Add(Kit.Field(Texts.AudioOutputDevice, DeviceCombo(true)));
            _windowsPanel.Children.Add(Kit.Field(Texts.AudioInputDevice, DeviceCombo(false)));
            _windowsPanel.Children.Add(Kit.Separator(new Thickness(0, 18, 0, 0)));
            _windowsPanel.Children.Add(Kit.Field(Texts.PowerPlan, PlanCombo()));
            _windowsPanel.Children.Add(Kit.Field(Texts.ScreenSaver, ScreenSaverRow()));
        }

        private ComboBox DeviceCombo(bool playback)
        {
            List<AudioDevice> devices = new List<AudioDevice>();
            string defaultId = null;
            try
            {
                if (_audio != null)
                {
                    devices = _audio.GetDevices(playback).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                    defaultId = _audio.GetDefaultId(playback);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Audio devices cannot be listed", ex);
            }

            string stored = playback ? _playbackStored : _recordingStored;
            string storedName = playback ? _playbackName : _recordingName;
            var choices = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(string.Empty, Texts.Unchanged) };
            string selected = string.Empty;
            foreach (AudioDevice device in devices)
            {
                bool isDefault = string.Equals(device.Id, defaultId, StringComparison.OrdinalIgnoreCase);
                string label = isDefault ? device.Name + " · " + Texts.DefaultShort : device.Name;
                choices.Add(new KeyValuePair<string, string>(device.Id, label));
                if (device.Matches(stored))
                {
                    selected = device.Id;
                    if (playback)
                        _playback = device;
                    else
                        _recording = device;
                }
            }
            if (!string.IsNullOrWhiteSpace(stored) && selected.Length == 0)
            {
                string name = string.IsNullOrWhiteSpace(storedName) ? stored : storedName;
                choices.Add(new KeyValuePair<string, string>(stored, name + " (" + Texts.NotConnectedShort + ")"));
                selected = stored;
            }

            ComboBox combo = Kit.Combo(choices, selected, id =>
            {
                AudioDevice device = devices.FirstOrDefault(d => d.Id == id);
                if (playback)
                {
                    _playback = device;
                    _playbackStored = device != null ? GuidText(device) : id;
                    _playbackName = device?.Name ?? (id.Length == 0 ? string.Empty : storedName);
                }
                else
                {
                    _recording = device;
                    _recordingStored = device != null ? GuidText(device) : id;
                    _recordingName = device?.Name ?? (id.Length == 0 ? string.Empty : storedName);
                }
            });
            combo.HorizontalAlignment = HorizontalAlignment.Stretch;
            return combo;
        }

        private static string GuidText(AudioDevice device) =>
            device.Guid != Guid.Empty ? device.Guid.ToString("D") : device.Id;

        private ComboBox PlanCombo()
        {
            Guid active = Guid.Empty;
            try
            {
                if (_powerPlans != null)
                {
                    _plans = _powerPlans.GetPlans().OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                    active = _powerPlans.GetActive();
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Power plans cannot be listed", ex);
            }

            var choices = new List<KeyValuePair<Guid, string>> { new KeyValuePair<Guid, string>(Guid.Empty, Texts.Unchanged) };
            choices.AddRange(_plans.Select(p =>
                new KeyValuePair<Guid, string>(p.Id, p.Name + (p.Id == active ? " · " + Texts.ActiveShort : string.Empty))));
            if (_planId != Guid.Empty && _plans.All(p => p.Id != _planId))
            {
                string name = string.IsNullOrWhiteSpace(_planName) ? _planId.ToString("D") : _planName;
                choices.Add(new KeyValuePair<Guid, string>(_planId, name + " (" + Texts.NotFoundShort + ")"));
            }

            string storedName = _planName;
            ComboBox combo = Kit.WithScrollBar(Kit.Combo(choices, _planId, id =>
            {
                _planId = id;
                PowerPlan plan = _plans.FirstOrDefault(p => p.Id == id);
                _planName = plan != null ? plan.Name : id == Guid.Empty ? string.Empty : storedName;
            }));
            combo.HorizontalAlignment = HorizontalAlignment.Stretch;
            return combo;
        }

        private StackPanel ScreenSaverRow()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (KeyValuePair<bool?, string> choice in new[]
            {
                new KeyValuePair<bool?, string>(null, Texts.Unchanged),
                new KeyValuePair<bool?, string>(true, Texts.On),
                new KeyValuePair<bool?, string>(false, Texts.Off),
            })
            {
                bool? value = choice.Key;
                var button = new UiButton { Content = choice.Value, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), Tag = value };
                button.Click += (s, e) =>
                {
                    _screenSaverOn = value;
                    UpdateScreenSaverButtons();
                };
                _screenSaverButtons.Add(button);
                row.Children.Add(button);
            }
            UpdateScreenSaverButtons();
            return row;
        }

        private void UpdateScreenSaverButtons()
        {
            foreach (UiButton button in _screenSaverButtons)
                button.Appearance = (bool?)button.Tag == _screenSaverOn ? UiControlAppearance.Primary : UiControlAppearance.Secondary;
        }

        private bool TrySaveWindows(int delay)
        {
            var action = new WindowsSettingsAction
            {
                PlaybackDevice = _playback != null ? GuidText(_playback) : _playbackStored,
                RecordDevice = _recording != null ? GuidText(_recording) : _recordingStored,
                PlaybackName = _playback?.Name ?? _playbackName,
                RecordName = _recording?.Name ?? _recordingName,
                PlanId = _planId,
                PlanName = _planId == Guid.Empty ? string.Empty : _planName ?? string.Empty,
                ScreenSaverOn = _screenSaverOn,
                DelayMs = delay,
            };
            if (!action.HasChanges)
            {
                ShowError(_error, Texts.ErrorNothingToChange);
                return false;
            }
            Result = action;
            return true;
        }
    }
}
