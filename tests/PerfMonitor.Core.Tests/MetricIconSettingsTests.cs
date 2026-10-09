using PerfMonitor.Core.Settings;

namespace PerfMonitor.Core.Tests;

public sealed class MetricIconSettingsTests
{
    [Fact(DisplayName = "图标局部更新保留其他图标、整段与外观偏好")]
    public void Icon_patch_preserves_unrelated_preferences()
    {
        var initial = PerformanceSettings.Default with
        {
            Metrics = new MetricVisibility { Cpu = false },
            MetricIcons = new MetricIconVisibility { Memory = false, Time = false },
            FontSize = 16,
            Theme = BarTheme.Dark
        };

        var updated = initial.Apply(new SettingsPatch
        {
            MetricIcons = new MetricIconsSettingsPatch { Cpu = false, Gpu = false }
        });

        Assert.Equal(initial with
        {
            MetricIcons = initial.MetricIcons with { Cpu = false, Gpu = false }
        }, updated);
        Assert.True(initial.MetricIcons.Cpu);
    }

    [Fact(DisplayName = "关闭及重开整个段不会重置图标偏好")]
    public void Segment_changes_preserve_icon_preferences()
    {
        var initial = PerformanceSettings.Default.Apply(new SettingsPatch
        {
            MetricIcons = new MetricIconsSettingsPatch { Cpu = false }
        });
        var hidden = initial.Apply(new SettingsPatch { Metrics = new MetricsSettingsPatch { Cpu = false } });
        var shown = hidden.Apply(new SettingsPatch { Metrics = new MetricsSettingsPatch { Cpu = true } });

        Assert.Equal(initial, shown);
        Assert.False(hidden.MetricIcons.Cpu);
    }
}
