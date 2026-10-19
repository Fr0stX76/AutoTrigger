using System.Windows;
using System.Windows.Controls;

namespace AutoTrigger.Ui
{
    internal abstract class FormDialog : DialogWindow
    {
        private bool _saved;

        protected FormDialog(string context, string title, string primaryText, double width = 600)
            : base(title, Texts.AppName, width)
        {
            var content = new StackPanel();
            if (!string.IsNullOrEmpty(context))
                content.Children.Add(Kit.Caption(context));
            var heading = Kit.Heading(title);
            heading.Margin = new Thickness(0, 2, 0, 0);
            content.Children.Add(heading);
            Body = new StackPanel();
            content.Children.Add(Body);
            SetBody(content);

            var cancel = FooterButton(Texts.Cancel);
            cancel.IsCancel = true;
            var primary = FooterButton(primaryText, true);
            primary.Click += (s, e) => Accept();
            SetFooter(null, cancel, primary);

            Body.SizeChanged += (s, e) =>
            {
                if (IsLoaded)
                    DialogFit.Fit(this, false);
            };
        }

        protected StackPanel Body { get; }

        protected void Accept()
        {
            if (!TrySave())
                return;
            _saved = true;
            Close();
        }

        protected abstract bool TrySave();

        public bool Run(Window owner)
        {
            ShowOver(owner);
            return _saved;
        }

        protected static TextBlock ErrorLine()
        {
            var line = Kit.Wrapped(string.Empty, 12, Res.Critical);
            line.Margin = new Thickness(0, 6, 0, 0);
            line.Visibility = Visibility.Collapsed;
            return line;
        }

        protected static void ShowError(TextBlock line, string message)
        {
            line.Text = message ?? string.Empty;
            line.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
