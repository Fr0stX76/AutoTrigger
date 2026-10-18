using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoTrigger.Ui
{
    internal sealed class AppIconBox : Grid
    {
        private readonly Image _image;
        private readonly Border _tile;
        private readonly TextBlock _initials;

        public AppIconBox(double size, double radius)
        {
            Width = size;
            Height = size;
            _initials = Res.Text(string.Empty, size >= 40 ? 14 : 12, FontWeights.SemiBold, Res.TextPrimary);
            _initials.HorizontalAlignment = HorizontalAlignment.Center;
            _initials.TextTrimming = TextTrimming.None;
            _tile = new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1), Child = _initials };
            _tile.SetResourceReference(Border.BackgroundProperty, Res.ControlFill);
            _tile.SetResourceReference(Border.BorderBrushProperty, Res.CardStroke);
            _image = new Image
            {
                Width = size - 4,
                Height = size - 4,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            Children.Add(_tile);
            Children.Add(_image);
        }

        public void ShowInitials(string name)
        {
            _initials.Text = Initials(name);
            _tile.Visibility = Visibility.Visible;
            _image.Visibility = Visibility.Collapsed;
        }

        public void ShowImage(ImageSource image)
        {
            if (image == null)
                return;
            _image.Source = image;
            _image.Visibility = Visibility.Visible;
            _tile.Visibility = Visibility.Collapsed;
        }

        private static string Initials(string name)
        {
            string[] words = (name ?? string.Empty).Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return "?";
            if (words.Length == 1)
                return words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant();
            return (words[0].Substring(0, 1) + words[1].Substring(0, 1)).ToUpperInvariant();
        }
    }
}
