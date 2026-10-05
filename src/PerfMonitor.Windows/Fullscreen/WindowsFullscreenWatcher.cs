using System.Drawing;
using System.Runtime.InteropServices;
using PerfMonitor.Core.Shell;

namespace PerfMonitor.Windows.Fullscreen;

/// <summary>前台窗口探测所需的静态信息快照，全部为统一物理坐标。</summary>
public readonly record struct FullscreenWindowProbe(
    string ClassName,
    int ProcessId,
    long Style,
    bool IsVisible,
    bool IsCloaked,
    Rectangle Bounds);

/// <summary>
/// F09 全屏判定规则：无边框窗口（即使带 WS_MAXIMIZE，如 Chrome F11）覆盖对应显示器
/// 物理矩形才算全屏；带标题栏的普通最大化窗口不算；桌面与任务栏系统类排除。
/// 进程为 PerMonitorV2 感知，窗口与显示器矩形同处统一物理坐标系，不做额外缩放。
/// </summary>
public static class FullscreenWindowRules
{
    public const int BoundsTolerancePixels = 2;

    public const long WsCaption = 0x00C00000;

    private static readonly HashSet<string> ExcludedClassNames = new(StringComparer.Ordinal)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd"
    };

    public static bool IsExcludedClassName(string className) => ExcludedClassNames.Contains(className);

    public static bool CoversMonitorBounds(Rectangle windowBounds, Rectangle monitorBounds) =>
        windowBounds.Left <= monitorBounds.Left + BoundsTolerancePixels &&
        windowBounds.Right >= monitorBounds.Right - BoundsTolerancePixels &&
        windowBounds.Top <= monitorBounds.Top + BoundsTolerancePixels &&
        windowBounds.Bottom >= monitorBounds.Bottom - BoundsTolerancePixels;

    public static bool IsFullscreenCandidate(in FullscreenWindowProbe probe, Rectangle monitorBounds)
    {
        if (IsExcludedClassName(probe.ClassName) || !probe.IsVisible || probe.IsCloaked)
        {
            return false;
        }

        // 带标题栏的窗口（含普通最大化）不是全屏；无边框窗口按物理矩形覆盖判定。
        if ((probe.Style & WsCaption) != 0)
        {
            return false;
        }

        return CoversMonitorBounds(probe.Bounds, monitorBounds);
    }
}

