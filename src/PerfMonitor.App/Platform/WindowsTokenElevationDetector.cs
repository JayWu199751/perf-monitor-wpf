using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PerfMonitor.App.Platform;

internal static class WindowsTokenElevationDetector
{
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationInformationClass = 20;

    public static bool IsCurrentProcessElevated()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        using (token)
        {
            if (!GetTokenInformation(
                    token,
                    TokenElevationInformationClass,
                    out var elevation,
                    Marshal.SizeOf<TokenElevation>(),
                    out _))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return elevation.TokenIsElevated != 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevation
    {
        public int TokenIsElevated;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out SafeAccessTokenHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        SafeAccessTokenHandle tokenHandle,
        int tokenInformationClass,
        out TokenElevation tokenInformation,
        int tokenInformationLength,
        out int returnLength);
}
