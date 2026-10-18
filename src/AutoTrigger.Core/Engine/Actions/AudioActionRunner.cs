using System;
using System.Collections.Generic;
using System.Linq;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Core.Engine
{
    public sealed class AudioActionRunner
    {
        private readonly IAudioHardware _hardware;

        public AudioActionRunner(IAudioHardware hardware)
        {
            _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        }

        public static bool HasChanges(AudioAction a) =>
            !string.IsNullOrWhiteSpace(a.PlaybackDevice) || !string.IsNullOrWhiteSpace(a.RecordDevice);

        public ActionOutcome Run(AudioAction action)
        {
            if (!HasChanges(action))
                return ActionOutcome.Skip("nothing to change");

            var done = new List<string>();
            var failures = new List<string>();
            One(true, action.PlaybackDevice, action.PlaybackName, done, failures);
            One(false, action.RecordDevice, action.RecordName, done, failures);

            if (failures.Count > 0)
                return ActionOutcome.Failure(string.Join("; ", failures.Concat(done)));
            return ActionOutcome.Success(string.Join("; ", done));
        }

        private void One(bool playback, string stored, string storedName, List<string> done, List<string> failures)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return;
            string kind = playback ? "playback" : "recording";
            try
            {
                AudioDevice device = _hardware.GetDevices(playback).FirstOrDefault(d => d.Matches(stored));
                if (device == null)
                {
                    string name = string.IsNullOrWhiteSpace(storedName) ? stored : storedName;
                    failures.Add($"{kind} device {name} is not connected");
                    return;
                }
                if (string.Equals(_hardware.GetDefaultId(playback), device.Id, StringComparison.OrdinalIgnoreCase))
                {
                    done.Add($"{device.Name} already the default {kind} device");
                    return;
                }
                _hardware.SetDefault(device);
                done.Add($"{device.Name} is now the default {kind} device");
            }
            catch (Exception ex)
            {
                failures.Add($"{kind} device: {ex.Message}");
            }
        }
    }
}
