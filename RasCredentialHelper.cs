using System;
using System.Runtime.InteropServices;

namespace TeamsTrayStarter
{
    internal static class RasCredentialHelper
    {
        private const int RASCM_UserName = 0x00000001;
        private const int RASCM_Password = 0x00000002;
        private const int RASCM_Domain   = 0x00000004;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RASCREDENTIALS
        {
            public int dwSize;
            public int dwMask;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
            public string szUserName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
            public string szPassword;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string szDomain;
        }

        [DllImport("rasapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RasGetCredentials(
            string? lpszPhonebook,
            string lpszEntry,
            ref RASCREDENTIALS lpCredentials);

        public sealed class RasStoredCredential
        {
            public string UserName { get; init; } = string.Empty;
            public string PasswordHandle { get; init; } = string.Empty;
            public string Domain { get; init; } = string.Empty;
        }

        /// <summary>
        /// Reads the saved credentials for a VPN/RAS phone-book entry.
        /// The returned PasswordHandle is NOT the plain password. Pass it to RasDial.
        /// </summary>
        public static RasStoredCredential? GetStoredCredentials(string? phonebookPath, string entryName)
        {
            var creds = new RASCREDENTIALS
            {
                dwSize = Marshal.SizeOf<RASCREDENTIALS>(),
                dwMask = RASCM_UserName | RASCM_Password | RASCM_Domain,
                szUserName = string.Empty,
                szPassword = string.Empty,
                szDomain = string.Empty
            };

            uint result = RasGetCredentials(phonebookPath, entryName, ref creds);
            if (result != 0)
                return null;

            return new RasStoredCredential
            {
                UserName = creds.szUserName ?? string.Empty,
                PasswordHandle = creds.szPassword ?? string.Empty,
                Domain = creds.szDomain ?? string.Empty
            };
        }
    }
}