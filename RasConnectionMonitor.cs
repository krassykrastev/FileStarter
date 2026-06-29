using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace TeamsTrayStarter
{
    internal sealed class RasConnectionMonitor : IDisposable
    {
        private const int RASCN_Connection = 0x00000001;
        private const int RASCN_Disconnection = 0x00000002;
        private static readonly IntPtr InvalidHandleValue = new(-1);

        private readonly EventWaitHandle _rasEvent;
        private readonly Action _onConnectionChanged;
        private RegisteredWaitHandle? _registeredWaitHandle;
        private bool _disposed;

        public RasConnectionMonitor(Action onConnectionChanged)
        {
            _onConnectionChanged = onConnectionChanged ?? throw new ArgumentNullException(nameof(onConnectionChanged));
            _rasEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
        }

        public bool Start()
        {
            if (_disposed)
                return false;

            int result = RasConnectionNotification(
                InvalidHandleValue,
                _rasEvent.SafeWaitHandle,
                RASCN_Connection | RASCN_Disconnection);

            if (result != 0)
            {
                Logger.Other($"Failed to register RAS connection notifications. Error code: {result}");
                return false;
            }

            _registeredWaitHandle = ThreadPool.RegisterWaitForSingleObject(
                _rasEvent,
                (_, timedOut) =>
                {
                    if (!timedOut && !_disposed)
                    {
                        _onConnectionChanged();
                    }
                },
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);

            Logger.Change("RAS VPN connection monitor started");
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _registeredWaitHandle?.Unregister(null);
            _rasEvent.Dispose();
        }

        [DllImport("rasapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RasConnectionNotification(
            IntPtr hrasconn,
            SafeWaitHandle hEvent,
            int dwFlags);
    }
}