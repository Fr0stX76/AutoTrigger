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

            var closeKeys = new KeyCombination { Ctrl = true, Alt = true, Key = 0x43 };
            overlay.ClosedShortcut = closeKeys.Copy();
            Eq(overlay, ShortcutRules.OwnerOf(s, closeKeys), "a Closed shortcut has an owner");
            Eq(null, ShortcutRules.OwnerOf(s, closeKeys, overlay.Id, EventKind.Closed), "its own Closed box is not a conflict");
            Eq(overlay, ShortcutRules.OwnerOf(s, closeKeys, overlay.Id, EventKind.Started), "its Started box cannot take the Closed keys");
            Eq(overlay, ShortcutRules.OwnerOf(s, new KeyCombination { Ctrl = true, Shift = true, Key = 0x44 }, overlay.Id, EventKind.Closed),
                "its Closed box cannot take the Started keys");
            game.ClosedShortcut = new KeyCombination { Alt = true, Key = 0x70 };
            var alpha = new Profile { Name = "alpha", Shortcut = new KeyCombination { Alt = true, Key = 0x71 } };
            s.Profiles.Add(alpha);
            List<ShortcutEntry> sorted = ShortcutRules.Sorted(s.Profiles);
            Eq(4, sorted.Count, "every shortcut listed once");
            Check(sorted[0].Profile == alpha && sorted[1].Profile == game && sorted[2].Profile == overlay && sorted[3].Profile == overlay,
                "sorted by profile name, case ignored");
            Check(sorted[2].Event == EventKind.Started && sorted[3].Event == EventKind.Closed, "Started before Closed");
            Eq(0, ShortcutRules.Sorted(new[] { new Profile { Name = "Empty" } }).Count, "no shortcut: empty list");
            ShortcutEntry taken = ShortcutRules.EntryOf(s, closeKeys, overlay.Id, EventKind.Started);
            Check(taken != null && taken.Profile == overlay && taken.Event == EventKind.Closed, "the conflict names the box that has the keys");
            var unusable = new Profile { Name = "Hand edited", Shortcut = new KeyCombination { Key = 0x41 }, ClosedShortcut = new KeyCombination { Ctrl = true, Key = 0x0FFFFF } };
            Eq(0, ShortcutRules.All(new[] { unusable }).Count, "shortcuts that cannot be registered are not listed");
        }

        private static void CommandArguments()
        {
            Eq(CommandKind.None, Command.Parse(Array.Empty<string>()).Kind, "no arguments");
            Eq(CommandKind.None, Command.Parse(new[] { "game.exe" }).Kind, "not a command");
            Eq(CommandKind.Pause, Command.Parse(new[] { "/pause" }).Kind, "/pause");
            Eq(CommandKind.Resume, Command.Parse(new[] { "-RESUME" }).Kind, "-RESUME");
            Eq(CommandKind.Toggle, Command.Parse(new[] { "/toggle" }).Kind, "/toggle");
            Command started = Command.Parse(new[] { "/started", "My", "Game" });
            Check(started.Kind == CommandKind.Started && started.ProfileName == "My Game" && started.Event == EventKind.Started && started.RunsProfile,
                "/started with an unquoted name");
            Command closed = Command.Parse(new[] { "-CLOSED", "My Game" });
            Check(closed.Kind == CommandKind.Closed && closed.ProfileName == "My Game" && closed.Event == EventKind.Closed && closed.RunsProfile,
                "-CLOSED with a quoted name");
            Eq(CommandKind.Invalid, Command.Parse(new[] { "/started" }).Kind, "/started without a name");
            Eq(CommandKind.Invalid, Command.Parse(new[] { "/closed" }).Kind, "/closed without a name");
            Eq(CommandKind.Invalid, Command.Parse(new[] { "/profile", "My Game" }).Kind, "/profile is no longer accepted");
            Eq(CommandKind.Invalid, Command.Parse(new[] { "/bogus" }).Kind, "unknown verb");
            Check(!Command.Parse(new[] { "/pause" }).RunsProfile, "/pause runs no profile");
            Command wire = Command.FromWire(started.ToWire());
            Check(wire.Kind == CommandKind.Started && wire.ProfileName == "My Game", "Started and its name kept through the pipe");
            wire = Command.FromWire(closed.ToWire());
            Check(wire.Kind == CommandKind.Closed && wire.ProfileName == "My Game", "Closed and its name kept through the pipe");
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
                if (!command.RunsProfile)
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
                Eq(0, CommandChannel.Send(Command.Parse(new[] { "/started", "Known" }), pipe), "known profile: exit code 0");
                Eq(0, CommandChannel.Send(Command.Parse(new[] { "/closed", "Known" }), pipe), "known profile, Closed: exit code 0");
                Check(engineThread != 0, "answer given by another thread");
                Eq(1, CommandChannel.Send(Command.Parse(new[] { "/started", "Missing" }), pipe), "missing profile: exit code 1");
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
