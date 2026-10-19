using System;
using System.Collections.Generic;
using System.Linq;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Core.Engine
{
    public sealed class WindowsSettingsRunner
    {
        private readonly IAudioHardware _audio;
        private readonly IPowerPlans _powerPlans;
        private readonly IScreenSaver _screenSaver;

        public WindowsSettingsRunner(IAudioHardware audio, IPowerPlans powerPlans, IScreenSaver screenSaver)
        {
            _audio = audio;
            _powerPlans = powerPlans;
            _screenSaver = screenSaver;
        }

        public ActionOutcome Run(WindowsSettingsAction action)
        {
            if (!action.HasChanges)
                return ActionOutcome.Skip("nothing to change");

            var result = new Steps();
            if (action.ChangesAudio)
            {
                if (_audio == null)
                {
                    result.Skipped.Add("audio devices are not available here");
                }
                else
                {
                    SetAudioDevice(true, action.PlaybackDevice, action.PlaybackName, result);
                    SetAudioDevice(false, action.RecordDevice, action.RecordName, result);
                }
            }
            if (action.ChangesPowerPlan)
                SetPowerPlan(action, result);
            if (action.ScreenSaverOn.HasValue)
                SetScreenSaver(action.ScreenSaverOn.Value, result);
            return result.Outcome();
        }

        private void SetAudioDevice(bool playback, string stored, string storedName, Steps result)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return;
            string kind = playback ? "output" : "input";
            try
            {
                AudioDevice device = _audio.GetDevices(playback).FirstOrDefault(d => d.Matches(stored));
                if (device == null)
                {
                    string name = string.IsNullOrWhiteSpace(storedName) ? stored : storedName;
                    result.Failures.Add($"{kind} device {name} is not connected");
                    return;
                }
                if (string.Equals(_audio.GetDefaultId(playback), device.Id, StringComparison.OrdinalIgnoreCase))
                {
                    result.Done.Add($"{device.Name} already the default {kind} device");
                    return;
                }
                _audio.SetDefault(device);
                result.Done.Add($"{device.Name} is now the default {kind} device");
            }
            catch (Exception ex)
            {
                result.Failures.Add($"{kind} device: {ex.Message}");
            }
        }

        private void SetPowerPlan(WindowsSettingsAction action, Steps result)
        {
            if (_powerPlans == null)
            {
                result.Skipped.Add("power plans are not available here");
                return;
            }
            string name = string.IsNullOrWhiteSpace(action.PlanName) ? action.PlanId.ToString("D") : action.PlanName;
            try
            {
                PowerPlan plan = _powerPlans.GetPlans().Find(p => p.Id == action.PlanId);
                if (plan == null)
                {
                    result.Failures.Add($"power plan {name} not found");
                    return;
                }
                if (_powerPlans.GetActive() == plan.Id)
                {
                    result.Done.Add($"power plan {plan.Name} already active");
                    return;
                }
                _powerPlans.SetActive(plan.Id);
                result.Done.Add($"power plan {plan.Name} now active");
            }
            catch (Exception ex)
            {
                result.Failures.Add($"power plan {name}: {ex.Message}");
            }
        }

        private void SetScreenSaver(bool on, Steps result)
        {
            if (_screenSaver == null)
            {
                result.Skipped.Add("the screen saver setting is not available here");
                return;
            }
            string state = on ? "on" : "off";
            try
            {
                if (_screenSaver.IsOn() == on)
                {
                    result.Done.Add("screen saver already " + state);
                    return;
                }
                _screenSaver.Set(on);
                if (_screenSaver.IsOn() != on)
                {
                    result.Failures.Add("screen saver: Windows kept the previous setting (a group policy may manage it)");
                    return;
                }
                result.Done.Add("screen saver now " + state);
            }
            catch (Exception ex)
            {
                result.Failures.Add($"screen saver: {ex.Message}");
            }
        }

        private sealed class Steps
        {
            public readonly List<string> Done = new List<string>();
            public readonly List<string> Failures = new List<string>();
            public readonly List<string> Skipped = new List<string>();

            public ActionOutcome Outcome()
            {
                if (Failures.Count > 0)
                    return ActionOutcome.Failure(string.Join("; ", Failures.Concat(Done).Concat(Skipped)));
                if (Done.Count == 0)
                    return ActionOutcome.Skip(string.Join("; ", Skipped));
                return ActionOutcome.Success(string.Join("; ", Done.Concat(Skipped)));
            }
        }
    }
}
