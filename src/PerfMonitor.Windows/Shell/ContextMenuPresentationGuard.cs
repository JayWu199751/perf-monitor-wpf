namespace PerfMonitor.Windows.Shell;

/// <summary>
/// 共享菜单呈现去重：同一次右键可能先后收到 WM_RBUTTONUP 与 WM_CONTEXTMENU 两条原生消息，
/// 菜单已打开或距上次打开在抑制窗口内时不得再次弹出。
/// </summary>
public sealed class ContextMenuPresentationGuard
{
    private readonly int _reopenWindowMilliseconds;
    private long? _lastOpenedAtMilliseconds;

    public ContextMenuPresentationGuard(int reopenWindowMilliseconds = 200)
    {
        _reopenWindowMilliseconds = reopenWindowMilliseconds;
    }

    /// <summary>判断当前是否允许弹出菜单；返回 true 时记录本次打开时刻。</summary>
    public bool ShouldOpen(bool isMenuCurrentlyOpen, long nowMilliseconds)
    {
        if (isMenuCurrentlyOpen ||
            (_lastOpenedAtMilliseconds is { } lastOpenedAt &&
                nowMilliseconds - lastOpenedAt < _reopenWindowMilliseconds))
        {
            return false;
        }

        _lastOpenedAtMilliseconds = nowMilliseconds;
        return true;
    }
}
