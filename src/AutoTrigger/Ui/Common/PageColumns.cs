using System.Windows;
using System.Windows.Controls;

namespace AutoTrigger.Ui
{
    // The frame of the pages in two columns, so they keep the same margins, headings and scrolling.
    // Each page sets its own column widths, which depend on what it shows.
    internal static class PageColumns
    {
        public static void Fill(Grid page, ColumnDefinition left, ColumnDefinition right, FrameworkElement leftColumn,
            FrameworkElement rightColumn)
        {
            page.ColumnDefinitions.Add(left);
            page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Spacing.Section) });
            page.ColumnDefinitions.Add(right);
            leftColumn.Margin = Spacing.PageLeftColumn;
            rightColumn.Margin = Spacing.PageRightColumn;
            Grid.SetColumn(rightColumn, 2);
            page.Children.Add(leftColumn);
            page.Children.Add(rightColumn);
        }

        // The heading stays in place. Content that has no scrolling of its own is given one here, under the heading,
        // so a long column never pushes the other one out of view. Content that is not given one loses its own margin.
        public static Grid Column(string heading, FrameworkElement content, bool scrolls)
        {
            var column = new Grid();
            column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            column.Children.Add(Kit.Heading(heading));

            FrameworkElement body = scrolls
                ? new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = content,
                }
                : content;
            body.Margin = Spacing.Top(Spacing.Medium);
            Grid.SetRow(body, 1);
            column.Children.Add(body);
            return column;
        }
    }
}
