using PerfMonitor.Windows.Shell;

namespace PerfMonitor.Windows.Tests;

public sealed class TrayIconCatalogTests
{
    [Fact(DisplayName = "有效浅色主题选择黑色托盘图，深色主题选择白色托盘图")]
    public void Selects_black_icon_for_light_theme_and_white_icon_for_dark_theme()
    {
        Assert.StartsWith("tray-light", TrayIconCatalog.SelectFileName(isDarkEffective: false, dpiScale: 1.0));
        Assert.StartsWith("tray-dark", TrayIconCatalog.SelectFileName(isDarkEffective: true, dpiScale: 1.0));
    }

    [Theory(DisplayName = "按 16 乘缩放比就近匹配 16/20/24/28/32 物理像素档位")]
    [InlineData(1.00, 16)]
    [InlineData(1.25, 20)]
    [InlineData(1.50, 24)]
    [InlineData(1.75, 28)]
    [InlineData(2.00, 32)]
    [InlineData(1.10, 16)]
    [InlineData(1.35, 20)]
    [InlineData(1.60, 24)]
    // 1.9×16 四舍五入到 30，与 28/32 等距：稳定排序下就近取较小档位。
    [InlineData(1.90, 28)]
    [InlineData(2.40, 32)]
    public void Matches_the_nearest_physical_pixel_ladder_for_the_display_scale(
        double dpiScale,
        int expectedSize)
    {
        Assert.EndsWith($"-{expectedSize}.png", TrayIconCatalog.SelectFileName(isDarkEffective: false, dpiScale));
    }

    [Fact(DisplayName = "同档位不同主题只切换明暗前缀，尺寸保持就近一致")]
    public void Theme_flip_keeps_the_same_size_ladder()
    {
        var light = TrayIconCatalog.SelectFileName(isDarkEffective: false, dpiScale: 1.75);
        var dark = TrayIconCatalog.SelectFileName(isDarkEffective: true, dpiScale: 1.75);

        Assert.Equal("tray-light-28.png", light);
        Assert.Equal("tray-dark-28.png", dark);
    }
}
