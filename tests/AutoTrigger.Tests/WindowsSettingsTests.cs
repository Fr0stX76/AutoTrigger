using System;
using System.Collections.Generic;
using System.Linq;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Json;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Tests
{
    public static partial class Runner
    {
        private static void WindowsSettingsCombined()
        {
            var audio = new FakeAudioHardware();
            var speakers = new AudioDevice { Id = "{0.0.0.00000000}.{11111111-1111-1111-1111-111111111111}", Name = "Speakers", Playback = true };
            var tv = new AudioDevice { Id = "{0.0.0.00000000}.{22222222-2222-2222-2222-222222222222}", Name = "TV", Playback = true };
            audio.Devices.AddRange(new[] { speakers, tv });
            audio.DefaultPlayback = speakers.Id;
            var balanced = new PowerPlan { Id = Guid.NewGuid(), Name = "Balanced" };
            var high = new PowerPlan { Id = Guid.NewGuid(), Name = "High performance" };
            var plans = new FakePowerPlans { Active = balanced.Id };
            plans.Plans.AddRange(new[] { balanced, high });
            var saver = new FakeScreenSaver();
            using var exec = new ActionExecutor(new FakeProcessSystem(new FakeScheduler()), null, audio, plans, null, saver);

            var all = new WindowsSettingsAction
            {
                PlaybackDevice = "22222222-2222-2222-2222-222222222222",
                PlaybackName = "TV",
                PlanId = high.Id,
                PlanName = high.Name,
                ScreenSaverOn = false,
            };
            Check(all.HasChanges && all.IsHardwareChange, "three settings: a hardware change");
            ActionOutcome r = exec.ExecuteAsync(all).Result;
            Eq(ActionResult.Succeeded, r.Result, "all applied: result");
            Eq("TV is now the default output device; power plan High performance now active; screen saver now off", r.Message, "all applied: message in order");
            Check(audio.DefaultPlayback == tv.Id && plans.Active == high.Id && !saver.On, "all applied");

            audio.DefaultPlayback = speakers.Id;
            plans.Active = balanced.Id;
            saver.On = true;
            var missing = new WindowsSettingsAction
            {
                PlaybackDevice = "44444444-4444-4444-4444-444444444444",
                PlaybackName = "Headset",
                PlanId = high.Id,
                PlanName = high.Name,
                ScreenSaverOn = false,
            };
            r = exec.ExecuteAsync(missing).Result;
            Eq(ActionResult.Failed, r.Result, "a missing device: fails");
            Check(r.Message.StartsWith("output device Headset is not connected; ", StringComparison.Ordinal), "the failure comes first: " + r.Message);
            Check(plans.Active == high.Id && !saver.On, "the other settings are still applied");
            Eq(speakers.Id, audio.DefaultPlayback, "default playback unchanged");

            using var partial = new ActionExecutor(new FakeProcessSystem(new FakeScheduler()), null, null, plans, null, null);
            plans.Active = balanced.Id;
            r = partial.ExecuteAsync(new WindowsSettingsAction { PlanId = high.Id, PlanName = high.Name, ScreenSaverOn = true }).Result;
            Eq(ActionResult.Succeeded, r.Result, "screen saver not available here: the plan still counts");
            Eq("power plan High performance now active; the screen saver setting is not available here", r.Message, "skipped part named");

            Eq(ActionResult.Skipped, exec.ExecuteAsync(new WindowsSettingsAction()).Result.Result, "nothing set: skipped");
            Check(!new WindowsSettingsAction().HasChanges, "empty action has no changes");
        }

        private static void WindowsSettingsLegacy()
        {
            Guid profileId = Guid.NewGuid();
            Guid planId = Guid.NewGuid();
            string json = "{\"version\":1,\"profiles\":[{\"id\":\"" + profileId.ToString("D") + "\",\"name\":\"Old\","
                + "\"onStart\":["
                + "{\"type\":\"audio\",\"delayMs\":500,\"playbackDevice\":\"g\",\"recordDevice\":\"m\",\"playbackName\":\"TV\",\"recordName\":\"Mic\"},"
                + "{\"type\":\"powerPlan\",\"delayMs\":1000,\"planId\":\"" + planId.ToString("D") + "\",\"planName\":\"Gaming\"},"
                + "{\"type\":\"screenSaver\",\"delayMs\":0,\"on\":false}"
                + "],\"onClose\":[{\"type\":\"screenSaver\",\"delayMs\":0,\"on\":true}],\"onFocus\":[],\"onBlur\":[]}]}";
            var warnings = new List<string>();
            AppSettings s = SettingsJson.Read(JsonValue.Parse(json), warnings);
            Eq(0, warnings.Count, "older types read without warnings");
            List<ProfileAction> start = s.Profiles[0].OnStart;
            Check(start.All(a => a is WindowsSettingsAction), "older types become Windows settings");

            var audio = (WindowsSettingsAction)start[0];
            Check(audio.PlaybackDevice == "g" && audio.RecordDevice == "m" && audio.PlaybackName == "TV" && audio.RecordName == "Mic"
                && audio.DelayMs == 500 && !audio.ChangesPowerPlan && audio.ScreenSaverOn == null, "audio: devices and delay only");
            var plan = (WindowsSettingsAction)start[1];
            Check(plan.PlanId == planId && plan.PlanName == "Gaming" && plan.DelayMs == 1000 && !plan.ChangesAudio && plan.ScreenSaverOn == null, "power plan: plan and delay only");
            var off = (WindowsSettingsAction)start[2];
            Check(off.ScreenSaverOn == false && !off.ChangesAudio && !off.ChangesPowerPlan, "screen saver off");
            Eq(true, ((WindowsSettingsAction)s.Profiles[0].OnClose[0]).ScreenSaverOn, "screen saver on");

            JsonValue written = SettingsJson.Write(s);
            string text = written.ToJson(true);
            Check(!text.Contains("\"type\": \"audio\"") && !text.Contains("\"type\": \"powerPlan\"") && !text.Contains("\"type\": \"screenSaver\""), "written with the new type only");
            JsonValue first = SettingsJson.WriteAction(audio);
            Eq("windowsSettings", first["type"].AsString(), "new type name");
            Eq("unchanged", first["screenSaver"].AsString(), "screen saver left unchanged");
            Check(first["planId"].Kind == JsonKind.Null, "no plan: null");

            AppSettings back = SettingsJson.Read(written, new List<string>());
            Eq(text, SettingsJson.Write(back).ToJson(true), "write(read(converted)) is stable");
        }
    }
}
