using System;
using Microsoft.Win32;

namespace TeamsTrayStarter
{
    public static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "FileStarter";

        public static void EnableRunAtLogin()
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
                throw new InvalidOperationException("Cannot determine executable path.");

            var command = $"\"{exePath}\"";

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                          ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (key == null)
                throw new InvalidOperationException("Cannot open HKCU Run key.");

            key.SetValue(ValueName, command, RegistryValueKind.String);
        }

        public static void DisableRunAtLogin()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}