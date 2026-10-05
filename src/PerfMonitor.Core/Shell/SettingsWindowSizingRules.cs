namespace PerfMonitor.Core.Shell;

/// <summary>
/// 设置窗尺寸规则：窗口最大高度按当前屏工作区可用高确定，不照抄旧版固定 880 高度。
/// 内容低于工作区时窗口按内容自适应（无滚动条），超过时窗口封顶并由内容区滚动访问所有行。
/// </summary>
public static class SettingsWindowSizingRules
{
    /// <summary>窗口允许的最小最大高度；工作区过小时保底，保证设置窗仍可用。</summary>
    public const double MinWindowHeight = 240;

    /// <summary>按当前屏工作区可用高（DIP）解析窗口允许的最大高度；非法输入回退保底高度。</summary>
    public static double ResolveMaxWindowHeight(double workAreaHeight)
    {
        if (!double.IsFinite(workAreaHeight) || workAreaHeight <= 0)
        {
            return MinWindowHeight;
        }

        return Math.Max(MinWindowHeight, workAreaHeight);
    }
}
