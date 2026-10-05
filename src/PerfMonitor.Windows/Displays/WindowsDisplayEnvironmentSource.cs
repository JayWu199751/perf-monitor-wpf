using System.Runtime.InteropServices;
using PerfMonitor.Core.Shell;

namespace PerfMonitor.Windows.Displays;

/// <summary>
/// 通过 EnumDisplayMonitors / GetMonitorInfo / GetDpiForMonitor 读取显示器边界、
/// 工作区与有效 DPI；并用一个不可见顶层窗口接收 WM_DISPLAYCHANGE 与
/// WM_SETTINGCHANGE 广播，在分辨率、工作区或系统参数变化时通知订阅方。
/// 资源释放遵循既有 adapter 习惯：销毁消息窗口，释放后枚举返回空列表。
/// </summary>
public sealed class WindowsDisplayEnvironmentSource : IDisplayEnvironmentSource, IDisposable
{
    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int MonitorDefaultToNearest = 2;
    private const uint WsOverlapped = 0x00000000;

    private static readonly object ClassSync = new();
    private static ushort? _windowClassAtom;
    private static readonly Dictionary<nint, WindowsDisplayEnvironmentSource> Sources = [];

    private nint _messageWindow;
    private bool _disposed;

    public WindowsDisplayEnvironmentSource()
    {
        _messageWindow = EnsureMessageWindow(this);
    }

    public event EventHandler? DisplaysChanged;

    public IReadOnlyList<DisplayInformation> GetDisplays()
    {
        if (_disposed)
        {
            return [];
        }

        var displays = new List<DisplayInformation>();
        var callback = new MonitorEnumProc((nint monitor, nint hdc, ref NativeRect rect, nint data) =>
        {
            var info = new MonitorInfo
            {
                CbSize = (uint)Marshal.SizeOf<MonitorInfo>()
            };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                displays.Add(new DisplayInformation(
                    ToPlacementRect(info.RcMonitor),
                    ToPlacementRect(info.RcWork),
                    GetDpiScale(monitor)));
            }

            return true;
        });
        return NativeMethods.EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero)
            ? displays
            : [];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_messageWindow != nint.Zero)
        {
            Sources.Remove(_messageWindow);
            _ = NativeMethods.DestroyWindow(_messageWindow);
            _messageWindow = nint.Zero;
        }
    }

    private void RaiseDisplaysChanged()
    {
        if (_disposed)
        {
            return;
        }

        DisplaysChanged?.Invoke(this, EventArgs.Empty);
    }

    private static double GetDpiScale(nint monitor)
    {
        try
        {
            return NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out _) && dpiX > 0
                ? dpiX / 96.0
                : 1.0;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return 1.0;
        }
    }

    private static PlacementRect ToPlacementRect(NativeRect rect) => new(
        rect.Left,
        rect.Top,
        rect.Right - rect.Left,
        rect.Bottom - rect.Top);

    private static nint EnsureMessageWindow(WindowsDisplayEnvironmentSource source)
    {
        lock (ClassSync)
        {
            if (_windowClassAtom is not { } atom)
            {
                atom = RegisterWindowClass();
                if (atom == 0)
                {
                    return nint.Zero;
                }

                _windowClassAtom = atom;
            }

            var window = NativeMethods.CreateWindowExW(
                0,
                atom,
                "PerfMonitorDisplaySource",
                WsOverlapped,
                0,
                0,
                0,
                0,
                nint.Zero,
                nint.Zero,
                nint.Zero,
                nint.Zero);
            if (window == nint.Zero)
            {
                return nint.Zero;
            }

            Sources[window] = source;
            return window;
        }
    }

    private static ushort RegisterWindowClass()
    {
        var className = "PerfMonitorDisplaySourceWindow";
        var classStruct = new WndClassEx
        {
            CbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            LpfnWndProc = DisplaySourceWndProc,
            LpszClassName = className,
            HInstance = NativeMethods.GetModuleHandleW(null)
        };
        return NativeMethods.RegisterClassExW(ref classStruct);
    }

    private static nint DisplaySourceWndProc(nint window, uint message, nint wParam, nint lParam)
    {
        if (message is WmDisplayChange or WmSettingChange &&
            Sources.TryGetValue(window, out var source))
        {
            source.RaiseDisplaysChanged();
        }

        return NativeMethods.DefWindowProcW(window, message, wParam, lParam);
    }

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, ref NativeRect rect, nint data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint CbSize;

        public NativeRect RcMonitor;

        public NativeRect RcWork;

        public uint DwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint CbSize;

        public uint Style;

        public WndClassProc? LpfnWndProc;

        public int CbClsExtra;

        public int CbWndExtra;

        public nint HInstance;

        public nint HIcon;

        public nint HCursor;

        public nint HbrBackground;

        public string? LpszMenuName;

        public string LpszClassName;

        public nint HIconSm;
    }

    private delegate nint WndClassProc(nint window, uint message, nint wParam, nint lParam);

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern bool EnumDisplayMonitors(
            nint hdc,
            nint clipRect,
            MonitorEnumProc callback,
            nint data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

        [DllImport("shcore.dll")]
        internal static extern bool GetDpiForMonitor(
            nint monitor,
            int dpiType,
            out uint dpiX,
            out uint dpiY);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern ushort RegisterClassExW(ref WndClassEx windowClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern nint CreateWindowExW(
            uint extendedStyle,
            ushort classAtom,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            nint parent,
            nint menu,
            nint instance,
            nint param);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyWindow(nint window);

        [DllImport("user32.dll")]
        internal static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern nint GetModuleHandleW(string? moduleName);
    }
}
