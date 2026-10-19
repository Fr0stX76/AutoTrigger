using System;
using System.Windows;

namespace AutoTrigger.Ui
{
    internal static class DialogFit
    {
        public static void Fit(Window window, bool keepCentered)
        {
            if (!(window.Content is FrameworkElement content) || double.IsNaN(window.Width))
                return;
            content.Measure(new Size(window.Width, double.PositiveInfinity));
            double height = Math.Ceiling(content.DesiredSize.Height);
            if (height <= 0 || Math.Abs(height - window.Height) < 0.5)
                return;
            double old = double.IsNaN(window.Height) ? height : window.Height;
            window.Height = height;
            if (keepCentered && !double.IsNaN(window.Top))
                window.Top += (old - height) / 2;
            WindowPlacement.KeepOnScreen(window);
        }
    }
}
