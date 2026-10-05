using System.Drawing;
using PerfMonitor.Windows.Fullscreen;

namespace PerfMonitor.Windows.Tests;

/// <summary>
/// F09 全屏判定规则与前台窗口观察者：无边框 F11 全屏必须识别，
/// 普通最大化（带标题栏）不算全屏，排除本应用、桌面与任务栏。
/// </summary>
public sealed class WindowsFullscreenWatcherTests
{
    private static readonly Rectangle MonitorBounds = new(0, 0, 2560, 1440);

    private static FullscreenWindowProbe Probe(
        string className = "Chrome_WidgetWin_1",
        int processId = 4242,
        long style = 0,
        bool isVisible = true,
        bool isCloaked = false,
        Rectangle? bounds = null) => new(
        className,
        processId,
        style,
        isVisible,
        isCloaked,
        bounds ?? MonitorBounds);

    [Theory(DisplayName = "排除桌面与任务栏系统窗口类，含副屏任务栏")]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    public void Desktop_and_taskbar_class_names_are_excluded(string className)
    {
        var probe = Probe(className: className);

        Assert.False(FullscreenWindowRules.IsFullscreenCandidate(probe, MonitorBounds));
    }

    [Fact(DisplayName = "覆盖显示器物理矩形的无边框窗口是全屏（Chrome F11 即使带 WS_MAXIMIZE）")]
    public void Borderless_window_covering_the_monitor_is_fullscreen_even_maximized()
    {
        const long wsMaximize = 0x01000000;
        var probe = Probe(style: wsMaximize);

        Assert.True(FullscreenWindowRules.IsFullscreenCandidate(probe, MonitorBounds));
    }

    [Fact(DisplayName = "带标题栏的普通最大化窗口不算全屏")]
    public void Maximized_window_with_a_title_bar_is_not_fullscreen()
    {
        const long wsCaption = 0x00C00000;
        const long wsMaximize = 0x01000000;
        // 最大化窗口的客户区覆盖工作区但保留边框区域，这里仍取整屏矩形验证样式判据优先。
        var probe = Probe(style: wsCaption | wsMaximize, bounds: MonitorBounds);

        Assert.False(FullscreenWindowRules.IsFullscreenCandidate(probe, MonitorBounds));
    }

    [Fact(DisplayName = "窗口矩形在边界容差 2 物理px 内视为覆盖显示器")]
    public void Window_bounds_within_the_two_pixel_tolerance_count_as_covering()
    {
        var exactCover = Probe(bounds: new Rectangle(-2, -2, 2564, 1444));
        Assert.True(FullscreenWindowRules.IsFullscreenCandidate(exactCover, MonitorBounds));

        var beyondTolerance = Probe(bounds: new Rectangle(-3, -3, 2560, 1440));
        Assert.False(FullscreenWindowRules.IsFullscreenCandidate(beyondTolerance, MonitorBounds));

        var slightlySmaller = Probe(bounds: new Rectangle(0, 0, 2560, 1439));
        Assert.True(FullscreenWindowRules.IsFullscreenCandidate(slightlySmaller, MonitorBounds));

        var clearlySmaller = Probe(bounds: new Rectangle(0, 0, 2560, 1200));
        Assert.False(FullscreenWindowRules.IsFullscreenCandidate(clearlySmaller, MonitorBounds));
    }

    [Fact(DisplayName = "不可见或被遮挡 cloak 的前台候选不算全屏")]
    public void Invisible_or_cloaked_windows_are_not_fullscreen()
    {
        Assert.False(FullscreenWindowRules.IsFullscreenCandidate(Probe(isVisible: false), MonitorBounds));
        Assert.False(FullscreenWindowRules.IsFullscreenCandidate(Probe(isCloaked: true), MonitorBounds));
    }

    [Fact(DisplayName = "观察者只在进入/退出全屏的状态边沿通知，持续全屏不重复通知")]
    public async Task Watcher_reports_only_fullscreen_state_edges()
    {
        var foregroundWindow = new IntPtr(1234);
        using var watcher = new WindowsFullscreenWatcher(
            pollInterval: TimeSpan.FromMilliseconds(20),
            foregroundWindowProvider: () => foregroundWindow,
            probeProvider: _ => Probe(),
            monitorBoundsProvider: _ => MonitorBounds,
            ownProcessId: 999,
            callbackContext: null);

        var notifications = new List<bool>();
        watcher.Start(isFullscreen =>
        {
            lock (notifications)
            {
                notifications.Add(isFullscreen);
            }
        });

        // 等待若干轮询周期后检查是否恰好一次进入边沿，随后模拟退出。
        await Task.Delay(300);
        bool firstEnter;
        lock (notifications)
        {
            firstEnter = notifications.Count == 1 && notifications[0];
        }

        Assert.True(firstEnter, $"进入全屏应只通知一次 true，实际: [{string.Join(", ", notifications)}]");

        foregroundWindow = IntPtr.Zero;
        await Task.Delay(300);
        bool exit;
        lock (notifications)
        {
            exit = notifications.Count == 2 && notifications[1] == false;
        }

        watcher.Stop();
        Assert.True(exit, $"退出全屏应只追加一次 false 通知，实际: [{string.Join(", ", notifications)}]");
    }

    [Fact(DisplayName = "观察者排除本进程的窗口，即使其矩形覆盖显示器")]
    public async Task Watcher_excludes_windows_of_the_own_process()
    {
        var sawFullscreenNotification = new TaskCompletionSource();
        using var watcher = new WindowsFullscreenWatcher(
            pollInterval: TimeSpan.FromMilliseconds(20),
            foregroundWindowProvider: () => new IntPtr(1234),
            probeProvider: _ => Probe(processId: Environment.ProcessId),
            monitorBoundsProvider: _ => MonitorBounds,
            ownProcessId: Environment.ProcessId,
            callbackContext: null);

        watcher.Start(isFullscreen =>
        {
            if (isFullscreen)
            {
                sawFullscreenNotification.TrySetResult();
            }
        });

        await Assert.ThrowsAsync<TimeoutException>(
            () => sawFullscreenNotification.Task.WaitAsync(TimeSpan.FromMilliseconds(400)));
        watcher.Stop();
    }

    [Fact(DisplayName = "全屏判定按显示器物理矩形比较，混合 DPI 不做额外缩放")]
    public void Fullscreen_comparison_uses_unified_physical_coordinates()
    {
        // 1920x1080 的 150% DPI 显示器物理矩形与同物理坐标窗口。
        var monitor = new Rectangle(2560, 0, 1920, 1080);
        var probe = Probe(bounds: new Rectangle(2560, 0, 1920, 1080));

        Assert.True(FullscreenWindowRules.IsFullscreenCandidate(probe, monitor));
    }
}
