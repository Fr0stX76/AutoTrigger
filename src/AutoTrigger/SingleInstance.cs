using System;
using System.Threading;
using AutoTrigger.Ui;

namespace AutoTrigger
{
    internal sealed class SingleInstance : IDisposable
    {
        private const string MutexName = @"Local\AutoTrigger.7f3c1d2e.Instance";
        private const string EventName = @"Local\AutoTrigger.7f3c1d2e.Activate";

        private readonly Mutex _mutex;
        private readonly EventWaitHandle _activate;
        private RegisteredWaitHandle _registration;

        private SingleInstance(Mutex mutex, EventWaitHandle activate)
        {
            _mutex = mutex;
            _activate = activate;
        }

        public static SingleInstance TryAcquire()
        {
            Mutex mutex = null;
            EventWaitHandle activate = null;
            try
            {
                bool createdNew;
                try
                {
                    mutex = new Mutex(true, MutexName, out createdNew);
                }
                catch (UnauthorizedAccessException)
                {
                    return null;
                }
                if (!createdNew)
                    return null;
                activate = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
                var instance = new SingleInstance(mutex, activate);
                mutex = null;
                activate = null;
                return instance;
            }
            finally
            {
                activate?.Dispose();
                mutex?.Dispose();
            }
        }

        public static void ActivateRunningInstance()
        {
            try
            {
                NativeUi.AllowSetForegroundWindow(NativeUi.ASFW_ANY);
                using (EventWaitHandle activate = EventWaitHandle.OpenExisting(EventName))
                    activate.Set();
            }
            catch (Exception)
            {
            }
        }

        public void Listen(Action onActivate)
        {
            _registration = ThreadPool.RegisterWaitForSingleObject(_activate, (state, timedOut) => onActivate(), null, Timeout.Infinite, false);
        }

        public void Dispose()
        {
            _registration?.Unregister(null);
            _activate.Dispose();
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
            _mutex.Dispose();
        }
    }
}
