namespace AutoTrigger.Core.Engine
{
    public static class CommandLine
    {
        public static string ArgumentsOf(string commandLine)
        {
            if (string.IsNullOrEmpty(commandLine))
                return string.Empty;
            int i = 0;
            int n = commandLine.Length;
            while (i < n && char.IsWhiteSpace(commandLine[i]))
                i++;
            if (i < n && commandLine[i] == '"')
            {
                int close = commandLine.IndexOf('"', i + 1);
                i = close < 0 ? n : close + 1;
            }
            while (i < n && !char.IsWhiteSpace(commandLine[i]))
                i++;
            return commandLine.Substring(i).Trim();
        }
    }
}
