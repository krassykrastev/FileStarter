using System;
using System.Threading;
using System.Windows.Forms;

namespace TeamsTrayStarter
{
    internal static class Program
    {
        private const string SingleInstanceMutexName = @"Global\FileStarter_SingleInstance_Mutex";
        private static Mutex? _singleInstanceMutex;

        [STAThread]
        static void Main()
        {
            bool createdNew;

            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: SingleInstanceMutexName,
                createdNew: out createdNew);

            if (!createdNew)
            {
                // Another instance is already running
                return;
            }

            try
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new TrayAppContext());
            }
            finally
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                catch
                {
                    // Ignore if mutex was already released
                }

                _singleInstanceMutex?.Dispose();
            }
        }
    }
}