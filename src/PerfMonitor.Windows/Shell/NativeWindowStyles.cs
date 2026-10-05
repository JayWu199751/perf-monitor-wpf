using NativeMethods = PerfMonitor.Windows.Native.NativeMethods;

namespace PerfMonitor.Windows.Shell;

/// <summary>
/// 任务切换器隐匿：为顶层窗口补上 WS_EX_TOOLWINDOW。
/// 新版 WPF 对 ShowInTaskbar=false 的实现是挂靠 WPF 自建的隐藏所有者窗口而非工具窗口样式；
/// 所有权只压掉任务栏按钮，Windows 11 的 Alt+Tab 与任务视图仍列出被持有的可见窗口。
/// 工具窗口样式是 shell 对任务栏、Alt+Tab、任务视图一致排除的机制，与所有权叠加无害。
/// 决策背景见 docs/adr/0007-tool-window-exclusion-from-task-switcher.md。
/// </summary>
public static class NativeWindowStyles
{
    /// <summary>幂等注入工具窗口样式；句柄无效时忽略。须在 WPF 完成自身样式设置后调用（Loaded 时机）。</summary>
    public static void ExcludeFromTaskSwitcher(nint window)
    {
        if (window == nint.Zero)
        {
            return;
        }

        var exStyle = NativeMethods.GetWindowLong(window, NativeMethods.GwlExstyle);
        if ((exStyle & NativeMethods.WsExToolWindow) != 0)
        {
            return;
        }

        _ = NativeMethods.SetWindowLong(window, NativeMethods.GwlExstyle, exStyle | NativeMethods.WsExToolWindow);
    }
}
