using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using AutoTrigger.Core.Engine;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace AutoTrigger.Ui
{
    internal sealed class ProcessPickerDialog : FormDialog
    {
        private readonly List<RunningProgram> _programs;
        private readonly StackPanel _list = new StackPanel();
        private readonly TextBlock _error = ErrorLine();
        private ListRow _selectedRow;
        private string _selectedPath;

        public ProcessPickerDialog(IProcessSystem processes)
            : base(null, Texts.RunningPrograms, Texts.Select, 640)
        {
            _programs = ReadPrograms(processes);

            UiTextBox search = Kit.TextField(string.Empty, Filter, Texts.Search);
            search.Margin = new Thickness(0, 18, 0, 0);
            Body.Children.Add(search);

            var scroller = new ScrollViewer
            {
                Height = 380,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _list,
            };
            Border card = Res.Card(scroller, new Thickness(4));
            card.Margin = new Thickness(0, 12, 0, 0);
            Body.Children.Add(card);
            Body.Children.Add(_error);
            Filter(string.Empty);
            Loaded += (s, e) => search.Focus();
        }

        public string SelectedPath => _selectedPath;

        protected override bool TrySave()
        {
            if (string.IsNullOrEmpty(_selectedPath))
            {
                ShowError(_error, Texts.ChooseAProgram);
                return false;
            }
            return true;
        }

        private void Filter(string text)
        {
            _list.Children.Clear();
            _selectedRow = null;
            string query = (text ?? string.Empty).Trim();
            foreach (RunningProgram program in _programs)
            {
                if (query.Length > 0 && program.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0
                    && program.Path.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0)
                    continue;
                var lines = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
                lines.Children.Add(Res.Text(program.Name, 14, FontWeights.SemiBold));
                lines.Children.Add(Kit.Caption(program.Path));
                var row = new ListRow(lines, new Thickness(12, 6, 12, 6));
                string path = program.Path;
                row.Clicked += () => Choose(row, path);
                row.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount == 2)
                        Accept();
                };
                _list.Children.Add(row);
            }
            if (_list.Children.Count == 0)
            {
                TextBlock none = Kit.Caption(Texts.NothingFound);
                none.Margin = new Thickness(12);
                _list.Children.Add(none);
            }
        }

        private void Choose(ListRow row, string path)
        {
            if (_selectedRow != null)
                _selectedRow.IsSelected = false;
            _selectedRow = row;
            row.IsSelected = true;
            _selectedPath = path;
            ShowError(_error, null);
        }

        private static List<RunningProgram> ReadPrograms(IProcessSystem processes)
        {
            int self;
            using (Process current = Process.GetCurrentProcess())
                self = current.Id;
            try
            {
                return RunningPrograms.Read(processes, self, Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            }
            catch (Exception ex)
            {
                AutoTrigger.Core.Logging.Log.Warning("Running programs cannot be listed", ex);
                return new List<RunningProgram>();
            }
        }
    }
}
