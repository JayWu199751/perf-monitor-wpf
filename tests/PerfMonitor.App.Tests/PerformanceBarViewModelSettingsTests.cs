using PerfMonitor.App;
using PerfMonitor.Core.Settings;

namespace PerfMonitor.App.Tests;

public sealed class PerformanceBarViewModelSettingsTests
{
    [Fact(DisplayName = "透明显示开合不触发布局宽重算（保留峰值宽）")]
    public void Toggling_transparent_display_does_not_report_a_layout_change()
    {
        var vm = new PerformanceBarViewModel();
        var transparent = PerformanceSettings.Default with { TransparentDisplay = true };

        vm.ApplySettings(transparent);

        var result = vm.ApplySettings(transparent with { TransparentDisplay = false });

        vm.Dispose();
        Assert.False(result, "透明显示变化保留峰值宽，不得触发自然宽重算。");
    }

    [Fact(DisplayName = "透明显示关闭到开启同样不触发布局宽重算")]
    public void Enabling_transparent_display_does_not_report_a_layout_change()
    {
        var vm = new PerformanceBarViewModel();

        var result = vm.ApplySettings(PerformanceSettings.Default with { TransparentDisplay = true });

        vm.Dispose();
        Assert.False(result, "透明显示变化保留峰值宽，不得触发自然宽重算。");
    }

    [Fact(DisplayName = "字号或指标组合变化仍触发布局宽重算（重置峰值）")]
    public void Changing_font_size_still_reports_a_layout_change()
    {
        var vm = new PerformanceBarViewModel();

        var result = vm.ApplySettings(PerformanceSettings.Default with { FontSize = 14 });

        vm.Dispose();
        Assert.True(result);
    }
}
