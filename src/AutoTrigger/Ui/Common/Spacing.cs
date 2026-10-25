using System.Windows;

namespace AutoTrigger.Ui
{
    // The spacing scale of the window. Border widths and the few values off the scale, used to align text
    // with a control next to it, stay as plain numbers where they are used.
    internal static class Spacing
    {
        public const double Tight = 4;
        public const double Small = 8;
        public const double Medium = 12;
        public const double Large = 16;
        public const double Field = 18;
        public const double Section = 24;
        public const double Page = 32;

        public static readonly Thickness PageMargin = new Thickness(Page, Section, Page, Section);
        public static readonly Thickness PageLeftColumn = new Thickness(Page, Section, 0, Section);
        public static readonly Thickness PageRightColumn = new Thickness(0, Section, Page, Section);
        public static readonly Thickness Panel = new Thickness(Section, 20, Section, 20);
        public static readonly Thickness ListInset = new Thickness(Tight);
        public static readonly Thickness ListRow = new Thickness(Medium, 6, Medium, 6);

        public static Thickness Top(double size) => new Thickness(0, size, 0, 0);

        public static Thickness Left(double size) => new Thickness(size, 0, 0, 0);

        public static Thickness Right(double size) => new Thickness(0, 0, size, 0);
    }
}
