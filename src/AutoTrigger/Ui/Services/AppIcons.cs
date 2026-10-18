using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AutoTrigger.Core.Logging;

namespace AutoTrigger.Ui
{
    internal sealed class AppIcons : IDisposable
    {
        private readonly string _cacheDir;
        private readonly Dispatcher _ui;
        private readonly Dictionary<string, ImageSource> _memory = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<Action<ImageSource>>> _waiting =
            new Dictionary<string, List<Action<ImageSource>>>(StringComparer.OrdinalIgnoreCase);
        private readonly BlockingCollection<string> _queue = new BlockingCollection<string>();
        private readonly Thread _worker;
        private ImageSource _generic;
        private bool _genericTried;

        public AppIcons(string cacheDir, Dispatcher ui)
        {
            _cacheDir = cacheDir;
            _ui = ui;
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "AutoTrigger icons" };
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start();
        }

        public void Get(string exePath, Action<ImageSource> apply)
        {
            string path = (exePath ?? string.Empty).Trim();
            bool known = _memory.TryGetValue(path, out ImageSource icon);
            if (known)
                apply(icon);
            if (_waiting.TryGetValue(path, out List<Action<ImageSource>> list))
            {
                list.Add(apply);
                return;
            }
            if (known)
                return;
            _waiting[path] = new List<Action<ImageSource>> { apply };
            try
            {
                _queue.Add(path);
            }
            catch (InvalidOperationException)
            {
                _waiting.Remove(path);
            }
        }

        private void WorkerLoop()
        {
            foreach (string path in _queue.GetConsumingEnumerable())
            {
                try
                {
                    Load(path);
                }
                catch (Exception ex)
                {
                    Log.Detailed($"Icon of {path} cannot be read: {ex.Message}");
                    Deliver(path, null, false, true);
                }
            }
        }

        private void Load(string path)
        {
            string cacheFile = path.Length > 0 ? CacheFile(path) : null;
            ImageSource cached = cacheFile != null ? Decode(cacheFile) : null;
            bool exeExists = SafeExists(path);
            bool stale = exeExists && (cached == null || SafeWriteTime(path) > SafeWriteTime(cacheFile));
            if (cached != null && !stale)
            {
                Deliver(path, cached, true, true);
                return;
            }
            if (cached != null)
                Deliver(path, cached, true, false);
            if (exeExists)
            {
                byte[] png = null;
                try
                {
                    png = Extract(path);
                    if (png != null)
                        WriteCache(cacheFile, png);
                }
                catch (Exception ex)
                {
                    Log.Detailed($"Icon of {path} cannot be read: {ex.Message}");
                }
                ImageSource fresh = png != null ? Decode(png) : null;
                if (fresh != null || cached != null)
                {
                    Deliver(path, fresh, fresh != null, true);
                    return;
                }
            }
            Deliver(path, Generic(), false, true);
        }

        private ImageSource Generic()
        {
            if (!_genericTried)
            {
                _genericTried = true;
                try
                {
                    byte[] png = Extract(null);
                    _generic = png != null ? Decode(png) : null;
                }
                catch (Exception ex)
                {
                    Log.Detailed($"Generic icon cannot be read: {ex.Message}");
                }
            }
            return _generic;
        }

