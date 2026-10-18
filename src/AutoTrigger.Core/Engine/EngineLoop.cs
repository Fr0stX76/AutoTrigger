using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using AutoTrigger.Core.Logging;

namespace AutoTrigger.Core.Engine
{
    public sealed class EngineLoop : IEngineScheduler, IDisposable
    {
        private readonly object _gate = new object();
        private readonly Queue<Action> _posted = new Queue<Action>();
        private readonly List<ScheduledItem> _timers = new List<ScheduledItem>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Thread _thread;
        private bool _disposed;

        public EngineLoop()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "AutoTrigger engine" };
            _thread.Start();
        }

        public long NowMs => _clock.ElapsedMilliseconds;

        public bool IsEngineThread => Thread.CurrentThread == _thread;

        public void Post(Action action)
        {
            if (action == null)
                return;
            lock (_gate)
            {
                if (_disposed)
                    return;
                _posted.Enqueue(action);
                Monitor.Pulse(_gate);
            }
        }

        public IDisposable Schedule(int delayMs, Action action)
        {
            var item = new ScheduledItem(this, NowMs + Math.Max(0, delayMs), action);
            lock (_gate)
            {
                if (_disposed || action == null)
                    return item;
                int index = _timers.Count;
                while (index > 0 && _timers[index - 1].Due > item.Due)
                    index--;
                _timers.Insert(index, item);
                if (index == 0)
                    Monitor.Pulse(_gate);
            }
            return item;
        }

        private void Cancel(ScheduledItem item)
        {
            lock (_gate)
                _timers.Remove(item);
        }

        private void Run()
        {
            while (true)
            {
                Action next;
                lock (_gate)
                {
                    while (true)
                    {
                        if (_disposed)
                            return;
                        if (_posted.Count > 0)
                        {
                            next = _posted.Dequeue();
                            break;
                        }
                        long now = NowMs;
                        if (_timers.Count > 0 && _timers[0].Due <= now)
                        {
                            next = _timers[0].Action;
                            _timers.RemoveAt(0);
                            break;
                        }
                        int wait = _timers.Count == 0 ? Timeout.Infinite : (int)Math.Min(int.MaxValue, _timers[0].Due - now);
                        Monitor.Wait(_gate, wait);
                    }
                }
                try
                {
                    next();
                }
                catch (Exception ex)
                {
                    Log.Error("Unexpected error in the engine", ex);
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _posted.Clear();
                _timers.Clear();
                Monitor.PulseAll(_gate);
            }
            if (!IsEngineThread)
                _thread.Join(TimeSpan.FromSeconds(2));
        }

        private sealed class ScheduledItem : IDisposable
        {
            private readonly EngineLoop _loop;

            public ScheduledItem(EngineLoop loop, long due, Action action)
            {
                _loop = loop;
                Due = due;
                Action = action;
            }

            public long Due { get; }

            public Action Action { get; }

            public void Dispose() => _loop.Cancel(this);
        }
    }
}
