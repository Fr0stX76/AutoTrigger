using System;
using AutoTrigger.Core.Json;

namespace AutoTrigger.Core.Model
{
    public abstract class ProfileAction
    {
        public const int MaxDelayMs = 600000;
        public const int DelayStepMs = 100;

        private int _delayMs;

        public abstract string Type { get; }

        public int DelayMs
        {
            get => _delayMs;
            set
            {
                int rounded = (int)Math.Round(value / (double)DelayStepMs, MidpointRounding.AwayFromZero) * DelayStepMs;
                _delayMs = Math.Max(0, Math.Min(MaxDelayMs, rounded));
            }
        }

        public virtual bool IsHardwareChange => false;

        public abstract string Describe();
    }

    public sealed class RunProgramAction : ProfileAction
    {
        public const string TypeName = "runProgram";
        public const int DefaultHoldCapSec = 30;

        private int _holdCapSec = DefaultHoldCapSec;

        public override string Type => TypeName;

        public string Path { get; set; } = string.Empty;

        public string Arguments { get; set; } = string.Empty;

        public bool HoldNext { get; set; }

        public int HoldCapSec
        {
            get => _holdCapSec;
            set => _holdCapSec = Math.Max(1, Math.Min(3600, value));
        }

        public override string Describe() => "Run program " + System.IO.Path.GetFileName(Path ?? string.Empty);
    }

    public sealed class CloseProgramAction : ProfileAction
    {
        public const string TypeName = "closeProgram";
        public const int DefaultForceAfterSec = 5;

        private int _forceAfterSec = DefaultForceAfterSec;

        public override string Type => TypeName;

        public string Target { get; set; } = string.Empty;

        public bool Force { get; set; }

        public int ForceAfterSec
        {
            get => _forceAfterSec;
            set => _forceAfterSec = Math.Max(0, Math.Min(600, value));
        }

        public override string Describe() => "Close program " + Target;
    }

    public sealed class IncludeProfileAction : ProfileAction
    {
        public const string TypeName = "includeProfile";

        public override string Type => TypeName;

        public Guid ProfileId { get; set; }

        public override string Describe() => "Reference profile " + ProfileId.ToString("D");
    }

    public enum HdrChange
    {
        Unchanged,
        On,
        Off,
    }

    public sealed class DisplayAction : ProfileAction
    {
        public const string TypeName = "display";

        public override string Type => TypeName;

        public override bool IsHardwareChange => true;

        public Guid DisplayId { get; set; }

        public HdrChange Hdr { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public uint RefreshNumerator { get; set; }

        public uint RefreshDenominator { get; set; } = 1;

        public bool RefreshMax { get; set; }

        public int ColorDepthBpc { get; set; }

        public string ColorFormat { get; set; } = string.Empty;

        public string DynamicRange { get; set; } = string.Empty;

        public override string Describe() => "Display";
    }

    public sealed class AudioAction : ProfileAction
    {
        public const string TypeName = "audio";

        public override string Type => TypeName;

        public override bool IsHardwareChange => true;

        public string PlaybackDevice { get; set; } = string.Empty;

        public string RecordDevice { get; set; } = string.Empty;

        public string PlaybackName { get; set; } = string.Empty;

        public string RecordName { get; set; } = string.Empty;

        public override string Describe() => "Audio";
    }

    public sealed class PowerPlanAction : ProfileAction
    {
        public const string TypeName = "powerPlan";

        public override string Type => TypeName;

        public Guid PlanId { get; set; }

        public string PlanName { get; set; } = string.Empty;

        public override string Describe() => "Power plan " + (string.IsNullOrWhiteSpace(PlanName) ? PlanId.ToString("D") : PlanName);
    }

    public sealed class PressHotkeyAction : ProfileAction
    {
        public const string TypeName = "sendKeys";

        public override string Type => TypeName;

        public bool Ctrl { get; set; }

        public bool Alt { get; set; }

        public bool Shift { get; set; }

        public int Key { get; set; }

        public override string Describe() => "Press hotkey " + KeyNames.Combination(this);
    }

    public sealed class UnknownAction : ProfileAction
    {
        public UnknownAction(string typeName, JsonValue raw)
        {
            TypeName = typeName ?? string.Empty;
            Raw = raw;
        }

        public string TypeName { get; }

        public JsonValue Raw { get; }

        public override string Type => TypeName;

        public override string Describe() => "Unknown action '" + TypeName + "'";
    }
}
