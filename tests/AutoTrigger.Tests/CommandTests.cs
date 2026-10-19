using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using AutoTrigger.Core.Model;

namespace AutoTrigger.Tests
{
    public static partial class Runner
    {
        private static void ShortcutConflicts()
        {
            var s = new AppSettings();
            var overlay = new Profile { Name = "Overlay", Shortcut = new KeyCombination { Ctrl = true, Shift = true, Key = 0x44 } };
            var game = new Profile { Name = "Game" };
            game.OnBlur.Add(new PressHotkeyAction { Keys = new KeyCombination { Ctrl = true, Alt = true, Key = 0x31 } });
            s.Profiles.Add(overlay);
            s.Profiles.Add(game);

            Eq(overlay, ShortcutRules.OwnerOf(s, new KeyCombination { Ctrl = true, Shift = true, Key = 0x44 }), "owner found");
            Eq(null, ShortcutRules.OwnerOf(s, new KeyCombination { Ctrl = true, Shift = true, Key = 0x44 }, overlay.Id), "its own profile is not a conflict");
            Eq(null, ShortcutRules.OwnerOf(s, new KeyCombination { Ctrl = true, Key = 0x44 }), "other modifiers: no owner");
            Eq(game, ShortcutRules.SenderOf(s, new KeyCombination { Ctrl = true, Alt = true, Key = 0x31 }), "sent by a Hotkey action in Lost focus");
            Eq(null, ShortcutRules.SenderOf(s, new KeyCombination { Ctrl = true, Alt = true, Key = 0x32 }), "not sent");
            Eq(null, ShortcutRules.OwnerOf(s, null), "no keys: no owner");
            Eq(null, ShortcutRules.SenderOf(s, new KeyCombination()), "empty keys: no sender");
        }

        private static void CommandArguments()
        {
            Eq(CommandKind.None, Command.Parse(Array.Empty<string>()).Kind, "no arguments");
            Eq(CommandKind.None, Command.Parse(new[] { "game.exe" }).Kind, "not a command");
            Eq(CommandKind.Pause, Command.Parse(new[] { "/pause" }).Kind, "/pause");
            Eq(CommandKind.Resume, Command.Parse(new[] { "-RESUME" }).Kind, "-RESUME");
            Eq(CommandKind.Toggle, Command.Parse(new[] { "/toggle" }).Kind, "/toggle");
            Command profile = Command.Parse(new[] { "/profile", "My", "Game" });
            Check(profile.Kind == CommandKind.Profile && profile.ProfileName == "My Game", "/profile with an unquoted name");
            Eq(CommandKind.Invalid, Command.Parse(new[] { "/profile" }).Kind, "/profile without a name");
            Eq(CommandKind.Invalid, Command.Parse(new[] { "/bogus" }).Kind, "unknown verb");
            Command wire = Command.FromWire(profile.ToWire());
            Check(wire.Kind == CommandKind.Profile && wire.ProfileName == "My Game", "profile name kept through the pipe");
            Eq(CommandKind.Pause, Command.FromWire(Command.Parse(new[] { "/pause" }).ToWire()).Kind, "pause kept through the pipe");
        }

        private static void CommandPipe()
        {
            var never = new TaskCompletionSource<string>();
            int engineThread = 0;
            Func<Command, Task<string>> handle = command =>
            {
                if (command.Kind == CommandKind.Toggle)
                    return never.Task;
                if (command.Kind != CommandKind.Profile)
                    return Task.FromResult(CommandServer.Ok);
                var result = new TaskCompletionSource<string>();
                var engine = new Thread(() =>
                {
                    engineThread = Environment.CurrentManagedThreadId;
                    result.TrySetResult(command.ProfileName == "Known" ? CommandServer.Ok : "profile not found");
                });
                engine.Start();
                return result.Task;
            };

            string pipe = "AutoTrigger.7f3c1d2e.Tests." + Guid.NewGuid().ToString("N");
            using (new CommandServer(pipe, handle, null, 300, 300))
            {
                Eq(0, CommandChannel.Send(Command.Parse(new[] { "/profile", "Known" }), pipe), "known profile: exit code 0");
                Check(engineThread != 0, "answer given by another thread");
                Eq(1, CommandChannel.Send(Command.Parse(new[] { "/profile", "Missing" }), pipe), "missing profile: exit code 1");
                Eq(0, CommandChannel.Send(Command.Parse(new[] { "/pause" }), pipe), "pause: exit code 0");

                using (var silent = new NamedPipeClientStream(".", pipe, PipeDirection.InOut))
                {
                    silent.Connect(2000);
                    Thread.Sleep(600);
                    Eq(0, CommandChannel.Send(Command.Parse(new[] { "/resume" }), pipe), "a client that sends nothing is dropped; the next one is served");
                }

                var clock = System.Diagnostics.Stopwatch.StartNew();
                Eq(1, CommandChannel.Send(Command.Parse(new[] { "/toggle" }), pipe), "no answer: exit code 1");
                Check(clock.ElapsedMilliseconds < 5000, $"no answer: the client is released quickly ({clock.ElapsedMilliseconds} ms)");
            }
        }
    }
}
