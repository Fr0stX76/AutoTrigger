namespace AutoTrigger.Ui
{
    internal sealed class HdrState
    {
        public static readonly HdrState Unknown = new HdrState();

        public bool Available { get; set; }

        public bool On { get; set; }
    }
}
