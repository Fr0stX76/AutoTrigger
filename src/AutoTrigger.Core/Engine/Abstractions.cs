using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AutoTrigger.Core.Engine
{
    public struct ProcessEntry
    {
        public ProcessEntry(int pid, string exeName)
        {
            Pid = pid;
            ExeName = exeName;
        }

        public int Pid { get; }

        public string ExeName { get; }
    }

    public sealed class ProcessDetails
    {
        public int Pid { get; set; }

        public string Path { get; set; }

        public long CreationTime { get; set; }
    }

    public sealed class StartedProgram
    {
        public int Pid { get; set; }

        public Task Exited { get; set; }
    }

    public interface IProcessSystem
    {
        void Snapshot(List<ProcessEntry> into);

        ProcessDetails Query(int pid);

        IDisposable WatchExit(int pid, long creationTime, Action onExit);

        bool IsRunning(int pid, long creationTime);

        StartedProgram Start(string path, string arguments, string workingDirectory, bool followExit);

        bool RequestClose(int pid);

        void Kill(int pid);

        string GetCommandLine(int pid);

        void WindowOwners(HashSet<int> into);
    }

    public interface IForegroundSource
    {
        event Action Changed;

        int GetForegroundProcessId();
    }

    public static class ForegroundIds
    {
        public const int EmptyFrame = -1;
    }

    public interface IEngineScheduler
    {
        void Post(Action action);

        IDisposable Schedule(int delayMs, Action action);

        long NowMs { get; }
    }
}
