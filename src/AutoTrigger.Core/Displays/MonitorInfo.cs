namespace AutoTrigger.Core.Displays
{
    public sealed class MonitorInfo
    {
        public string DevicePath { get; set; }

        public string FriendlyName { get; set; }

        public uint TargetId { get; set; }

        public uint AdapterLow { get; set; }

        public int AdapterHigh { get; set; }

        public bool HdrSupported { get; set; }

        public bool HdrOn { get; set; }

        public string GdiName { get; set; } = string.Empty;

        public string AdapterName { get; set; } = string.Empty;

        public int Width { get; set; }

        public int Height { get; set; }

        public uint RefreshNumerator { get; set; }

        public uint RefreshDenominator { get; set; }

        public int BitsPerColor { get; set; }

        public bool IsPrimary { get; set; }

        public int X { get; set; }

        public int Y { get; set; }

        public int SignalWidth { get; set; }

        public int SignalHeight { get; set; }

        public ColorSetting Color { get; set; }

        public PixelSize Desktop => new PixelSize(Width, Height);

        public PixelSize Signal => new PixelSize(SignalWidth, SignalHeight);

        public RefreshRate Refresh => new RefreshRate(RefreshNumerator, RefreshDenominator);

        public string Name => string.IsNullOrEmpty(FriendlyName) ? "display " + TargetId : FriendlyName;
    }
}