public sealed class WindowsFullscreenWatcher : IFullscreenWatcher
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    private const uint MonitorDefaultToNearest = 2;
    private const int DwmwaCloaked = 14;
    private const int WindowClassNameMaxLength = 256;

    private readonly TimeSpan _pollInterval;
    private readonly Func<nint> _foregroundWindowProvider;
    private readonly Func<nint, FullscreenWindowProbe?> _probeProvider;
    private readonly Func<nint, Rectangle?> _monitorBoundsProvider;
    private readonly int _ownProcessId;
    private readonly object _sync = new();
    private readonly SynchronizationContext? _callbackContext;
    private System.Threading.Timer? _timer;
    private Action<bool>? _onFullscreenChanged;
    // 0 = 尚未报告，1 = 非全屏，2 = 全屏；只在状态翻转时通知（仅边沿处理）。
    private int _lastReportedState;
    private bool _disposed;

    public WindowsFullscreenWatcher()
        : this(
            DefaultPollInterval,
            static () => NativeMethods.GetForegroundWindow(),
            static handle => ProbeWindow(handle),
            static handle => NativeMethods.TryGetMonitorBounds(handle, out var bounds) ? bounds : null,
            ownProcessId: null,
            SynchronizationContext.Current)
    {
    }

    public WindowsFullscreenWatcher(
        TimeSpan pollInterval,
        Func<nint> foregroundWindowProvider,
        Func<nint, FullscreenWindowProbe?> probeProvider,
        Func<nint, Rectangle?>? monitorBoundsProvider = null,
        int? ownProcessId = null,
        SynchronizationContext? callbackContext = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pollInterval, TimeSpan.Zero);
        _pollInterval = pollInterval;
        _foregroundWindowProvider = foregroundWindowProvider;
        _probeProvider = probeProvider;
        _monitorBoundsProvider = monitorBoundsProvider ?? DefaultMonitorBoundsProvider;
        _ownProcessId = ownProcessId ?? Environment.ProcessId;
        // 边沿通知送回创建线程（WPF 为 UI 线程）；注入 null 时在线程池线程直接回调。
        _callbackContext = callbackContext;
    }

    private static Rectangle? DefaultMonitorBoundsProvider(nint window) =>
        NativeMethods.TryGetMonitorBounds(window, out var bounds) ? bounds : null;

    public void Start(Action<bool> onFullscreenChanged)
    {
        ArgumentNullException.ThrowIfNull(onFullscreenChanged);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_timer is not null)
            {
                return;
            }

            _onFullscreenChanged = onFullscreenChanged;
            _lastReportedState = 0;
            _timer = new System.Threading.Timer(
                _ => PollOnce(),
                null,
                dueTime: TimeSpan.Zero,
                period: _pollInterval);
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _timer?.Dispose();
            _timer = null;
            _onFullscreenChanged = null;
            _lastReportedState = 0;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }

        Stop();
    }

    private void PollOnce()
    {
        bool isFullscreen;
        lock (_sync)
        {
            if (_timer is null || _onFullscreenChanged is null)
            {
                return;
            }

            isFullscreen = EvaluateForegroundFullscreen();
            var reportedState = isFullscreen ? 2 : 1;
            if (_lastReportedState == reportedState)
            {
                return;
            }

            _lastReportedState = reportedState;
        }

        Dispatch(isFullscreen);
    }

    private bool EvaluateForegroundFullscreen()
    {
        var foregroundWindow = _foregroundWindowProvider();
        if (foregroundWindow == nint.Zero)
        {
            return false;
        }

        var probe = _probeProvider(foregroundWindow);
        if (probe is not { } candidate || candidate.ProcessId == _ownProcessId)
        {
            return false;
        }

        return _monitorBoundsProvider(foregroundWindow) is { } monitorBounds &&
               FullscreenWindowRules.IsFullscreenCandidate(candidate, monitorBounds);
    }

    private void Dispatch(bool isFullscreen)
    {
        var callback = _onFullscreenChanged;
        if (callback is null)
        {
            return;
        }

        var context = _callbackContext;
        if (context is not null && !ReferenceEquals(context, SynchronizationContext.Current))
        {
            context.Post(_ => callback(isFullscreen), null);
            return;
        }

        callback(isFullscreen);
    }

    private static FullscreenWindowProbe? ProbeWindow(nint window)
    {
        if (!NativeMethods.IsWindowVisible(window))
        {
            return new FullscreenWindowProbe(string.Empty, 0, 0, IsVisible: false, IsCloaked: false, Rectangle.Empty);
        }

        var className = NativeMethods.GetWindowClassName(window);
        _ = NativeMethods.GetWindowThreadProcessId(window, out var processId);
        var style = NativeMethods.GetWindowLong(window, NativeMethods.GwlStyle);
        var isCloaked = NativeMethods.IsWindowCloaked(window);
        if (!NativeMethods.GetWindowRectangle(window, out var bounds))
        {
            return null;
        }

        return new FullscreenWindowProbe(
            className,
            (int)processId,
            style,
            IsVisible: true,
            IsCloaked: isCloaked,
            bounds);
    }

    internal static class NativeMethods
    {
        public const int GwlStyle = -16;

        [DllImport("user32.dll")]
        public static extern nint GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(nint window);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(nint window, out uint processId);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static extern long GetWindowLong(nint window, int index);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(nint window, System.Text.StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(nint window, out Rect rectangle);

        [DllImport("user32.dll")]
        private static extern nint MonitorFromWindow(nint window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(
            nint window,
            int attribute,
            out int value,
            int sizeOfValue);

        public static string GetWindowClassName(nint window)
        {
            var builder = new System.Text.StringBuilder(WindowClassNameMaxLength);
            _ = GetClassName(window, builder, builder.Capacity);
            return builder.ToString();
        }

        public static bool GetWindowRectangle(nint window, out Rectangle bounds)
        {
            if (!GetWindowRect(window, out var rectangle))
            {
                bounds = Rectangle.Empty;
                return false;
            }

            bounds = Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
            return true;
        }

        public static bool TryGetMonitorBounds(nint window, out Rectangle monitorBounds)
        {
            var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
            if (monitor == nint.Zero)
            {
                monitorBounds = Rectangle.Empty;
                return false;
            }

            var info = new MonitorInfo
            {
                Size = checked((uint)Marshal.SizeOf<MonitorInfo>())
            };
            if (!GetMonitorInfoW(monitor, ref info))
            {
                monitorBounds = Rectangle.Empty;
                return false;
            }

            monitorBounds = Rectangle.FromLTRB(
                info.MonitorLeft,
                info.MonitorTop,
                info.MonitorRight,
                info.MonitorBottom);
            return true;
        }

        public static bool IsWindowCloaked(nint window) =>
            DwmGetWindowAttribute(window, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;

            public int Top;

            public int Right;

            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MonitorInfo
        {
            public uint Size;

            public int MonitorLeft;

            public int MonitorTop;

            public int MonitorRight;

            public int MonitorBottom;

            public int WorkLeft;

            public int WorkTop;

            public int WorkRight;

            public int WorkBottom;

            public uint Flags;
        }
    }
}
