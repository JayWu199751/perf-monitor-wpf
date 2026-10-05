using PerfMonitor.Core.Shell;
using NativeMethods = PerfMonitor.Windows.Native.NativeMethods;

namespace PerfMonitor.Windows.Shell;

/// <summary>
/// 任务栏 z-order 可见性守卫的 Windows 实现：以卡片中心点的最顶层根窗口归属判遮挡
/// （根窗口为任务栏即被遮挡，不按截图与 Topmost 属性判定），被遮挡时把卡片提到
/// topmost 层恢复可见性。快守卫实时判定；慢刷新枚举任务栏根窗口句柄，explorer
/// 重启后自动恢复。守卫停止时把卡片放回普通 z 序层。
/// </summary>
public sealed class WindowsTaskbarVisibilityGuard : ITaskbarVisibilityGuardPort, IDisposable
{
    private const int GaRoot = 2;
    // HWND_TOPMOST=-1、HWND_NOTOPMOST=-2：必须按 64 位有符号 -1/-2 传入。
    // 不可用 uint(0xFFFFFFFF) 再转 nint——零扩展后变成无效句柄，SetWindowPos 会静默失败。
    private const nint HwndNotopmost = -2;
    private const nint HwndTopmost = -1;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    private readonly Func<nint> _cardWindow;
    private readonly object _sync = new();
    private readonly List<nint> _taskbarWindows = [];
    private bool _elevated;
    private bool _disposed;

    public WindowsTaskbarVisibilityGuard(Func<nint> cardWindow)
    {
        ArgumentNullException.ThrowIfNull(cardWindow);
        _cardWindow = cardWindow;
    }

    /// <summary>最近一次慢刷新枚举到的任务栏根窗口句柄数。</summary>
    public int TaskbarWindowCount
    {
        get
        {
            lock (_sync)
            {
                return _taskbarWindows.Count;
            }
        }
    }

    public void EnsureAboveTaskbar()
    {
        var card = _cardWindow();
        if (card == nint.Zero || _disposed)
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (!NativeMethods.GetWindowRect(card, out var rect))
            {
                return;
            }

            // 卡片中心点的最顶层可见窗口的根窗口是任务栏 → 卡片被任务栏遮挡。
            var hit = NativeMethods.WindowFromPoint(
                new NativeMethods.NativePoint { X = (rect.Left + rect.Right) / 2, Y = (rect.Top + rect.Bottom) / 2 });
            if (hit == nint.Zero)
            {
                return;
            }

            var root = NativeMethods.GetAncestor(hit, GaRoot);
            if (root == card || !IsTaskbarRootWindow(root))
            {
                return;
            }

            if (NativeMethods.SetWindowPos(
                    card,
                    HwndTopmost,
                    0,
                    0,
                    0,
                    0,
                    SwpNoMove | SwpNoSize | SwpNoActivate))
            {
                _elevated = true;
            }
        }
    }

    public void RefreshTaskbarHandles()
    {
        if (_disposed)
        {
            return;
        }

        var windows = new List<nint>();
        var callback = new NativeMethods.EnumWindowsProc((window, data) =>
        {
            if (IsTaskbarClass(window))
            {
                windows.Add(window);
            }

            return true;
        });
        if (!NativeMethods.EnumWindows(callback, nint.Zero))
        {
            return;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _taskbarWindows.Clear();
            _taskbarWindows.AddRange(windows);
        }
    }

    public void OnGuardStopped()
    {
        lock (_sync)
        {
            if (!_elevated || _disposed)
            {
                return;
            }

            var card = _cardWindow();
            if (card != nint.Zero)
            {
                _ = NativeMethods.SetWindowPos(
                    card,
                    HwndNotopmost,
                    0,
                    0,
                    0,
                    0,
                    SwpNoMove | SwpNoSize | SwpNoActivate);
            }

            _elevated = false;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _taskbarWindows.Clear();
        }
    }

    private static bool IsTaskbarRootWindow(nint root) =>
        root != nint.Zero && NativeMethods.IsWindow(root) && IsTaskbarClass(root);

    private static bool IsTaskbarClass(nint window)
    {
        var buffer = new char[64];
        var length = NativeMethods.GetClassName(window, buffer, buffer.Length);
        if (length <= 0 || length >= buffer.Length)
        {
            return false;
        }

        return TaskbarClassNames.All.Contains(new string(buffer, 0, length));
    }
}
