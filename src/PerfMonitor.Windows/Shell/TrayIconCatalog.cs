namespace PerfMonitor.Windows.Shell;

/// <summary>
/// 托盘图标资源选择：有效浅色主题用黑色托盘图（tray-light），深色主题用白色托盘图（tray-dark）；
/// 物理尺寸按「16 × DPI 缩放比」就近匹配 16/20/24/28/32 档位，避免系统拉伸造成模糊。
/// 资产复用自旧版冻结源码 rewrite-wpf/reference/baseline/resources/。
/// </summary>
public static class TrayIconCatalog
{
    public const string LightThemePrefix = "tray-light";

    public const string DarkThemePrefix = "tray-dark";

    public static readonly int[] PhysicalPixelSizes = [16, 20, 24, 28, 32];

    public static string SelectFileName(bool isDarkEffective, double dpiScale)
    {
        var prefix = isDarkEffective ? DarkThemePrefix : LightThemePrefix;
        var targetPhysicalSize = (int)Math.Round(16 * dpiScale);
        var size = PhysicalPixelSizes
            .OrderBy(candidate => Math.Abs(candidate - targetPhysicalSize))
            .First();
        return $"{prefix}-{size}.png";
    }
}
