using System.Windows;
using System.Windows.Controls;

namespace AutoTrigger.Ui
{
    internal static class Res
    {
        public const string TextPrimary = "TextFillColorPrimaryBrush";
        public const string AccentFill = "AccentFillColorDefaultBrush";
        public const string TextOnAccent = "TextOnAccentFillColorPrimaryBrush";
        public const string Success = "SystemFillColorSuccessBrush";
        public const string Layer = "LayerFillColorDefaultBrush";
        public const string CardBackground = "CardBackgroundFillColorDefaultBrush";
        public const string CardStroke = "CardStrokeColorDefaultBrush";
        public const string Divider = "DividerStrokeColorDefaultBrush";
        public const string SubtleHover = "SubtleFillColorSecondaryBrush";
        public const string SubtlePressed = "SubtleFillColorTertiaryBrush";
        public const string ControlFill = "ControlFillColorSecondaryBrush";
        public const string ControlFillDefault = "ControlFillColorDefaultBrush";
        public const string Critical = "SystemFillColorCriticalBrush";

        public static Style FocusOutline(CornerRadius radius)
        {
            var outline = new FrameworkElementFactory(typeof(Border));
            outline.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            outline.SetValue(Border.CornerRadiusProperty, radius);
            outline.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            outline.SetResourceReference(Border.BorderBrushProperty, TextPrimary);
            var style = new Style(typeof(Control));
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Control)) { VisualTree = outline }));
            style.Seal();
            return style;
        }

        public static T Bind<T>(this T element, DependencyProperty property, string key) where T : FrameworkElement
        {
            element.SetResourceReference(property, key);
            return element;
        }

        public static TextBlock Text(string text, double size = 14, FontWeight? weight = null, string brush = TextPrimary)
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = weight ?? FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            block.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return block;
        }

        public static Border Card(UIElement child, Thickness padding)
        {
            var card = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = padding,
                Child = child,
            };
            card.SetResourceReference(Border.BackgroundProperty, CardBackground);
            card.SetResourceReference(Border.BorderBrushProperty, CardStroke);
            return card;
        }
    }
}
