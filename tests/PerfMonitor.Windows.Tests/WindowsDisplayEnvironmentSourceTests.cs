using PerfMonitor.Windows.Displays;

namespace PerfMonitor.Windows.Tests;

public sealed class WindowsDisplayEnvironmentSourceTests
{
    [Fact(DisplayName = "Windows 显示器 adapter 能枚举至少一台显示器且工作区在边界内")]
    public void Enumerates_at_least_one_display_with_work_area_inside_bounds()
    {
        using var source = new WindowsDisplayEnvironmentSource();

        var displays = source.GetDisplays();

        Assert.NotEmpty(displays);
        foreach (var display in displays)
        {
            Assert.True(display.Bounds.Width > 0);
            Assert.True(display.Bounds.Height > 0);
            Assert.True(display.WorkArea.Width > 0);
            Assert.True(display.WorkArea.Height > 0);
            Assert.True(
                display.WorkArea.X >= display.Bounds.X &&
                display.WorkArea.Y >= display.Bounds.Y &&
                display.WorkArea.Right <= display.Bounds.Right &&
                display.WorkArea.Bottom <= display.Bounds.Bottom);
            Assert.True(display.DpiScale >= 1.0);
        }
    }

    [Fact(DisplayName = "重复枚举返回一致且每次独立的显示器列表")]
    public void Repeated_enumeration_returns_consistent_and_independent_lists()
    {
        using var source = new WindowsDisplayEnvironmentSource();

        var first = source.GetDisplays();
        var second = source.GetDisplays();

        Assert.Equal(first.Count, second.Count);
        Assert.NotSame(first, second);
        for (var index = 0; index < first.Count; index++)
        {
            Assert.Equal(first[index], second[index]);
        }
    }

    [Fact(DisplayName = "订阅显示器变化事件后释放不抛异常且可重复释放")]
    public void Disposal_after_subscribing_displays_changed_is_idempotent()
    {
        var source = new WindowsDisplayEnvironmentSource();
        source.DisplaysChanged += (_, _) => { };

        source.Dispose();
        source.Dispose();

        Assert.Empty(source.GetDisplays());
    }
}
