using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AutoTrigger.Core.Displays;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Core.Engine
{
    public sealed class DisplayActionRunner
    {
        private readonly IDisplayHardware _hardware;

        public DisplayActionRunner(IDisplayHardware hardware)
        {
            _hardware = hardware ?? throw new ArgumentNullException(nameof(hardware));
        }

        public static bool HasChanges(DisplayAction a) =>
            a.Hdr != HdrChange.Unchanged || (a.Width > 0 && a.Height > 0) || a.RefreshNumerator > 0 || a.RefreshMax
            || a.ColorDepthBpc > 0 || !string.IsNullOrEmpty(a.ColorFormat) || !string.IsNullOrEmpty(a.DynamicRange);

        public ActionOutcome Run(DisplayAction action, IReadOnlyList<DisplayEntry> displays)
        {
            if (!HasChanges(action))
                return ActionOutcome.Skip("nothing to change");

            List<MonitorInfo> monitors = _hardware.GetMonitors();
            var targets = new List<MonitorInfo>();
            if (action.DisplayId == Guid.Empty)
            {
                foreach (DisplayEntry entry in displays.Where(d => d.Managed && d.IsLinked))
                {
                    MonitorInfo monitor = Find(monitors, entry);
                    if (monitor != null)
                        targets.Add(monitor);
                }
                if (targets.Count == 0)
                    return ActionOutcome.Skip("no managed display is connected");
            }
            else
            {
                DisplayEntry entry = displays.FirstOrDefault(d => d.Id == action.DisplayId);
                if (entry == null)
                    return ActionOutcome.Skip("the display of this action is no longer in the list");
                MonitorInfo monitor = Find(monitors, entry);
                if (monitor == null)
                    return ActionOutcome.Skip($"{NameOf(entry)} is not connected");
                targets.Add(monitor);
            }

            var reports = new List<string>();
            var failures = new List<string>();
            foreach (MonitorInfo monitor in targets)
            {
                DisplayResult result = RunOne(action, monitor, action.DisplayId == Guid.Empty);
                if (result.Failed)
                    failures.Add($"{monitor.Name}: {result.Text}");
                else
                    reports.Add($"{monitor.Name}: {result.Text}");
            }

            if (failures.Count > 0)
                return ActionOutcome.Failure(string.Join("; ", failures.Concat(reports)));
            return ActionOutcome.Success(string.Join("; ", reports));
        }

        private const string AlreadySet = "already set";

        private static MonitorInfo Find(List<MonitorInfo> monitors, DisplayEntry entry) =>
            entry.IsLinked
                ? monitors.FirstOrDefault(m => string.Equals(m.DevicePath, entry.DevicePath, StringComparison.OrdinalIgnoreCase))
                : null;

        private static string NameOf(DisplayEntry entry) =>
            !string.IsNullOrEmpty(entry.FriendlyName) ? entry.FriendlyName : "the display";

        private enum StepKind
        {
            Desktop,
            Hdr,
            Refresh,
            Color,
        }

        private sealed class Step
        {
            public StepKind Kind;
            public PixelSize FromSize, ToSize;
            public bool FromHdr, ToHdr;
            public RefreshRate FromRate, ToRate;
            public ColorSetting FromColor, ToColor;

            public string Describe()
            {
                switch (Kind)
                {
                    case StepKind.Desktop: return ToSize.ToString();
                    case StepKind.Hdr: return ToHdr ? "HDR on" : "HDR off";
                    case StepKind.Refresh: return ToSize.IsSet ? ToSize + ", " + ToRate : ToRate.ToString();
                    default: return ToColor.ToString();
                }
            }
        }

        private sealed class DisplayResult
        {
            public bool Failed;
            public bool Changed;
            public string Text;
        }

        private sealed class Plan
        {
            public readonly List<Step> Steps = new List<Step>();
            public readonly List<string> Notes = new List<string>();
            public string Failure;
        }

        private DisplayResult RunOne(DisplayAction action, MonitorInfo monitor, bool allManaged)
        {
            Log.Detailed($"{monitor.Name} before: {Describe(monitor)}");
            Plan plan = PlanSteps(action, monitor, allManaged);
            if (plan.Failure != null)
                return Fail(plan.Failure);
            if (plan.Steps.Count == 0)
                return new DisplayResult { Text = plan.Notes.Count > 0 ? string.Join(", ", plan.Notes) : AlreadySet };
            return ApplySteps(monitor, plan);
        }

        private Plan PlanSteps(DisplayAction action, MonitorInfo monitor, bool allManaged)
        {
            var plan = new Plan();
            var caps = new Lazy<DisplayCapabilities>(() => _hardware.GetCapabilities(monitor), LazyThreadSafetyMode.None);

            Step desktop = DesktopStep(action, monitor, caps, plan);
            if (plan.Failure != null)
                return plan;
            Step hdr = HdrStep(action, monitor, allManaged, plan);
            Step refresh = RefreshStep(action, monitor, caps, plan);
            if (plan.Failure != null)
                return plan;

            if (refresh != null && desktop != null)
            {
                refresh.ToSize = desktop.ToSize;
                desktop = null;
            }
            Step color = ColorStep(action, monitor, allManaged, plan);

            if (desktop != null)
                plan.Steps.Add(desktop);
            if (hdr != null)
                plan.Steps.Add(hdr);
            if (refresh != null && color != null)
            {
                bool colorFirst = refresh.FromRate.Hz * color.ToColor.DataPerPixel <= refresh.ToRate.Hz * color.FromColor.DataPerPixel;
                plan.Steps.Add(colorFirst ? color : refresh);
                plan.Steps.Add(colorFirst ? refresh : color);
            }
            else if (refresh != null)
            {
                plan.Steps.Add(refresh);
            }
            else if (color != null)
            {
                plan.Steps.Add(color);
            }
            return plan;
        }

        private static Step DesktopStep(DisplayAction action, MonitorInfo monitor, Lazy<DisplayCapabilities> caps, Plan plan)
        {
            var wanted = new PixelSize(action.Width, action.Height);
            if (!wanted.IsSet || wanted.Equals(monitor.Desktop))
                return null;
            if (!caps.Value.DesktopSizes.Contains(wanted))
            {
                plan.Failure = $"{wanted} is not offered by this display";
                return null;
            }
            return new Step { Kind = StepKind.Desktop, FromSize = monitor.Desktop, ToSize = wanted };
        }

        private static Step HdrStep(DisplayAction action, MonitorInfo monitor, bool allManaged, Plan plan)
        {
            if (action.Hdr == HdrChange.Unchanged)
                return null;
            bool on = action.Hdr == HdrChange.On;
            if (on && !monitor.HdrSupported)
            {
                if (allManaged)
                {
                    Log.Detailed($"{monitor.Name}: no HDR; left out");
                }
                else
                {
                    plan.Notes.Add("HDR not supported");
                    Log.Warning($"{monitor.Name}: HDR is not supported; HDR left off");
                }
                return null;
            }
            return monitor.HdrOn == on ? null : new Step { Kind = StepKind.Hdr, FromHdr = monitor.HdrOn, ToHdr = on };
        }

        private static Step RefreshStep(DisplayAction action, MonitorInfo monitor, Lazy<DisplayCapabilities> caps, Plan plan)
        {
            var wanted = new RefreshRate(action.RefreshNumerator, action.RefreshDenominator);
            if (action.RefreshMax)
            {
                wanted = caps.Value.RefreshRates.OrderByDescending(r => r.Hz).FirstOrDefault();
                if (!wanted.IsSet)
                {
                    plan.Failure = "the refresh rates of this display cannot be read";
                    return null;
                }
            }
            if (!wanted.IsSet || wanted.SameAs(monitor.Refresh))
                return null;
            if (!RefreshRate.TryMatch(wanted, caps.Value.RefreshRates, out RefreshRate match, out bool exact))
            {
                plan.Failure = $"{wanted} is not offered by this display";
                return null;
            }
            if (!exact)
            {
                plan.Notes.Add($"{match} (nearest to {wanted})");
                Log.Warning($"{monitor.Name}: {wanted} is not offered; using {match}, the nearest");
            }
            if (match.SameAs(monitor.Refresh))
                return null;
            return new Step { Kind = StepKind.Refresh, FromRate = monitor.Refresh, ToRate = match, FromSize = monitor.Desktop };
        }

        private static Step ColorStep(DisplayAction action, MonitorInfo monitor, bool allManaged, Plan plan)
        {
            bool wanted = action.ColorDepthBpc > 0 || !string.IsNullOrEmpty(action.ColorFormat) || !string.IsNullOrEmpty(action.DynamicRange);
            if (!wanted)
                return null;
            if (monitor.Color == null)
            {
                if (allManaged)
                {
                    Log.Detailed($"{monitor.Name}: not on an NVIDIA graphics card; color left out");
                }
                else
                {
                    plan.Notes.Add("color needs an NVIDIA graphics card");
                    Log.Warning($"{monitor.Name}: the color can only be set on an NVIDIA graphics card; left as is");
                }
                return null;
            }
            ColorSetting target = monitor.Color.With(action.ColorDepthBpc, action.ColorFormat, action.DynamicRange);
            return target.Equals(monitor.Color) ? null : new Step { Kind = StepKind.Color, FromColor = monitor.Color, ToColor = target };
        }

        private DisplayResult ApplySteps(MonitorInfo monitor, Plan plan)
        {
            var done = new List<Step>();
            bool signalChanged = false;
            foreach (Step step in plan.Steps)
            {
                if (signalChanged)
                {
                    MonitorInfo now = Fresh(monitor);
                    if (now != null && InPlace(step, now))
                    {
                        Log.Detailed($"{monitor.Name}: {step.Describe()} already in place");
                        continue;
                    }
                }
                if (step.Kind == StepKind.Color && !SafeIsSupported(monitor, step.ToColor))
                {
                    plan.Notes.Add($"{step.ToColor} not accepted by the driver");
                    Log.Warning($"{monitor.Name}: the driver does not accept {step.ToColor} in the current mode; color left as is");
                    continue;
                }
                try
                {
                    signalChanged |= Apply(monitor, step, forward: true);
                    done.Add(step);
                }
                catch (Exception ex)
                {
                    if (step.Kind == StepKind.Color)
                    {
                        plan.Notes.Add($"{step.ToColor} refused by the driver");
                        Log.Warning($"{monitor.Name}: the driver refused {step.ToColor} ({ex.Message}); color left as is");
                        continue;
                    }
                    Log.Error($"{monitor.Name}: {step.Describe()} failed: {ex.Message}");
                    Undo(monitor, done);
                    return Fail($"{step.Describe()} failed: {ex.Message}" + (done.Count > 0 ? " (earlier changes undone)" : string.Empty));
                }
            }

            if (Log.IsEnabled(LogLevel.Detailed))
            {
                MonitorInfo after = _hardware.GetMonitors().FirstOrDefault(m => m.DevicePath == monitor.DevicePath);
                if (after != null)
                    Log.Detailed($"{monitor.Name} after: {Describe(after)}");
            }

            var text = new List<string>(done.Select(s => s.Describe()));
            text.AddRange(plan.Notes);
            return new DisplayResult { Changed = done.Count > 0, Text = text.Count > 0 ? string.Join(", ", text) : AlreadySet };
        }

        private static DisplayResult Fail(string text) => new DisplayResult { Failed = true, Text = text };

        private MonitorInfo Fresh(MonitorInfo monitor)
        {
            try
            {
                return _hardware.GetMonitors().FirstOrDefault(m =>
                    string.Equals(m.DevicePath, monitor.DevicePath, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Log.Detailed($"{monitor.Name}: cannot be read again ({ex.Message})");
                return null;
            }
        }

        private static bool InPlace(Step step, MonitorInfo now)
        {
            switch (step.Kind)
            {
                case StepKind.Desktop: return now.Desktop.Equals(step.ToSize);
                case StepKind.Hdr: return now.HdrOn == step.ToHdr;
                case StepKind.Refresh: return now.Refresh.SameAs(step.ToRate) && (!step.ToSize.IsSet || now.Desktop.Equals(step.ToSize));
                default: return now.Color != null && now.Color.Equals(step.ToColor);
            }
        }

        private bool SafeIsSupported(MonitorInfo monitor, ColorSetting color)
        {
            try
            {
                return _hardware.IsColorSupported(monitor, color);
            }
            catch (Exception ex)
            {
                Log.Detailed($"{monitor.Name}: color check failed: {ex.Message}");
                return false;
            }
        }

        private bool Apply(MonitorInfo monitor, Step step, bool forward)
        {
            bool signal;
            switch (step.Kind)
            {
                case StepKind.Desktop:
                    bool scaled = _hardware.SetDesktopSize(monitor, forward ? step.ToSize : step.FromSize);
                    if (!scaled)
                        Log.Warning($"{monitor.Name}: the driver refused to scale the desktop; the signal was changed instead");
                    signal = !scaled;
                    break;
                case StepKind.Hdr:
                    _hardware.SetHdr(monitor, forward ? step.ToHdr : step.FromHdr);
                    signal = true;
                    break;
                case StepKind.Refresh:
                    bool kept = _hardware.SetRefresh(monitor, forward ? step.ToRate : step.FromRate, forward ? step.ToSize : step.FromSize);
                    if (!kept)
                        Log.Warning($"{monitor.Name}: the driver refused to scale the desktop; the signal was changed instead");
                    signal = true;
                    break;
                default:
                    _hardware.SetColor(monitor, forward ? step.ToColor : step.FromColor);
                    signal = true;
                    break;
            }
            if (signal)
                _hardware.Settle();
            return signal;
        }

        private void Undo(MonitorInfo monitor, List<Step> done)
        {
            for (int i = done.Count - 1; i >= 0; i--)
            {
                Step step = done[i];
                try
                {
                    Apply(monitor, step, forward: false);
                    Log.Info($"{monitor.Name}: undone: {step.Describe()}");
                }
                catch (Exception ex)
                {
                    Log.Error($"{monitor.Name}: could not undo {step.Describe()}: {ex.Message}");
                }
            }
        }

        private static string Describe(MonitorInfo m) =>
            $"desktop {m.Desktop} at ({m.X}, {m.Y}), signal {m.Signal} at {m.Refresh}, HDR {(m.HdrOn ? "on" : "off")}"
            + (m.Color != null ? ", " + m.Color : string.Empty);
    }
}
