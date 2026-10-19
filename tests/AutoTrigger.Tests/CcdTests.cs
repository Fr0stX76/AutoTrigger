using System;
using AutoTrigger.Core.Displays;
using AutoTrigger.Core.Platform;

namespace AutoTrigger.Tests
{
    public static partial class Runner
    {
        private static void PutInt(byte[] b, int offset, int value) => BitConverter.GetBytes(value).CopyTo(b, offset);

        private static int GetInt(byte[] b, int offset) => BitConverter.ToInt32(b, offset);

        private static void SetPath(byte[] paths, int i, int targetId, int sourceMode, int targetMode)
        {
            int o = i * CcdConfig.PathSize;
            PutInt(paths, o + 8, i);
            PutInt(paths, o + 12, sourceMode);
            PutInt(paths, o + 20, 7);
            PutInt(paths, o + 28, targetId);
            PutInt(paths, o + 32, targetMode);
        }

        private static void SetSource(byte[] modes, int i, int w, int h, int x, int y)
        {
            int o = i * CcdConfig.ModeSize;
            PutInt(modes, o, 1);
            PutInt(modes, o + 16, w);
            PutInt(modes, o + 20, h);
            PutInt(modes, o + 28, x);
            PutInt(modes, o + 32, y);
        }

        private static void SetTarget(byte[] modes, int i, int w, int h)
        {
            int o = i * CcdConfig.ModeSize;
            PutInt(modes, o, 2);
            PutInt(modes, o + 40, w);
            PutInt(modes, o + 44, h);
        }

        private static CcdConfig ThreeScreens()
        {
            var paths = new byte[3 * CcdConfig.PathSize];
            var modes = new byte[6 * CcdConfig.ModeSize];
            SetPath(paths, 0, 100, 0, 1);
            SetSource(modes, 0, 3840, 2160, 0, 0);
            SetTarget(modes, 1, 3840, 2160);
            SetPath(paths, 1, 200, 2, 3);
            SetSource(modes, 2, 3840, 2160, -3840, 0);
            SetTarget(modes, 3, 3840, 2160);
            SetPath(paths, 2, 300, 4, 5);
            SetSource(modes, 4, 1920, 1080, 3840, 0);
            SetTarget(modes, 5, 1920, 1080);
            return CcdConfig.FromBytes(paths, 3, modes, 6);
        }

        private static void CcdLayout()
        {
            using (CcdConfig c = ThreeScreens())
            {
                c.ResizeSource(2, new PixelSize(2560, 1440));
                Eq(-2560, c.SourceLeft(2), "left screen keeps its right edge");
                Eq(0, c.SourceLeft(0), "main screen unchanged");
                Eq(3840, c.SourceLeft(4), "right screen unchanged");
                Eq("(0, 0) 3840 × 2160; (-2560, 0) 2560 × 1440; (3840, 0) 1920 × 1080", c.Layout(), "layout");
            }
            using (CcdConfig c = ThreeScreens())
            {
                c.ResizeSource(0, new PixelSize(2560, 1440));
                Eq(0, c.SourceLeft(0), "main screen stays at 0");
                Eq(2560, c.SourceLeft(4), "screen on the right follows the edge");
                Eq(-3840, c.SourceLeft(2), "screen on the left stays");
            }
        }

        private static void CcdReleaseTarget()
        {
            using (CcdConfig c = ThreeScreens())
            {
                int path = c.FindPath(new MonitorInfo { AdapterLow = 7, TargetId = 200 });
                Eq(1, path, "path found by adapter and target id");
                Eq(3u, c.TargetModeOf(path), "target mode");
                c.ReleaseTargetMode(path);
                Eq(5u, c.ModeCount, "target mode removed");
                Eq(CcdConfig.InvalidIndex, c.TargetModeIndex(1), "path has no target mode");
                Eq(3u, c.SourceModeIndex(2), "indexes after it renumbered (source)");
                Eq(4u, c.TargetModeIndex(2), "indexes after it renumbered (target)");
                Eq(0u, c.SourceModeIndex(0), "indexes before it unchanged");
                Check(c.IsSourceMode(3) && c.SourceSize(3).Equals(new PixelSize(1920, 1080)), "modes moved down");
                Check(c.IsTargetMode(4) && c.TargetSize(4).Equals(new PixelSize(1920, 1080)), "last mode moved down");
            }
            var paths = new byte[2 * CcdConfig.PathSize];
            var modes = new byte[3 * CcdConfig.ModeSize];
            SetPath(paths, 0, 100, 0, 2);
            SetPath(paths, 1, 200, 1, 2);
            SetSource(modes, 0, 1920, 1080, 0, 0);
            SetSource(modes, 1, 1920, 1080, 0, 0);
            SetTarget(modes, 2, 1920, 1080);
            using (CcdConfig c = CcdConfig.FromBytes(paths, 2, modes, 3))
            {
                c.ReleaseTargetMode(0);
                Eq(3u, c.ModeCount, "a target mode still used by another path is kept");
                Eq(2u, c.TargetModeIndex(1), "the other path keeps it");
            }
        }
    }
}
