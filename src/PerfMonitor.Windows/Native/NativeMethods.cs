using System.Runtime.InteropServices;

namespace PerfMonitor.Windows.Native;

/// <summary>
/// 跨 adapter 共享的 Win32 互操作声明：统一 RECT/POINT/MONITORINFO 结构与常用
/// user32/shcore 入口，避免各文件私有重复声明漂移。文件或 adapter 专用的入口
/// （消息窗口类注册、DWM 属性等）仍留在各自文件内。
/// </summary>
public static class NativeMethods
{
    public const uint MonitorDefaultToNearest = 2;

    public const int DpiEffective = 0;

    /// <summary>GetWindowLong/SetWindowLong 的扩展样式索引。</summary>
    public const int GwlExstyle = -20;

    /// <summary>工具窗口扩展样式；任务栏、Alt+Tab 与任务视图一致排除该类窗口。</summary>
    public const int WsExToolWindow = 0x0080;

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativePoint
    {
        public int X;

        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MonitorInfoW
    {
        public uint CbSize;

        public NativeRect RcMonitor;

        public NativeRect RcWork;

        public uint DwFlags;
    }

    public delegate bool EnumWindowsProc(nint window, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(nint window, char[] buffer, int maxCount);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfoW info);

    [DllImport("shcore.dll")]
    public static extern bool GetDpiForMonitor(
        nint monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(NativePoint point);

    /// <summary>读取扩展样式；GWL_EXSTYLE 为 32 位样式值，W 版入口在 x64 上同样适用。</summary>
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static extern int GetWindowLong(nint window, int index);

    /// <summary>写入扩展样式并返回旧值；GWL_EXSTYLE 为 32 位样式值，W 版入口在 x64 上同样适用。</summary>
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    public static extern int SetWindowLong(nint window, int index, int value);
}
