using System.Globalization;

namespace AutoTrigger.Core.Model
{
    public static class CountText
    {
        // "1 display", "2 displays": every noun counted in AutoTrigger takes a plain "s".
        public static string Of(int count, string noun) =>
            count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? noun : noun + "s");
    }
}
