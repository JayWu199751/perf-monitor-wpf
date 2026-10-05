using System.Windows;
using PerfMonitor.App;
using PerfMonitor.Core.Settings;

namespace PerfMonitor.App.Tests;

public sealed class PerformanceBarViewModelSettingsTests
{
    [Fact(DisplayName = "透明显示开合按设置更新边框粗细")]
    public void Toggling_transparent_display_updates_border_thickness()
    {
        var vm = new PerformanceBarViewModel();
        try
        {
            vm.ApplySettings(PerformanceSettings.Default with { TransparentDisplay = true });
            var transparentThickness = vm.BorderThickness;

            vm.ApplySettings(PerformanceSettings.Default with { TransparentDisplay = false });

            Assert.Equal(new Thickness(0), transparentThickness);
            Assert.Equal(new Thickness(1), vm.BorderThickness);
        }
        finally
        {
            vm.Dispose();
        }
    }

    [Fact(DisplayName = "字号变化更新字号与派生间距属性")]
    public void Changing_font_size_updates_font_size_properties()
    {
        var vm = new PerformanceBarViewModel();
        try
        {
            var before = vm.FontSize;

            vm.ApplySettings(PerformanceSettings.Default with { FontSize = 14 });

            Assert.Equal(14, vm.FontSize);
            Assert.NotEqual(before, vm.FontSize);
        }
        finally
        {
            vm.Dispose();
        }
    }
}
