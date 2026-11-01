using System;

namespace AutoTrigger.Core.Text
{
    // A message shown in the window and written to the log: the window gets the active language, the log English.
    public sealed class Note
    {
        private readonly Func<string> _render;

        public Note(Func<string> render)
        {
            _render = render ?? throw new ArgumentNullException(nameof(render));
        }

        public string Text => _render();

        public string English => TextCatalog.InEnglish(_render);

        public override string ToString() => English;
    }
}
