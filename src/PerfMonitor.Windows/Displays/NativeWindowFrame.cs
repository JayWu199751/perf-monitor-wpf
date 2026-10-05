using System.Runtime.InteropServices;
using PerfMonitor.Core.Shell;

namespace PerfMonitor.Windows.Displays;

/// <summary>
/// 以物理像素读写顶层窗口框架；供 WPF 性能条窗口实现落位端口使用，
/// 避免 DIP 换算在混合 DPI 下产生歧义。
/// </summary>
public static class NativeWindowFrame
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    public static PlacementRect ReadFrame(nint window)
    {
        var rect = new NativeRect();
        return window != nint.Zero && NativeMethods.GetWindowRect(window, ref rect)
            ? new PlacementRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
            : default;
    }

    public static bool SetPosition(nint window, double x, double y)
    {
        if (window == nint.Zero)
        {
            return false;
        }

        return NativeMethods.SetWindowPos(
            window,
            nint.Zero,
            (int)Math.Round(x),
            (int)Math.Round(y),
            0,
            0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpNoOwnerZOrder);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(nint window, ref NativeRect rect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            nint window,
            nint insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);
    }
}
