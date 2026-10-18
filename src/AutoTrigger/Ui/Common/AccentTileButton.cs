using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AutoTrigger.Ui
{
    internal class AccentTileButton : Button
    {
        private const double SoftOpacity = 0.14;

        private static readonly Style FilledFocus = Res.FocusOutline(new CornerRadius(4));
        private static readonly Style OutlineFocus = Res.FocusOutline(new CornerRadius(6));

        public AccentTileButton(double width, double height, string glyph, double glyphWidth, double glyphHeight, bool filled)
        {
            Width = width;
            Height = height;
            MinWidth = 0;
            MinHeight = 0;
            Padding = new Thickness(0);
            FocusVisualStyle = filled ? FilledFocus : OutlineFocus;
            Template = BuildTemplate(glyph, glyphWidth, glyphHeight, filled);
        }

        private static ControlTemplate BuildTemplate(string data, double glyphWidth, double glyphHeight, bool filled)
        {
            DependencyProperty paint = filled ? Shape.FillProperty : Shape.StrokeProperty;
            var root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            var background = new FrameworkElementFactory(typeof(Border), "bg");
            background.SetValue(Border.CornerRadiusProperty, new CornerRadius(filled ? 4 : 6));
            background.SetResourceReference(Border.BackgroundProperty, Res.AccentFill);
            background.SetValue(UIElement.OpacityProperty, SoftOpacity);
            root.AppendChild(background);

            var glyph = new FrameworkElementFactory(typeof(Path), "glyph");
            glyph.SetValue(Path.DataProperty, Geometry.Parse(data));
            glyph.SetValue(FrameworkElement.WidthProperty, glyphWidth);
            glyph.SetValue(FrameworkElement.HeightProperty, glyphHeight);
            glyph.SetValue(Shape.StretchProperty, Stretch.Fill);
            glyph.SetValue(Shape.StrokeThicknessProperty, filled ? 0.0 : 1.5);
            glyph.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            glyph.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            glyph.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            glyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            glyph.SetValue(FrameworkElement.MarginProperty, new Thickness(filled ? 2 : 0, 0, 0, 0));
            glyph.SetResourceReference(paint, Res.AccentFill);
            root.AppendChild(glyph);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = root };

            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "bg"));
            hover.Setters.Add(new Setter(paint, new DynamicResourceExtension(Res.TextOnAccent), "glyph"));
            var pressed = new Trigger { Property = IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.8, "bg"));
            template.Triggers.Add(hover);
            template.Triggers.Add(pressed);
            return template;
        }
    }

    internal sealed class LaunchButton : AccentTileButton
    {
        public LaunchButton()
            : base(36, 32, "M0,0 L10,6 L0,12 Z", 10, 12, true)
        {
        }
    }

    internal sealed class AddTileButton : AccentTileButton
    {
        public AddTileButton(double height)
            : base(40, height, "M6,0 L6,12 M0,6 L12,6", 12, 12, false)
        {
        }
    }
}
