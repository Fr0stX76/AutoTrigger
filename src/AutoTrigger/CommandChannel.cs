using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AutoTrigger.Core.Logging;

namespace AutoTrigger
{
    internal enum CommandKind
    {
        None,
        Pause,
        Resume,
        Toggle,
        Profile,
        Invalid,
    }

    internal sealed class Command
    {
        public CommandKind Kind { get; private set; }

        public string ProfileName { get; private set; } = string.Empty;

        public static Command Parse(string[] args)
        {
            if (args == null || args.Length == 0 || !(args[0].StartsWith("/", StringComparison.Ordinal) || args[0].StartsWith("-", StringComparison.Ordinal)))
                return new Command { Kind = CommandKind.None };
            string verb = args[0].TrimStart('/', '-').ToLowerInvariant();
            string rest = string.Join(" ", args.Skip(1)).Trim();
            return FromParts(verb, rest);
        }

        public static Command FromWire(string line)
        {
            string text = (line ?? string.Empty).Trim();
            int space = text.IndexOf(' ');
            return space < 0 ? FromParts(text.ToLowerInvariant(), string.Empty)
                : FromParts(text.Substring(0, space).ToLowerInvariant(), text.Substring(space + 1).Trim());
        }

        private static Command FromParts(string verb, string rest)
        {
            switch (verb)
            {
                case "pause": return new Command { Kind = CommandKind.Pause };
                case "resume": return new Command { Kind = CommandKind.Resume };
                case "toggle": return new Command { Kind = CommandKind.Toggle };
                case "profile" when rest.Length > 0: return new Command { Kind = CommandKind.Profile, ProfileName = rest };
                default: return new Command { Kind = CommandKind.Invalid };
            }
        }

        public string ToWire() => Kind == CommandKind.Profile ? "profile " + ProfileName : Kind.ToString().ToLowerInvariant();
    }

    internal static class CommandWire
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public static StreamReader Reader(Stream pipe) => new StreamReader(pipe, Utf8, false, 1024, true);

        public static byte[] Line(string text) => Utf8.GetBytes(text + "\r\n");
    }

    internal sealed class CommandServer : IDisposable
    {
        public const string Ok = "ok";
        public const string UnknownCommand = "unknown command";
        public const string Failed = "failed";
        public const string NoAnswer = "no answer";
        public const int MaxInstances = 4;
        private const int DrainTimeoutMs = 2000;

        private readonly string _pipeName;
        private readonly Func<Command, Task<string>> _handle;
        private readonly Func<string, NamedPipeServerStream> _createPipe;
        private readonly int _readTimeoutMs;
        private readonly int _replyTimeoutMs;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        public CommandServer(string pipeName, Func<Command, Task<string>> handle, Func<string, NamedPipeServerStream> createPipe = null,
            int readTimeoutMs = 5000, int replyTimeoutMs = 10000)
        {
            _pipeName = pipeName;
            _handle = handle;
            _createPipe = createPipe ?? CreatePipe;
            _readTimeoutMs = readTimeoutMs;
            _replyTimeoutMs = replyTimeoutMs;
            Task.Run(ListenAsync);
        }

        private static NamedPipeServerStream CreatePipe(string name) =>
            new NamedPipeServerStream(name, PipeDirection.InOut, MaxInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        private async Task ListenAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    using (NamedPipeServerStream pipe = _createPipe(_pipeName))
                    {
                        await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                        await ServeAsync(pipe).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning("Command line request failed: " + ex.Message);
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
        }

        private async Task ServeAsync(NamedPipeServerStream pipe)
        {
            string line;
            using (StreamReader reader = CommandWire.Reader(pipe))
            {
                Task<string> read = reader.ReadLineAsync();
                if (!await Within(read, _readTimeoutMs).ConfigureAwait(false))
                {
                    _ = read.ContinueWith(t => t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    Log.Warning($"Command line request ignored: nothing received within {_readTimeoutMs / 1000} s");
                    return;
                }
                line = await read.ConfigureAwait(false);
            }
            Command command = Command.FromWire(line);
            Log.Info($"Command line: {command.ToWire()}");
            string reply = command.Kind == CommandKind.Invalid || command.Kind == CommandKind.None
                ? UnknownCommand
                : await AnswerAsync(command).ConfigureAwait(false);
            byte[] bytes = CommandWire.Line(reply);
            await pipe.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            await Within(Task.Run(() => Drain(pipe)), DrainTimeoutMs).ConfigureAwait(false);
        }

        private async Task<string> AnswerAsync(Command command)
        {
            var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _ = _handle(command).ContinueWith(t => answer.TrySetResult(t.Status == TaskStatus.RanToCompletion ? t.Result : Failed),
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                Log.Error("Command line request failed", ex);
                return Failed;
            }
            if (await Within(answer.Task, _replyTimeoutMs).ConfigureAwait(false))
                return answer.Task.Result;
            Log.Warning($"Command line: no answer within {_replyTimeoutMs / 1000} s");
            return NoAnswer;
        }

        private static async Task<bool> Within(Task task, int timeoutMs)
        {
            using (var timer = new CancellationTokenSource())
            {
                Task first = await Task.WhenAny(task, Task.Delay(timeoutMs, timer.Token)).ConfigureAwait(false);
                timer.Cancel();
                return first == task;
            }
        }

        private static void Drain(PipeStream pipe)
        {
            try
            {
                pipe.WaitForPipeDrain();
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is PlatformNotSupportedException)
            {
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
        }
    }

    internal static class CommandChannel
    {
        private const int SendTimeoutMs = 10000;
        private const int ReplyTimeoutMs = 15000;

        public static string PipeName
        {
            get
            {
                using (Process self = Process.GetCurrentProcess())
                    return @"AutoTrigger.7f3c1d2e.Commands." + self.SessionId;
            }
        }

        public static int Send(Command command, string pipeName = null)
        {
            var clock = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    using (var pipe = new NamedPipeClientStream(".", pipeName ?? PipeName, PipeDirection.InOut))
                    {
                        pipe.Connect(1000);
                        byte[] bytes = CommandWire.Line(command.ToWire());
                        pipe.Write(bytes, 0, bytes.Length);
                        using (StreamReader reader = CommandWire.Reader(pipe))
                        {
                            Task<string> reply = reader.ReadLineAsync();
                            try
                            {
                                if (!reply.Wait(ReplyTimeoutMs))
                                    return 1;
                            }
                            catch (AggregateException)
                            {
                                return 1;
                            }
                            return reply.Result == CommandServer.Ok ? 0 : 1;
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    return 1;
                }
                catch (Exception ex) when (ex is TimeoutException || ex is IOException)
                {
                    if (clock.ElapsedMilliseconds > SendTimeoutMs)
                        return 1;
                    Thread.Sleep(250);
                }
            }
        }
    }
}
