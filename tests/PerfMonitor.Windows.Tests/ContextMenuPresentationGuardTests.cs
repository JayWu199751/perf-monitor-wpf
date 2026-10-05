using PerfMonitor.Windows.Shell;

namespace PerfMonitor.Windows.Tests;

public sealed class ContextMenuPresentationGuardTests
{
    [Fact(DisplayName = "菜单已打开时不再允许重复打开")]
    public void Suppresses_reopening_while_the_menu_is_already_open()
    {
        var guard = new ContextMenuPresentationGuard();

        Assert.True(guard.ShouldOpen(isMenuCurrentlyOpen: false, nowMilliseconds: 1_000));
        Assert.False(guard.ShouldOpen(isMenuCurrentlyOpen: true, nowMilliseconds: 1_100));
    }

    [Fact(DisplayName = "重复原生消息落在抑制窗口内时只弹一次菜单")]
    public void Suppresses_duplicate_native_messages_inside_the_window()
    {
        var guard = new ContextMenuPresentationGuard();

        Assert.True(guard.ShouldOpen(isMenuCurrentlyOpen: false, nowMilliseconds: 1_000));
        // WM_RBUTTONUP 与 WM_CONTEXTMENU 先后到达：第二次视为重复消息。
        Assert.False(guard.ShouldOpen(isMenuCurrentlyOpen: false, nowMilliseconds: 1_050));
        Assert.False(guard.ShouldOpen(isMenuCurrentlyOpen: false, nowMilliseconds: 1_199));
    }

    [Fact(DisplayName = "抑制窗口结束后允许再次打开菜单")]
    public void Allows_reopening_after_the_suppression_window()
    {
        var guard = new ContextMenuPresentationGuard();

        Assert.True(guard.ShouldOpen(isMenuCurrentlyOpen: false, nowMilliseconds: 1_000));
        Assert.True(guard.ShouldOpen(isMenuCurrentlyOpen: false, nowMilliseconds: 1_200));
    }
}
