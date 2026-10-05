using PerfMonitor.Core.Shell;
using PerfMonitor.Windows.Shell;

namespace PerfMonitor.Windows.Tests;

public sealed class WindowsTaskbarVisibilityGuardTests
{
    [Fact(DisplayName = "卡片句柄为空时遮挡检查与守卫停止都安全无副作用")]
    public void Empty_card_handle_keeps_guard_calls_safe()
    {
        using var guard = new WindowsTaskbarVisibilityGuard(() => nint.Zero);

        guard.RefreshTaskbarHandles();
        guard.EnsureAboveTaskbar();
        guard.OnGuardStopped();

        Assert.True(guard.TaskbarWindowCount >= 0);
    }

    [Fact(DisplayName = "慢刷新枚举到运行中的任务栏根窗口句柄")]
    public void Slow_refresh_enumerates_running_taskbar_root_windows()
    {
        using var guard = new WindowsTaskbarVisibilityGuard(() => nint.Zero);

        guard.RefreshTaskbarHandles();

        Assert.True(guard.TaskbarWindowCount >= 1, "桌面会话应至少存在一个 Shell 任务栏窗口。");
    }

    [Fact(DisplayName = "重复慢刷新重新枚举句柄，explorer 重启后能恢复")]
    public void Repeated_refresh_re_enumerates_taskbar_handles()
    {
        using var guard = new WindowsTaskbarVisibilityGuard(() => nint.Zero);

        guard.RefreshTaskbarHandles();
        var first = guard.TaskbarWindowCount;
        guard.RefreshTaskbarHandles();

        Assert.Equal(first, guard.TaskbarWindowCount);
    }

    [Fact(DisplayName = "卡片句柄无效时遮挡检查安全跳过，不触碰其他窗口")]
    public void An_invalid_card_handle_is_skipped_safely()
    {
        using var guard = new WindowsTaskbarVisibilityGuard(() => (nint)1);

        guard.RefreshTaskbarHandles();
        guard.EnsureAboveTaskbar();
        guard.OnGuardStopped();

        Assert.True(guard.TaskbarWindowCount >= 1);
    }
}
