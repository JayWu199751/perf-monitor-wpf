using PerfMonitor.Core.Shell;

namespace PerfMonitor.Core.Tests;

public sealed class SettingsWindowSizingRulesTests
{
    [Fact(DisplayName = "窗口最大高度跟随当前屏工作区而不是固定 880")]
    public void Max_window_height_follows_the_work_area_instead_of_a_fixed_880()
    {
        Assert.Equal(700d, SettingsWindowSizingRules.ResolveMaxWindowHeight(700d));
        Assert.Equal(1000d, SettingsWindowSizingRules.ResolveMaxWindowHeight(1000d));
        Assert.Equal(540d, SettingsWindowSizingRules.ResolveMaxWindowHeight(540d));
    }

    [Fact(DisplayName = "工作区过小时保底可用高度")]
    public void Very_small_work_areas_fall_back_to_the_minimum_usable_height()
    {
        Assert.Equal(SettingsWindowSizingRules.MinWindowHeight, SettingsWindowSizingRules.ResolveMaxWindowHeight(120d));
        Assert.Equal(SettingsWindowSizingRules.MinWindowHeight, SettingsWindowSizingRules.ResolveMaxWindowHeight(0d));
    }

    [Fact(DisplayName = "非法工作区高度回退保底高度")]
    public void Invalid_work_area_heights_fall_back_to_the_minimum_usable_height()
    {
        Assert.Equal(SettingsWindowSizingRules.MinWindowHeight, SettingsWindowSizingRules.ResolveMaxWindowHeight(double.NaN));
        Assert.Equal(SettingsWindowSizingRules.MinWindowHeight, SettingsWindowSizingRules.ResolveMaxWindowHeight(double.PositiveInfinity));
        Assert.Equal(SettingsWindowSizingRules.MinWindowHeight, SettingsWindowSizingRules.ResolveMaxWindowHeight(-40d));
    }
}
