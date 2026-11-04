using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AutoTrigger.Ui
{
    // A text that ends with an ellipsis shows in full on hover, whatever the language; a text that fits shows nothing.
    // Texts get the marker when they are built, so a tooltip set by a page is never replaced.
    internal static class CutText
    {
        public static readonly object Marker = new FullText(string.Empty);

        public static void Register()
        {
            var opening = new ToolTipEventHandler(Opening);
            EventManager.RegisterClassHandler(typeof(TextBlock), FrameworkElement.ToolTipOpeningEvent, opening);
            EventManager.RegisterClassHandler(typeof(ComboBox), FrameworkElement.ToolTipOpeningEvent, opening);
        }

        private static void Opening(object sender, ToolTipEventArgs e)
        {
            var owner = (FrameworkElement)sender;
            if (!(owner.ToolTip is FullText))
                return;
            TextBlock text = owner as TextBlock ?? MarkedText(owner);
            if (text != null && IsCut(text))
                owner.ToolTip = new FullText(text.Text);
            else
                e.Handled = true;
        }

        // In a combo box, the only marked text is its selection: the list opens in a popup, outside its visual tree.
        private static TextBlock MarkedText(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock block && block.ToolTip is FullText)
                    return block;
                TextBlock found = MarkedText(child);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static bool IsCut(TextBlock block)
        {
            if (block.TextTrimming == TextTrimming.None || block.TextWrapping != TextWrapping.NoWrap || string.IsNullOrEmpty(block.Text))
                return false;
            var typeface = new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch);
            var formatted = new FormattedText(block.Text, CultureInfo.CurrentUICulture, block.FlowDirection, typeface,
                block.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(block).PixelsPerDip);
            // Layout rounding can make a text that fits up to a pixel narrower than measured.
            return formatted.Width > block.ActualWidth - block.Padding.Left - block.Padding.Right + 1;
        }

        // Not a string, so screen readers do not read every text twice as its help text.
        private sealed class FullText
        {
            private readonly string _text;

            public FullText(string text)
            {
                _text = text;
            }

            public override string ToString() => _text;
        }
    }
}
