using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutoTrigger.Core.Logging;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Ui
{
    // Turns a picture, an .ico or a program into the .ico kept for an application.
    internal static class IconImport
    {
        public static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".ico", ".exe" };

        // A picture is decoded whole: past these, the file is refused rather than filling the memory.
        private const long MaxFileBytes = 20L * 1024 * 1024;
        private const int MaxPixelSide = 16384;
        private static readonly int[] Sides = { IconFile.MaxSide, 48, 32, 24, 16 };

        // The .ico, or null when the file cannot be used; the reason goes to the log.
        public static byte[] FromFile(string path)
        {
            try
            {
                string extension = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
                if (!Extensions.Contains(extension) || !File.Exists(path))
                    return null;
                BitmapSource image = extension == ".exe" ? FromProgram(path) : FromPicture(path, extension == ".ico");
                return image == null ? null : IconFile.FromPngs(Sides.Select(side => new KeyValuePair<int, byte[]>(side, Png(Square(image, side)))));
            }
            catch (Exception ex) when (AppIcons.IsImageError(ex))
            {
                Log.Warning($"Icon not imported from {path}: {ex.Message}");
                return null;
            }
        }

        private static BitmapSource FromProgram(string path)
        {
            byte[] png = AppIcons.Extract(path);
            return png == null ? null : Decode(png, false);
        }

        private static BitmapSource FromPicture(string path, bool ico)
        {
            var info = new FileInfo(path);
            if (info.Length == 0 || info.Length > MaxFileBytes)
            {
                Log.Warning($"Icon not imported from {path}: {info.Length} bytes");
                return null;
            }
            return Decode(File.ReadAllBytes(path), ico);
        }

        // From memory, so that the chosen file is never held open. The size is read from the header first, and a
        // picture is decoded already scaled down, so that a huge one never takes its full size in memory.
        private static BitmapSource Decode(byte[] bytes, bool ico)
        {
            int index, width, height;
            using (var stream = new MemoryStream(bytes))
            {
                BitmapDecoder header = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (header.Frames.Count == 0)
                    return null;
                index = 0;
                for (int i = 1; ico && i < header.Frames.Count; i++)
                {
                    if ((long)header.Frames[i].PixelWidth * header.Frames[i].PixelHeight
                        > (long)header.Frames[index].PixelWidth * header.Frames[index].PixelHeight)
                        index = i;
                }
                width = header.Frames[index].PixelWidth;
                height = header.Frames[index].PixelHeight;
            }
            if (width < 1 || height < 1 || width > MaxPixelSide || height > MaxPixelSide)
                return null;
            using (var stream = new MemoryStream(bytes))
            {
                if (ico)
                {
                    BitmapFrame frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[index];
                    frame.Freeze();
                    return frame;
                }
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                if (width >= height)
                    image.DecodePixelWidth = Math.Min(width, IconFile.MaxSide);
                else
                    image.DecodePixelHeight = Math.Min(height, IconFile.MaxSide);
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }

        // Centred on a transparent square, so that a picture that is not square keeps its shape.
        private static RenderTargetBitmap Square(BitmapSource image, int side)
        {
            double scale = (double)side / Math.Max(image.PixelWidth, image.PixelHeight);
            double width = image.PixelWidth * scale;
            double height = image.PixelHeight * scale;
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (DrawingContext context = visual.RenderOpen())
                context.DrawImage(image, new Rect((side - width) / 2, (side - height) / 2, width, height));
            var target = new RenderTargetBitmap(side, side, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            target.Freeze();
            return target;
        }

        private static byte[] Png(BitmapSource image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0)));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }
    }
}
