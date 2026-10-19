using System;
using System.Collections.Generic;
using System.Linq;
using AutoTrigger.Core.Displays;
using AutoTrigger.Core.Engine;
using AutoTrigger.Core.Json;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Tests
{
    public sealed class FakeDisplayHardware : IDisplayHardware
    {
        public readonly List<MonitorInfo> Monitors = new List<MonitorInfo>();
        public readonly Dictionary<string, DisplayCapabilities> Caps = new Dictionary<string, DisplayCapabilities>();
        public readonly List<string> Calls = new List<string>();
        public string FailOn;
        public bool RefuseScaling;
        public readonly HashSet<string> RefusedColors = new HashSet<string>();

        public MonitorInfo Add(string path, string name, int w, int h, uint rateNum, uint rateDen, bool hdrSupported, bool hdrOn, ColorSetting color, bool primary = false)
        {
            var m = new MonitorInfo
            {
                DevicePath = path, FriendlyName = name, Width = w, Height = h, SignalWidth = w, SignalHeight = h,
                RefreshNumerator = rateNum, RefreshDenominator = rateDen, HdrSupported = hdrSupported, HdrOn = hdrOn,
                Color = color, IsPrimary = primary,
            };
            Monitors.Add(m);
            var caps = new DisplayCapabilities { Signal = new PixelSize(w, h), HdrSupported = hdrSupported, Nvidia = color != null };
            caps.DesktopSizes.Add(new PixelSize(3840, 2160));
            caps.DesktopSizes.Add(new PixelSize(2560, 1440));
            caps.DesktopSizes.Add(new PixelSize(1920, 1080));
            caps.RefreshRates.Add(new RefreshRate(144000, 1000));
            caps.RefreshRates.Add(new RefreshRate(120000, 1000));
            caps.RefreshRates.Add(new RefreshRate(120000, 1001));
            caps.RefreshRates.Add(new RefreshRate(60000, 1000));
            Caps[path] = caps;
            return m;
        }

        private MonitorInfo Live(MonitorInfo m) => Monitors.First(x => x.DevicePath == m.DevicePath);

        private void Maybe(string call)
        {
            Calls.Add(call);
            if (FailOn != null && call.StartsWith(FailOn, StringComparison.Ordinal))
            {
                FailOn = null;
                throw new InvalidOperationException("refused by the test");
            }
        }

        public List<MonitorInfo> GetMonitors() => Monitors.Select(Copy).ToList();

        private static MonitorInfo Copy(MonitorInfo m) => new MonitorInfo
        {
            DevicePath = m.DevicePath, FriendlyName = m.FriendlyName, Width = m.Width, Height = m.Height,
            SignalWidth = m.SignalWidth, SignalHeight = m.SignalHeight, RefreshNumerator = m.RefreshNumerator,
            RefreshDenominator = m.RefreshDenominator, HdrSupported = m.HdrSupported, HdrOn = m.HdrOn, Color = m.Color, IsPrimary = m.IsPrimary,
        };

        public DisplayCapabilities GetCapabilities(MonitorInfo monitor) => Caps[monitor.DevicePath];

        public bool SetDesktopSize(MonitorInfo monitor, PixelSize size)
        {
            Maybe("desktop " + size);
            MonitorInfo m = Live(monitor);
            m.Width = size.Width;
            m.Height = size.Height;
            if (RefuseScaling)
            {
                m.SignalWidth = size.Width;
                m.SignalHeight = size.Height;
                return false;
            }
            return true;
        }

        public bool SetRefresh(MonitorInfo monitor, RefreshRate rate, PixelSize desktop)
        {
            Maybe("refresh " + rate + (desktop.IsSet ? " to " + desktop : string.Empty));
            MonitorInfo m = Live(monitor);
            m.RefreshNumerator = rate.Numerator;
            m.RefreshDenominator = rate.Denominator;
            if (desktop.IsSet)
            {
                m.Width = desktop.Width;
                m.Height = desktop.Height;
            }
            return true;
        }

        public bool HdrBrings10Bpc;

        public void SetHdr(MonitorInfo monitor, bool on)
        {
            Maybe("hdr " + (on ? "on" : "off"));
            MonitorInfo m = Live(monitor);
            m.HdrOn = on;
            if (on && HdrBrings10Bpc && m.Color != null)
                m.Color = m.Color.With(10, null, null);
        }

        public bool IsColorSupported(MonitorInfo monitor, ColorSetting color) => !RefusedColors.Contains(color.ToString());

        public void SetColor(MonitorInfo monitor, ColorSetting color)
        {
            Maybe("color " + color);
            Live(monitor).Color = color;
        }

        public void Settle() => Calls.Add("settle");
    }

    public sealed class FakeAudioHardware : IAudioHardware
    {
        public readonly List<AudioDevice> Devices = new List<AudioDevice>();
        public string DefaultPlayback;
        public string DefaultRecording;
        public readonly List<string> Calls = new List<string>();

        public List<AudioDevice> GetDevices(bool playback) => Devices.Where(d => d.Playback == playback).ToList();

        public string GetDefaultId(bool playback) => playback ? DefaultPlayback : DefaultRecording;

        public void SetDefault(AudioDevice device)
        {
            Calls.Add("default " + device.Name);
            if (device.Playback)
                DefaultPlayback = device.Id;
            else
                DefaultRecording = device.Id;
        }
    }

    public sealed class FakePowerPlans : IPowerPlans
    {
        public readonly List<PowerPlan> Plans = new List<PowerPlan>();
        public Guid Active;
        public readonly List<string> Calls = new List<string>();

        public List<PowerPlan> GetPlans() => Plans.ToList();

        public Guid GetActive() => Active;

        public void SetActive(Guid id)
        {
            Calls.Add("active " + Plans.First(p => p.Id == id).Name);
            Active = id;
        }
    }

    public sealed class FakeKeyboard : IKeyboard
    {
        public bool AdminInFront;
        public readonly List<string> Calls = new List<string>();

        public bool KeysHeld;

        public bool FrontWindowRunsAsAdmin() => AdminInFront;

        public bool WaitForHeldKeys(int timeoutMs) => !KeysHeld;

        public void Press(KeyCombination keys, int holdMs) => Calls.Add(KeyNames.Combination(keys) + " held " + holdMs);
    }

    public static partial class Runner
    {
        private static readonly ColorSetting Rgb8 = new ColorSetting(8, ColorSetting.Rgb, ColorSetting.Full);
        private static readonly ColorSetting Rgb10 = new ColorSetting(10, ColorSetting.Rgb, ColorSetting.Full);

        private static (FakeDisplayHardware hw, List<DisplayEntry> entries, DisplayActionRunner runner) DisplayRig()
        {
            var hw = new FakeDisplayHardware();
            hw.Add("tv", "Q90A", 3840, 2160, 120000, 1000, true, false, Rgb10, true);
            hw.Add("pc", "MG28U", 3840, 2160, 60000, 1000, false, false, null);
            var entries = new List<DisplayEntry>
            {
                new DisplayEntry { DevicePath = "tv", FriendlyName = "Q90A", Managed = true },
                new DisplayEntry { DevicePath = "pc", FriendlyName = "MG28U", Managed = false },
                new DisplayEntry { DevicePath = "gone", FriendlyName = "Old TV", Managed = true },
            };
            return (hw, entries, new DisplayActionRunner(hw));
        }

        private static void DisplayOnlyDiffers()
        {
            var (hw, entries, runner) = DisplayRig();
            var a = new DisplayAction { DisplayId = entries[0].Id, Hdr = HdrChange.Off, Width = 3840, Height = 2160, RefreshNumerator = 120000, RefreshDenominator = 1000, ColorDepthBpc = 10 };
            ActionOutcome r = runner.Run(a, entries);
            Eq(ActionResult.Succeeded, r.Result, "result");
            Eq(0, hw.Calls.Count, "nothing sent when everything is already set");
            Check(r.Message.Contains("already set"), "message says already set: " + r.Message);

            a = new DisplayAction { DisplayId = entries[0].Id };
            Eq(ActionResult.Skipped, runner.Run(a, entries).Result, "an action with nothing to change is skipped");
        }

        private static void DisplaySafeOrder()
        {
            var (hw, entries, runner) = DisplayRig();
            var a = new DisplayAction { DisplayId = entries[0].Id, RefreshNumerator = 144000, RefreshDenominator = 1000, ColorDepthBpc = 8 };
            ActionOutcome r = runner.Run(a, entries);
            Eq(ActionResult.Succeeded, r.Result, "result");
            Eq("color 8 bpc RGB Full|settle|refresh 144 Hz|settle", string.Join("|", hw.Calls), "colour first, a pause after each");

            hw.Calls.Clear();
            a = new DisplayAction { DisplayId = entries[0].Id, RefreshNumerator = 120000, RefreshDenominator = 1000, ColorDepthBpc = 10 };
            runner.Run(a, entries);
            Eq("refresh 120 Hz|settle|color 10 bpc RGB Full|settle", string.Join("|", hw.Calls), "rate first");

            hw.Calls.Clear();
            a = new DisplayAction { DisplayId = entries[0].Id, Width = 2560, Height = 1440, Hdr = HdrChange.On, RefreshNumerator = 60000, RefreshDenominator = 1000 };
            runner.Run(a, entries);
            Eq("hdr on|settle|refresh 60 Hz to 2560 × 1440|settle", string.Join("|", hw.Calls), "HDR, then rate and desktop together");

            hw.Calls.Clear();
            a = new DisplayAction { DisplayId = entries[0].Id, Width = 1920, Height = 1080, Hdr = HdrChange.Off };
            runner.Run(a, entries);
            Eq("desktop 1920 × 1080|hdr off|settle", string.Join("|", hw.Calls), "desktop, then HDR");

            hw.Calls.Clear();
            hw.RefuseScaling = true;
            a = new DisplayAction { DisplayId = entries[0].Id, Width = 2560, Height = 1440 };
            runner.Run(a, entries);
            Eq("desktop 2560 × 1440|settle", string.Join("|", hw.Calls), "pause after a desktop change that changed the signal");
        }

        private static void DisplayUndo()
        {
            var (hw, entries, runner) = DisplayRig();
            hw.FailOn = "refresh";
            var a = new DisplayAction { DisplayId = entries[0].Id, Width = 2560, Height = 1440, Hdr = HdrChange.On, RefreshNumerator = 60000, RefreshDenominator = 1000 };
            ActionOutcome r = runner.Run(a, entries);
            Eq(ActionResult.Failed, r.Result, "failed");
            Eq("hdr on|settle|refresh 60 Hz to 2560 × 1440|hdr off|settle",
                string.Join("|", hw.Calls), "the steps made are undone, latest first");
            MonitorInfo tv = hw.Monitors[0];
            Check(tv.Width == 3840 && !tv.HdrOn && tv.RefreshNumerator == 120000, "back to the start state");
        }

        private static void DisplayTargets()
        {
            var (hw, entries, runner) = DisplayRig();
            var a = new DisplayAction { DisplayId = Guid.Empty, Hdr = HdrChange.On };
            ActionOutcome r = runner.Run(a, entries);
            Eq(ActionResult.Succeeded, r.Result, "all managed: result");
            Eq("hdr on|settle", string.Join("|", hw.Calls), "all managed: the TV only");

            entries[1].Managed = true;
            hw.Calls.Clear();
            hw.Monitors[0].HdrOn = false;
            r = runner.Run(a, entries);
            Eq("hdr on|settle", string.Join("|", hw.Calls), "all managed: HDR only where supported");
            Check(!r.Message.Contains("not supported"), "no note for a managed display without HDR: " + r.Message);
            entries[1].Managed = false;

            Eq(ActionResult.Skipped, runner.Run(new DisplayAction { DisplayId = entries[2].Id, Hdr = HdrChange.On }, entries).Result, "not connected: skipped");
            Eq(ActionResult.Skipped, runner.Run(new DisplayAction { DisplayId = Guid.NewGuid(), Hdr = HdrChange.On }, entries).Result, "missing display: skipped");

            hw.Calls.Clear();
            runner.Run(new DisplayAction { DisplayId = entries[0].Id, RefreshNumerator = 120000, RefreshDenominator = 1001 }, entries);
            Eq("refresh 119.88 Hz|settle", string.Join("|", hw.Calls), "119.88 Hz sent");
            r = runner.Run(new DisplayAction { DisplayId = entries[0].Id, RefreshNumerator = 100000, RefreshDenominator = 1000 }, entries);
            Eq(ActionResult.Failed, r.Result, "100 Hz not offered: failed");
            hw.Calls.Clear();
            r = runner.Run(new DisplayAction { DisplayId = entries[0].Id, RefreshNumerator = 120200, RefreshDenominator = 1000 }, entries);
            Eq("refresh 120 Hz|settle", string.Join("|", hw.Calls), "nearest within 0.5 Hz");
            Check(r.Message.Contains("nearest"), "nearest noted: " + r.Message);

            hw.Calls.Clear();
            r = runner.Run(new DisplayAction { DisplayId = entries[1].Id, Hdr = HdrChange.On }, entries);
            Eq(0, hw.Calls.Count, "HDR not supported: nothing sent");
            Check(r.Message.Contains("not supported"), "HDR not supported noted: " + r.Message);
        }

        private static void DisplayColorGuard()
        {
            var (hw, entries, runner) = DisplayRig();
            hw.RefusedColors.Add(new ColorSetting(12, ColorSetting.Rgb, ColorSetting.Full).ToString());
            var a = new DisplayAction { DisplayId = entries[0].Id, Hdr = HdrChange.On, ColorDepthBpc = 12 };
            ActionOutcome r = runner.Run(a, entries);
            Eq(ActionResult.Succeeded, r.Result, "a refused colour does not fail the action");
            Eq("hdr on|settle", string.Join("|", hw.Calls), "colour not sent, HDR still done");
            Check(r.Message.Contains("not accepted"), "noted: " + r.Message);

            hw.Calls.Clear();
            r = runner.Run(new DisplayAction { DisplayId = entries[1].Id, ColorFormat = ColorSetting.YCbCr422 }, entries);
            Eq(0, hw.Calls.Count, "no NVIDIA: nothing sent");
            Check(r.Message.Contains("NVIDIA"), "noted: " + r.Message);
        }

        private static void CapabilitiesSaved()
        {
            var s = new AppSettings();
            var caps = new DisplayCapabilities { Signal = new PixelSize(3840, 2160), HdrSupported = true, Nvidia = true };
            caps.DesktopSizes.Add(new PixelSize(2560, 1440));
            caps.RefreshRates.Add(new RefreshRate(120000, 1001));
            caps.Colors.Add(new ColorSetting(10, ColorSetting.YCbCr422, ColorSetting.Limited));
            s.Displays.Add(new DisplayEntry { DevicePath = "tv", Known = caps });
            var p = new Profile { Name = "P" };
            p.OnStart.Add(new WindowsSettingsAction { PlaybackDevice = "g", PlaybackName = "TV speakers" });
            s.Profiles.Add(p);
            AppSettings back = SettingsJson.Read(SettingsJson.Write(s), new List<string>());
            DisplayCapabilities k = back.Displays[0].Known;
            Check(k != null && k.Signal.Equals(new PixelSize(3840, 2160)) && k.HdrSupported && k.Nvidia, "signal, HDR, NVIDIA kept");
            Check(k.DesktopSizes.SequenceEqual(caps.DesktopSizes) && k.RefreshRates.SequenceEqual(caps.RefreshRates) && k.Colors.SequenceEqual(caps.Colors), "lists kept");
            Eq("TV speakers", ((WindowsSettingsAction)back.Profiles[0].OnStart[0]).PlaybackName, "audio device name kept");
        }

        private static void DisplayMaxAndInPlace()
        {
            var (hw, entries, runner) = DisplayRig();
            ActionOutcome r = runner.Run(new DisplayAction { DisplayId = entries[0].Id, RefreshMax = true }, entries);
            Eq(ActionResult.Succeeded, r.Result, "max: result");
            Eq("refresh 144 Hz|settle", string.Join("|", hw.Calls), "max: highest rate offered");
            hw.Calls.Clear();
            runner.Run(new DisplayAction { DisplayId = entries[0].Id, RefreshMax = true }, entries);
            Eq(0, hw.Calls.Count, "max: already at the maximum, nothing sent");
            Check(DisplayActionRunner.HasChanges(new DisplayAction { RefreshMax = true }), "max counts as a change");

            hw.Calls.Clear();
            hw.HdrBrings10Bpc = true;
            hw.Monitors[0].Color = Rgb8;
            r = runner.Run(new DisplayAction { DisplayId = entries[0].Id, Hdr = HdrChange.On, ColorDepthBpc = 10 }, entries);
            Eq(ActionResult.Succeeded, r.Result, "in place: result");
            Eq("hdr on|settle", string.Join("|", hw.Calls), "colour already in place after HDR: skipped");
        }

        private static void AudioActions()
        {
            var hw = new FakeAudioHardware();
            var speakers = new AudioDevice { Id = "{0.0.0.00000000}.{11111111-1111-1111-1111-111111111111}", Name = "Speakers", Playback = true };
            var tv = new AudioDevice { Id = "{0.0.0.00000000}.{22222222-2222-2222-2222-222222222222}", Name = "TV", Playback = true };
            var mic = new AudioDevice { Id = "{0.0.1.00000000}.{33333333-3333-3333-3333-333333333333}", Name = "Mic", Playback = false };
            hw.Devices.AddRange(new[] { speakers, tv, mic });
            hw.DefaultPlayback = speakers.Id;
            hw.DefaultRecording = mic.Id;
            var runner = new WindowsSettingsRunner(hw, null, null);

            ActionOutcome r = runner.Run(new WindowsSettingsAction { PlaybackDevice = "22222222-2222-2222-2222-222222222222", RecordDevice = "{33333333-3333-3333-3333-333333333333}" });
            Eq(ActionResult.Succeeded, r.Result, "result");
            Eq("default TV", string.Join("|", hw.Calls), "TV made default; the microphone already was");
            Eq(tv.Id, hw.DefaultPlayback, "default playback");

            r = runner.Run(new WindowsSettingsAction { PlaybackDevice = "44444444-4444-4444-4444-444444444444", PlaybackName = "Headset" });
            Eq(ActionResult.Failed, r.Result, "missing device fails");
            Check(r.Message.Contains("Headset"), "names the device: " + r.Message);
            Eq(ActionResult.Skipped, runner.Run(new WindowsSettingsAction()).Result, "nothing to change: skipped");
            Check(AudioDevice.GuidOf(tv.Id) == new Guid("22222222-2222-2222-2222-222222222222"), "GUID of an endpoint id");
        }

        private static void PowerPlanActions()
        {
            var balanced = new PowerPlan { Id = new Guid("381b4222-f694-41f0-9685-ff5bb260df2e"), Name = "Balanced" };
            var high = new PowerPlan { Id = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c"), Name = "High performance" };
            var plans = new FakePowerPlans { Active = balanced.Id };
            plans.Plans.AddRange(new[] { balanced, high });
            using var exec = new ActionExecutor(new FakeProcessSystem(new FakeScheduler()), null, null, plans);

            ActionOutcome r = exec.ExecuteAsync(new WindowsSettingsAction { PlanId = high.Id, PlanName = "High performance" }).Result;
            Eq(ActionResult.Succeeded, r.Result, "plan set: result");
            Eq("power plan High performance now active", r.Message, "plan set: message");
            Eq("active High performance", string.Join("|", plans.Calls), "plan made active");
            Eq(high.Id, plans.Active, "active plan");

            plans.Calls.Clear();
            r = exec.ExecuteAsync(new WindowsSettingsAction { PlanId = high.Id, PlanName = "High performance" }).Result;
            Eq(ActionResult.Succeeded, r.Result, "already active: result");
            Eq(0, plans.Calls.Count, "already active: nothing sent");

            r = exec.ExecuteAsync(new WindowsSettingsAction { PlanId = Guid.NewGuid(), PlanName = "Gaming" }).Result;
            Eq(ActionResult.Failed, r.Result, "deleted plan fails");
            Eq("power plan Gaming not found", r.Message, "deleted plan message");
            Eq(high.Id, plans.Active, "deleted plan: active plan unchanged");

            using (var bare = new ActionExecutor(new FakeProcessSystem(new FakeScheduler())))
                Eq(ActionResult.Skipped, bare.ExecuteAsync(new WindowsSettingsAction { PlanId = high.Id }).Result.Result, "no power plans here: skipped");
            Eq("Windows settings", new WindowsSettingsAction { PlanName = "Gaming" }.Describe(), "log name");

            var s = new AppSettings();
            var p = new Profile { Name = "P" };
            p.OnStart.Add(new WindowsSettingsAction { PlanId = high.Id, PlanName = "High performance", DelayMs = 1500 });
            s.Profiles.Add(p);
            var warnings = new List<string>();
            AppSettings back = SettingsJson.Read(SettingsJson.Write(s), warnings);
            var read = back.Profiles[0].OnStart[0] as WindowsSettingsAction;
            Check(read != null && read.PlanId == high.Id && read.PlanName == "High performance" && read.DelayMs == 1500 && !read.ChangesAudio && read.ScreenSaverOn == null, "plan saved and read back");
            Eq(0, warnings.Count, "no warning");
            var copy = ProfileCopy.Duplicate(back.Profiles[0], "P (copy)").OnStart[0] as WindowsSettingsAction;
            Check(copy != null && copy.PlanId == high.Id && !ReferenceEquals(copy, read), "duplicated profile keeps the plan");
        }
        private static void PressHotkeyActions()
        {
            var keyboard = new FakeKeyboard();
            using var exec = new ActionExecutor(new FakeProcessSystem(new FakeScheduler()), null, null, null, keyboard);
            var overlay = new PressHotkeyAction { Keys = new KeyCombination { Ctrl = true, Alt = true, Key = 0x31 } };

            ActionOutcome r = exec.ExecuteAsync(overlay).Result;
            Eq(ActionResult.Succeeded, r.Result, "keys sent: result");
            Eq("Ctrl+Alt+1 sent", r.Message, "keys sent: message");
            Eq("Ctrl+Alt+1 held 50", string.Join("|", keyboard.Calls), "keys pressed and held");

            keyboard.Calls.Clear();
            keyboard.KeysHeld = true;
            r = exec.ExecuteAsync(overlay).Result;
            Eq(ActionResult.Succeeded, r.Result, "keys still held after the wait: sent anyway");
            Check(r.Message.Contains("still held down"), "keys still held: message says so: " + r.Message);
            Eq("Ctrl+Alt+1 held 50", string.Join("|", keyboard.Calls), "keys still held: pressed once");
            keyboard.KeysHeld = false;

            keyboard.Calls.Clear();
            keyboard.AdminInFront = true;
            r = exec.ExecuteAsync(overlay).Result;
            Eq(ActionResult.Failed, r.Result, "administrator window in front: fails");
            Check(r.Message.Contains("administrator"), "administrator window in front: message says why");
            Eq(0, keyboard.Calls.Count, "administrator window in front: nothing sent");

            keyboard.AdminInFront = false;
            r = exec.ExecuteAsync(new PressHotkeyAction()).Result;
            Eq(ActionResult.Failed, r.Result, "no key: fails");
            Eq(0, keyboard.Calls.Count, "no key: nothing sent");

            using (var bare = new ActionExecutor(new FakeProcessSystem(new FakeScheduler())))
                Eq(ActionResult.Skipped, bare.ExecuteAsync(overlay).Result.Result, "no keyboard here: skipped");
            Eq("Hotkey Ctrl+Alt+1", overlay.Describe(), "log name");

            var s = new AppSettings();
            var p = new Profile { Name = "P" };
            p.OnStart.Add(new PressHotkeyAction { Keys = new KeyCombination { Ctrl = true, Alt = true, Key = 0x32 }, DelayMs = 3000 });
            p.OnClose.Add(new PressHotkeyAction { Keys = new KeyCombination { Shift = true, Key = 0x61 } });
            s.Profiles.Add(p);
            var warnings = new List<string>();
            JsonValue written = SettingsJson.Write(s);
            Check(written.ToJson(false).Contains("\"keys\":\"Ctrl+Alt+2\""), "readable keys written in settings.json");
            AppSettings back = SettingsJson.Read(written, warnings);
            var start = back.Profiles[0].OnStart[0] as PressHotkeyAction;
            Check(start != null && start.Keys.Ctrl && start.Keys.Alt && !start.Keys.Shift && start.Keys.Key == 0x32 && start.DelayMs == 3000, "keys saved and read back");
            var close = back.Profiles[0].OnClose[0] as PressHotkeyAction;
            Check(close != null && !close.Keys.Ctrl && !close.Keys.Alt && close.Keys.Shift && close.Keys.Key == 0x61, "second keys saved and read back");
            Eq(0, warnings.Count, "no warning");
            var copy = ProfileCopy.Duplicate(back.Profiles[0], "P (copy)").OnStart[0] as PressHotkeyAction;
            Check(copy != null && copy.Keys.Key == 0x32 && !ReferenceEquals(copy, start), "duplicated profile keeps the keys");

            var odd = SettingsJson.ReadAction(JsonValue.Parse("{\"type\":\"sendKeys\",\"ctrl\":true,\"key\":999}")) as PressHotkeyAction;
            Check(odd != null && odd.Keys.Key == 0, "key out of range read as no key");
        }

        private static void KeyNameTexts()
        {
            Eq("Ctrl+Alt+1", KeyNames.Combination(new KeyCombination { Ctrl = true, Alt = true, Key = 0x31 }), "top-row digit");
            Eq("Ctrl+Alt+Shift+Num 1", KeyNames.Combination(new KeyCombination { Ctrl = true, Alt = true, Shift = true, Key = 0x61 }), "numpad digit and modifier order");
            Eq("F13", KeyNames.Combination(new KeyCombination { Key = 0x7C }), "F13 alone");
            Eq("Alt+Page Down", KeyNames.Combination(new KeyCombination { Alt = true, Key = 0x22 }), "named key");
            Eq("Key BA", KeyNames.Name(0xBA), "punctuation without a layout");
            Eq(";", KeyNames.Name(0xBA, k => ";"), "punctuation from the layout");
            Eq(string.Empty, KeyNames.Combination(new KeyCombination { Ctrl = true }), "no key: empty");
            Check(KeyNames.CanSend(0x31) && KeyNames.CanSend(0x7C) && KeyNames.CanSend(0xBA) && KeyNames.CanSend(0x6F), "keys that can be sent");
            Check(!KeyNames.CanSend(0x11) && !KeyNames.CanSend(0xA2) && !KeyNames.CanSend(0x5B) && !KeyNames.CanSend(0x14) && !KeyNames.CanSend(0x90),
                "modifiers, Windows, Caps Lock and Num Lock cannot be the key");
            Check(KeyNames.IsExtended(0x25) && KeyNames.IsExtended(0x6F) && !KeyNames.IsExtended(0x31) && !KeyNames.IsExtended(0x61), "extended keys");
        }
    }
}
