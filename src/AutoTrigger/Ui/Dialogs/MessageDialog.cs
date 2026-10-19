using System.Windows;
using System.Windows.Controls;

namespace AutoTrigger.Ui
{
    internal enum DialogChoice
    {
        None,
        Primary,
        Secondary,
        Extra,
    }

    internal sealed class MessageDialog : DialogWindow
    {
        private DialogChoice _choice;

        private MessageDialog(string title, string text, string primary, string secondary, string extra)
            : base(title, title, 480)
        {
            var body = Res.Text(text, 14);
            body.TextWrapping = TextWrapping.Wrap;
            body.TextTrimming = TextTrimming.None;
            SetBody(body);

            UIElement left = null;
            if (!string.IsNullOrEmpty(extra))
            {
                var third = FooterButton(extra);
                third.Click += (s, e) => Finish(DialogChoice.Extra);
                left = third;
            }
            var first = FooterButton(primary, true);
            first.Click += (s, e) => Finish(DialogChoice.Primary);
            if (string.IsNullOrEmpty(secondary))
            {
                SetFooter(left, first);
                return;
            }
            var second = FooterButton(secondary);
            second.IsCancel = true;
            second.Click += (s, e) => Finish(DialogChoice.Secondary);
            SetFooter(left, second, first);
        }

        private void Finish(DialogChoice choice)
        {
            _choice = choice;
            Close();
        }

        public static DialogChoice Show(Window owner, string title, string text, string primary, string secondary = null,
            string extra = null)
        {
            var dialog = new MessageDialog(title, text, primary, secondary, extra);
            dialog.ShowOver(owner);
            return dialog._choice;
        }
    }
}
