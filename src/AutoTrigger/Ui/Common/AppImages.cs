using System;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutoTrigger.Core.Logging;

namespace AutoTrigger.Ui
{
    internal static class AppImages
    {
        private const string IconUri = "pack://application:,,,/AutoTrigger;component/Assets/AutoTrigger.ico";
        private static BitmapFrame _window;
        private static BitmapSource _small;

        public static ImageSource WindowIcon
        {
            get
            {
                if (_window == null)
                {
                    try
                    {
                        _window = BitmapFrame.Create(new Uri(IconUri, UriKind.Absolute));
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("The window icon could not be loaded", ex);
                    }
                }
                return _window;
            }
        }

        public static ImageSource SmallIcon
        {
            get
            {
                if (_small == null)
                {
                    try
                    {
                        var decoder = BitmapDecoder.Create(new Uri(IconUri, UriKind.Absolute), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        _small = decoder.Frames
                            .OrderBy(f => Math.Abs(f.PixelWidth - 64))
                            .FirstOrDefault();
                        _small?.Freeze();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("The small icon could not be loaded", ex);
                    }
                }
                return _small;
            }
        }
    }
}