        private void Deliver(string path, ImageSource icon, bool keep, bool last)
        {
            _ui.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (keep && icon != null)
                    _memory[path] = icon;
                if (!_waiting.TryGetValue(path, out List<Action<ImageSource>> list))
                    return;
                if (last)
                    _waiting.Remove(path);
                if (icon == null)
                    return;
                foreach (Action<ImageSource> apply in list.ToArray())
                    apply(icon);
            }));
        }

        private static byte[] Extract(string exePath)
        {
            int index = SystemIconIndex(exePath);
            if (index < 0)
                return null;
            BitmapSource image = FromImageList(NativeUi.SHIL_JUMBO, index);
            if (image == null || IsSmallInside(image))
                image = FromImageList(NativeUi.SHIL_EXTRALARGE, index) ?? image;
            if (image == null)
                return null;
            image = CropToContent(image);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        private static int SystemIconIndex(string exePath)
        {
            var info = new NativeUi.SHFILEINFOW();
            uint size = (uint)Marshal.SizeOf(typeof(NativeUi.SHFILEINFOW));
            IntPtr ok;
            if (exePath == null)
            {
                uint flags = NativeUi.SHGFI_SYSICONINDEX | NativeUi.SHGFI_USEFILEATTRIBUTES;
                ok = NativeUi.SHGetFileInfo(".exe", NativeUi.FILE_ATTRIBUTE_NORMAL, ref info, size, flags);
            }
            else
            {
                ok = NativeUi.SHGetFileInfo(exePath, 0, ref info, size, NativeUi.SHGFI_SYSICONINDEX);
            }
            return ok == IntPtr.Zero ? -1 : info.iIcon;
        }

        private static BitmapSource FromImageList(int which, int index)
        {
            Guid iid = NativeUi.IID_IImageList;
            if (NativeUi.SHGetImageList(which, ref iid, out IntPtr list) != 0 || list == IntPtr.Zero)
                return null;
            try
            {
                IntPtr icon = NativeUi.ImageList_GetIcon(list, index, NativeUi.ILD_TRANSPARENT);
                if (icon == IntPtr.Zero)
                    return null;
                try
                {
                    BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    var copy = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                    copy.Freeze();
                    return copy;
                }
                finally
                {
                    NativeUi.DestroyIcon(icon);
                }
            }
            finally
            {
                Marshal.Release(list);
            }
        }

        private static bool IsSmallInside(BitmapSource image)
        {
            int w = image.PixelWidth, h = image.PixelHeight;
            var pixels = new byte[w * h * 4];
            image.CopyPixels(pixels, w * 4, 0);
            int maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (pixels[(y * w + x) * 4 + 3] > 8)
                    {
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            return maxX < w / 3 && maxY < h / 3;
        }

        private static BitmapSource CropToContent(BitmapSource image)
        {
            int w = image.PixelWidth, h = image.PixelHeight;
            var pixels = new byte[w * h * 4];
            image.CopyPixels(pixels, w * 4, 0);
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (pixels[(y * w + x) * 4 + 3] <= 8)
                        continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0)
                return image;
            int cw = maxX - minX + 1, ch = maxY - minY + 1;
            int side = Math.Max(cw, ch);
            if (side >= w - 2 && side >= h - 2)
                return image;

            var output = new byte[side * side * 4];
            int offX = (side - cw) / 2, offY = (side - ch) / 2;
            for (int y = 0; y < ch; y++)
                Buffer.BlockCopy(pixels, ((minY + y) * w + minX) * 4, output, ((offY + y) * side + offX) * 4, cw * 4);
            BitmapSource result = BitmapSource.Create(side, side, 96, 96, PixelFormats.Bgra32, null, output, side * 4);
            result.Freeze();
            return result;
        }

        private string CacheFile(string exePath)
        {
            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(exePath.ToLowerInvariant()));
                var sb = new StringBuilder();
                for (int i = 0; i < 10; i++)
                    sb.Append(hash[i].ToString("x2"));
                return Path.Combine(_cacheDir, sb + "-2.png");
            }
        }

        private static void WriteCache(string file, byte[] png)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temp = file + ".tmp";
            File.WriteAllBytes(temp, png);
            if (File.Exists(file))
                File.Delete(file);
            File.Move(temp, file);
        }

        private static ImageSource Decode(string file)
        {
            try
            {
                return File.Exists(file) ? Decode(File.ReadAllBytes(file)) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static ImageSource Decode(byte[] png)
        {
            try
            {
                var image = new BitmapImage();
                using (var stream = new MemoryStream(png))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                }
                image.Freeze();
                return image;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool SafeExists(string path)
        {
            try
            {
                return path.Length > 0 && File.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static DateTime SafeWriteTime(string path)
        {
            try
            {
                return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
        }
    }
}
