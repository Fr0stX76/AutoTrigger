using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AutoTrigger.Ui
{
    internal static class DropDowns
    {
        private const double EightItemsHeight = 8 * 40 + 4;
        private const double PopupChrome = 48;
        private const double SmallestList = 120;

        public static void Attach(ComboBox combo)
        {
            bool openedByPress = false;
            Point pressedAt = default;

            combo.Loaded += (s, e) => PlaceItself(combo);
            combo.DropDownOpened += (s, e) =>
            {
                PlaceItself(combo);
                combo.MaxDropDownHeight = FittedHeight(combo, EightItemsHeight);
            };
            combo.PreviewMouseLeftButtonDown += (s, e) =>
            {
                openedByPress = !combo.IsDropDownOpen;
                pressedAt = e.GetPosition(combo);
            };
            combo.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (!openedByPress)
                    return;
                openedByPress = false;
                if (combo.IsDropDownOpen && IsInItem(e.OriginalSource as DependencyObject) && !Moved(pressedAt, e.GetPosition(combo)))
                    e.Handled = true;
            };
        }

        private static void PlaceItself(ComboBox combo)
        {
            combo.ApplyTemplate();
            Popup popup = combo.Template?.FindName("PART_Popup", combo) as Popup ?? combo.Template?.FindName("Popup", combo) as Popup;
            if (popup == null || popup.Placement == PlacementMode.Custom)
                return;
            popup.Placement = PlacementMode.Custom;
            popup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) => Placements(combo, popup, popupSize, targetSize, offset);
        }

        private static CustomPopupPlacement[] Placements(ComboBox combo, Popup popup, Size popupSize, Size targetSize, Point offset)
        {
            var child = popup.Child as FrameworkElement;
            double unit = child != null && child.ActualHeight > 0 ? popupSize.Height / child.ActualHeight : 1;
            Thickness margin = child?.Margin ?? default;
            double left = -margin.Left * unit;
            double gap = offset.Y * unit;
            var below = new CustomPopupPlacement(new Point(left, targetSize.Height), PopupPrimaryAxis.Horizontal);
            if (!TryRoom(combo, out double roomBelow, out double roomAbove, out double screenBelow)
                || (popupSize.Height + gap <= roomBelow * unit && popupSize.Height + gap + margin.Bottom * unit <= screenBelow * unit)
                || roomBelow >= roomAbove)
                return new[] { below };
            return new[] { new CustomPopupPlacement(new Point(left, -popupSize.Height - 2 * gap), PopupPrimaryAxis.Horizontal) };
        }

        private static double FittedHeight(ComboBox combo, double normal)
        {
            if (!TryRoom(combo, out double below, out double above, out _))
                return normal;
            double room = Math.Max(below, above) - PopupChrome;
            return Math.Max(SmallestList, Math.Min(normal, room));
        }

        private static bool TryRoom(ComboBox combo, out double below, out double above, out double screenBelow)
        {
            below = 0;
            above = 0;
            screenBelow = 0;
            PresentationSource source = PresentationSource.FromVisual(combo);
            if (source?.CompositionTarget == null || combo.ActualHeight <= 0)
                return false;
            Point top = combo.PointToScreen(new Point(0, 0));
            Point bottom = combo.PointToScreen(new Point(0, combo.ActualHeight));
            var where = new NativeUi.POINT { X = (int)top.X, Y = (int)top.Y };
            IntPtr monitor = NativeUi.MonitorFromPoint(where, NativeUi.MONITOR_DEFAULTTONEAREST);
            var info = new NativeUi.MONITORINFO { cbSize = Marshal.SizeOf<NativeUi.MONITORINFO>() };
            if (monitor == IntPtr.Zero || !NativeUi.GetMonitorInfo(monitor, ref info))
                return false;
            double scale = source.CompositionTarget.TransformToDevice.M22;
            below = (info.rcWork.Bottom - bottom.Y) / scale;
            above = (top.Y - info.rcWork.Top) / scale;
            screenBelow = (info.rcMonitor.Bottom - bottom.Y) / scale;
            return true;
        }

        private static bool IsInItem(DependencyObject element)
        {
            while (element != null)
            {
                if (element is ComboBoxItem)
                    return true;
                if (element is ComboBox)
                    return false;
                element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
            }
            return false;
        }

        private static bool Moved(Point from, Point to) =>
            Math.Abs(to.X - from.X) > SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(to.Y - from.Y) > SystemParameters.MinimumVerticalDragDistance;
    }
}
