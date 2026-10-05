using System.Runtime.InteropServices;
using PerfMonitor.Windows.Shell;

namespace PerfMonitor.Windows.Tests;

/// <summary>
/// 回归测试（任务栏遮挡恢复失效 bug）：守卫恢复卡片可见性时必须把卡片插到
/// topmost 带顶端。HWND_TOPMOST 的合法值是 64 位全 1 的 -1；若常量被声明为
/// uint（0xFFFFFFFF），零扩展后会变成无效句柄，SetWindowPos 静默失败，
/// 卡片被激活的任务栏永久遮挡（用户症状：点击任务栏/进出任务视图后小窗消失）。
/// 本测试用真实任务栏 + 测试进程内创建的真实窗口走完整遮挡→恢复路径。
/// </summary>
public sealed class TaskbarGuardTopmostRegressionTests
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const int GaRoot = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassW
    {
        public uint Style;
        public nint LpfnWndProc;
        public int CbClsExtra;
        public int CbWndExtra;
        public nint HInstance;
        public nint HIcon;
        public nint HCursor;
        public nint HbrBackground;
        public string? LpszMenuName;
        public string LpszClassName;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint window, nint insertAfter, int x, int y, int w, int h, uint flags);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WndClassW wndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? name);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, int cmdShow);

    private delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint DefWindowProcW(nint hWnd, uint msg, nint wParam, nint lParam);

    private static readonly WndProc DefWndProc = DefWindowProcW;

    [Fact(DisplayName = "遮挡恢复把卡片提到真实任务栏之上（HWND_TOPMOST 必须为合法 -1）")]
    public void Ensure_above_taskbar_elevates_card_above_real_taskbar()
    {
        var tray = FindWindowW("Shell_TrayWnd", null);
        Assert.True(tray != nint.Zero, "桌面会话应存在 Shell_TrayWnd 任务栏窗口。");
        Assert.True(GetWindowRect(tray, out var trayRect), "读取任务栏矩形失败。");

        // 任务栏内偏左位置（避开运行中的实例卡片与托盘图标），创建测试"卡片"窗口。
        const int width = 120;
        const int height = 20;
        int x = trayRect.Left + 60;
        int y = (trayRect.Top + trayRect.Bottom - height) / 2;

        var instance = GetModuleHandleW(null);
        const string className = "PerfMonitor.TestCardWnd";
        var wndClass = new WndClassW
        {
            LpfnWndProc = Marshal.GetFunctionPointerForDelegate(DefWndProc),
            HInstance = instance,
            LpszClassName = className,
        };
        Assert.NotEqual(0, RegisterClassW(ref wndClass));
        var card = nint.Zero;
        try
        {
            // WS_POPUP | WS_VISIBLE；WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            card = CreateWindowExW(
                0x00000008 | 0x08000000 | 0x00000080,
                className, string.Empty, 0x80000000 | 0x10000000,
                x, y, width, height, nint.Zero, nint.Zero, instance, nint.Zero);
            Assert.True(card != nint.Zero, "创建测试卡片窗口失败。");
            _ = ShowWindow(card, 5 /* SW_SHOW */);

            // 与守卫同款置顶调用（走同一 NativeMethods），随后压到任务栏正下方 → 被遮挡态。
            Assert.True(SetWindowPos(card, new nint(-1), 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate));
            Assert.True(SetWindowPos(card, tray, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate));

            var center = new POINT { X = x + width / 2, Y = y + height / 2 };
            var rootBefore = GetAncestor(WindowFromPoint(center), GaRoot);
            Assert.Equal(tray, rootBefore);

            using var guard = new WindowsTaskbarVisibilityGuard(() => card);
            guard.EnsureAboveTaskbar();

            var rootAfter = GetAncestor(WindowFromPoint(center), GaRoot);
            Assert.True(rootAfter == card,
                $"守卫未能把卡片提到任务栏之上（rootAfter={rootAfter}）。" +
                "若 SetWindowPos 静默失败，请检查 HWND_TOPMOST 常量是否为 64 位 -1。");

            // 守卫停止后应放回普通 z 序层（HWND_NOTOPMOST 同样必须为合法 -2）。
            guard.OnGuardStopped();
        }
        finally
        {
            if (card != nint.Zero)
            {
                _ = DestroyWindow(card);
            }

            _ = UnregisterClassW(className, instance);
        }
    }
}
