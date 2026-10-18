using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AutoTrigger.Core.Displays
{
    public struct RefreshRate : IEquatable<RefreshRate>
    {
        public const double SameTolerance = 0.02;

        public const double NearTolerance = 0.5;

        public RefreshRate(uint numerator, uint denominator)
        {
            Numerator = numerator;
            Denominator = denominator == 0 ? 1 : denominator;
        }

        public uint Numerator { get; }

        public uint Denominator { get; }

        public bool IsSet => Numerator > 0;

        public double Hz => IsSet ? (double)Numerator / Denominator : 0;

        public bool SameAs(RefreshRate other) => IsSet && other.IsSet && Math.Abs(Hz - other.Hz) < SameTolerance;

        public bool Equals(RefreshRate other) => Numerator == other.Numerator && Denominator == other.Denominator;

        public override bool Equals(object obj) => obj is RefreshRate r && Equals(r);

        public override int GetHashCode() => (int)(Numerator * 397 ^ Denominator);

        public override string ToString()
        {
            if (!IsSet)
                return "—";
            double rounded = Math.Round(Hz, 2);
            string text = Math.Abs(rounded - Math.Round(rounded)) < 0.005
                ? Math.Round(rounded).ToString(CultureInfo.InvariantCulture)
                : rounded.ToString("0.##", CultureInfo.InvariantCulture);
            return text + " Hz";
        }

        public static bool TryMatch(RefreshRate wanted, IEnumerable<RefreshRate> offered, out RefreshRate match, out bool exact)
        {
            match = default;
            exact = false;
            double best = double.MaxValue;
            foreach (RefreshRate rate in offered)
            {
                double diff = Math.Abs(rate.Hz - wanted.Hz);
                if (diff < best)
                {
                    best = diff;
                    match = rate;
                }
            }
            if (best < SameTolerance)
            {
                exact = true;
                return true;
            }
            return best <= NearTolerance;
        }
    }

    public struct PixelSize : IEquatable<PixelSize>
    {
        public PixelSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }

        public int Height { get; }

        public bool IsSet => Width > 0 && Height > 0;

        public bool Equals(PixelSize other) => Width == other.Width && Height == other.Height;

        public override bool Equals(object obj) => obj is PixelSize s && Equals(s);

        public override int GetHashCode() => Width * 397 ^ Height;

        public override string ToString() =>
            Width.ToString(CultureInfo.InvariantCulture) + " × " + Height.ToString(CultureInfo.InvariantCulture);
    }

    public sealed class ColorSetting : IEquatable<ColorSetting>
    {
        public const string Rgb = "rgb";
        public const string YCbCr444 = "ycbcr444";
        public const string YCbCr422 = "ycbcr422";
        public const string YCbCr420 = "ycbcr420";
        public const string Full = "full";
        public const string Limited = "limited";

        public static readonly string[] Formats = { Rgb, YCbCr444, YCbCr422, YCbCr420 };
        public static readonly string[] Ranges = { Full, Limited };

        public ColorSetting(int bpc, string format, string range)
        {
            Bpc = bpc;
            Format = format ?? string.Empty;
            Range = range ?? string.Empty;
        }

        public int Bpc { get; }

        public string Format { get; }

        public string Range { get; }

        public ColorSetting With(int bpc, string format, string range) => new ColorSetting(
            bpc > 0 ? bpc : Bpc,
            string.IsNullOrEmpty(format) ? Format : format,
            string.IsNullOrEmpty(range) ? Range : range);

        public double DataPerPixel => Bpc * SamplesPerPixel;

        private double SamplesPerPixel => Format switch
        {
            YCbCr422 => 2.0,
            YCbCr420 => 1.5,
            _ => 3.0,
        };

        public bool Equals(ColorSetting other) =>
            other != null && Bpc == other.Bpc && Format == other.Format && Range == other.Range;

        public override bool Equals(object obj) => Equals(obj as ColorSetting);

        public override int GetHashCode() => Bpc * 397 ^ Format.GetHashCode() ^ Range.GetHashCode() * 7;

        public override string ToString() =>
            Bpc.ToString(CultureInfo.InvariantCulture) + " bpc " + FormatName(Format) + " " + RangeName(Range);

        public static string FormatName(string format)
        {
            switch (format)
            {
                case Rgb: return "RGB";
                case YCbCr444: return "YCbCr 4:4:4";
                case YCbCr422: return "YCbCr 4:2:2";
                case YCbCr420: return "YCbCr 4:2:0";
                default: return format ?? string.Empty;
            }
        }

        public static string RangeName(string range)
        {
            switch (range)
            {
                case Full: return "Full";
                case Limited: return "Limited";
                default: return range ?? string.Empty;
            }
        }
    }

    public sealed class DisplayCapabilities
    {
        public List<PixelSize> DesktopSizes { get; } = new List<PixelSize>();

        public PixelSize Signal { get; set; }

        public List<RefreshRate> RefreshRates { get; } = new List<RefreshRate>();

        public bool HdrSupported { get; set; }

        public bool Nvidia { get; set; }

        public List<ColorSetting> Colors { get; } = new List<ColorSetting>();

        public static DisplayCapabilities Common(IList<DisplayCapabilities> all)
        {
            var common = new DisplayCapabilities();
            if (all.Count == 0)
                return common;
            DisplayCapabilities first = all[0];
            common.DesktopSizes.AddRange(first.DesktopSizes.Where(s => all.All(c => c.DesktopSizes.Contains(s))));
            common.RefreshRates.AddRange(first.RefreshRates.Where(r => all.All(c => c.RefreshRates.Any(x => x.SameAs(r)))));
            common.HdrSupported = all.Any(c => c.HdrSupported);
            common.Nvidia = all.All(c => c.Nvidia);
            if (common.Nvidia)
                common.Colors.AddRange(first.Colors.Where(k => all.All(c => c.Colors.Contains(k))));
            return common;
        }
    }
}
