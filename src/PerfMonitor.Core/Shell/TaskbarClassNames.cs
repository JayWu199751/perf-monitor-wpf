namespace PerfMonitor.Core.Shell;

/// <summary>
/// Windows 任务栏窗口类名（字符串常量，不含 Win32 依赖）：
/// 全屏排除规则（F09）与任务栏可见性守卫共用的单一来源。
/// </summary>
public static class TaskbarClassNames
{
    public const string Tray = "Shell_TrayWnd";

    public const string SecondaryTray = "Shell_SecondaryTrayWnd";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Tray,
        SecondaryTray
    };
}
