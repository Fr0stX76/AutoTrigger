using System;
using System.Windows.Controls;
using AutoTrigger.Core.Model;
using AutoTrigger.Core.Text;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace AutoTrigger.Ui
{
    // The name of a new group, or the new name of a group. A name another group has would join the two, so it is refused.
    internal sealed class GroupNameDialog : FormDialog
    {
        private readonly Func<string, bool> _taken;
        private readonly UiTextBox _name;
        private readonly TextBlock _error = ErrorLine();

        public GroupNameDialog(string title, string name, Func<string, bool> taken)
            : base(null, title, Texts.Ok, 440)
        {
            _taken = taken;
            _name = Kit.TextField(name, text => Validate());
            _name.MaxLength = ProfileGroups.MaxNameLength;
            Body.Children.Add(Kit.Field(Texts.Name, _name));
            Body.Children.Add(_error);
            Loaded += (s, e) =>
            {
                _name.Focus();
                _name.SelectAll();
            };
            Validate();
        }

        public string GroupName => ProfileGroups.Clean(_name.Text);

        protected override bool TrySave() => Validate();

        private bool Validate()
        {
            bool taken = _taken(_name.Text);
            ShowError(_error, taken ? Texts.GroupNameTaken : null);
            bool valid = !taken && GroupName.Length > 0;
            PrimaryButton.IsEnabled = valid;
            return valid;
        }
    }
}
