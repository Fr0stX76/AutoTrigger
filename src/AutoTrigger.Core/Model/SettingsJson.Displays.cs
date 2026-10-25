using AutoTrigger.Core.Displays;
using AutoTrigger.Core.Json;

namespace AutoTrigger.Core.Model
{
    public static partial class SettingsJson
    {
        private static DisplayCapabilities ReadCapabilities(JsonValue known)
        {
            if (known == null || known.Kind != JsonKind.Object)
                return null;
            var caps = new DisplayCapabilities
            {
                HdrSupported = Bool(known, "hdr", false),
                Nvidia = Bool(known, "nvidia", false),
            };
            foreach (JsonValue item in Array(known, "desktopSizes"))
            {
                PixelSize size = ReadSize(item);
                if (size.IsSet && !caps.DesktopSizes.Contains(size))
                    caps.DesktopSizes.Add(size);
            }
            caps.Signal = ReadSize(known["signal"]);
            foreach (JsonValue rate in Array(known, "refreshRates"))
            {
                if (rate.Kind == JsonKind.Array && rate.Count == 2 && rate[0].AsNumber() > 0 && rate[1].AsNumber() > 0)
                    caps.RefreshRates.Add(new RefreshRate((uint)rate[0].AsNumber().Value, (uint)rate[1].AsNumber().Value));
            }
            foreach (JsonValue color in Array(known, "colors"))
            {
                if (color.Kind == JsonKind.Array && color.Count == 3 && color[0].AsNumber() > 0
                    && color[1].Kind == JsonKind.String && color[2].Kind == JsonKind.String)
                    caps.Colors.Add(new ColorSetting((int)color[0].AsNumber().Value, color[1].AsString(), color[2].AsString()));
            }
            return caps;
        }

        private static PixelSize ReadSize(JsonValue pair)
        {
            if (pair == null || pair.Kind != JsonKind.Array || pair.Count != 2)
                return default;
            double width = pair[0].AsNumber() ?? 0;
            double height = pair[1].AsNumber() ?? 0;
            return width > 0 && height > 0 ? new PixelSize((int)width, (int)height) : default;
        }

        private static JsonValue WriteCapabilities(DisplayCapabilities caps)
        {
            if (caps == null)
                return JsonValue.Null;
            var sizes = JsonValue.NewArray();
            foreach (PixelSize size in caps.DesktopSizes)
                sizes.Add(WriteSize(size));
            var rates = JsonValue.NewArray();
            foreach (RefreshRate rate in caps.RefreshRates)
            {
                rates.Add(JsonValue.NewArray()
                    .Add(JsonValue.FromNumber((long)rate.Numerator))
                    .Add(JsonValue.FromNumber((long)rate.Denominator)));
            }
            var colors = JsonValue.NewArray();
            foreach (ColorSetting color in caps.Colors)
            {
                colors.Add(JsonValue.NewArray()
                    .Add(JsonValue.FromNumber((long)color.Bpc))
                    .Add(JsonValue.FromString(color.Format))
                    .Add(JsonValue.FromString(color.Range)));
            }
            return JsonValue.NewObject()
                .Set("desktopSizes", sizes)
                .Set("signal", caps.Signal.IsSet ? WriteSize(caps.Signal) : JsonValue.Null)
                .Set("refreshRates", rates)
                .Set("hdr", caps.HdrSupported)
                .Set("nvidia", caps.Nvidia)
                .Set("colors", colors);
        }

        public static bool SameCapabilities(DisplayCapabilities stored, DisplayCapabilities live)
        {
            string saved = WriteCapabilities(stored).ToJson(false);
            string reloaded = WriteCapabilities(ReadCapabilities(WriteCapabilities(live))).ToJson(false);
            return saved == reloaded;
        }

        private static JsonValue WriteSize(PixelSize size) =>
            JsonValue.NewArray().Add(JsonValue.FromNumber((long)size.Width)).Add(JsonValue.FromNumber((long)size.Height));
    }
}
