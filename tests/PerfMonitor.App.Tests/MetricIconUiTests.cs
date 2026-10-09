using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using PerfMonitor.App;
using PerfMonitor.Core.Settings;

namespace PerfMonitor.App.Tests;

public sealed class MetricIconUiTests
{
    private static readonly string[] MetricNames = ["Cpu", "Memory", "Gpu", "Network", "Time"];

    [Theory(DisplayName = "五个图标开关各自更新且关闭段后仍可操作，设置窗重建恢复偏好")]
    [InlineData("Cpu")]
    [InlineData("Memory")]
    [InlineData("Gpu")]
    [InlineData("Network")]
    [InlineData("Time")]
    public void Icon_switches_update_independently_and_survive_window_recreation(string metric) => RunOnSta(() =>
    {
        var current = PerformanceSettings.Default with
        {
            Metrics = new MetricVisibility { Cpu = false, Memory = false, Gpu = false, Network = false, Time = false }
        };
        var originalMetrics = current.Metrics;
        var saveCount = 0;
        PerformanceSettings Update(SettingsPatch patch)
        {
            saveCount++;
            current = current.Apply(patch);
            return current;
        }

        var window = new SettingsWindow(current, Update, recoveredInvalidSettings: false);
        try
        {
            var toggle = Assert.IsType<ToggleButton>(window.FindName(metric + "IconToggle"));
            Assert.True(toggle.IsEnabled);
            toggle.IsChecked = false;
            Assert.Equal(1, saveCount);
            Assert.Equal(originalMetrics, current.Metrics);
        }
        finally
        {
            window.Close();
        }

        var reopened = new SettingsWindow(current, Update, recoveredInvalidSettings: false);
        try
        {
            foreach (var name in MetricNames)
            {
                var toggle = Assert.IsType<ToggleButton>(reopened.FindName(name + "IconToggle"));
                Assert.Equal(name != metric, toggle.IsChecked);
            }
            Assert.Equal(1, saveCount);
        }
        finally
        {
            reopened.Close();
        }
    });

    [Fact(DisplayName = "图标保存失败时开关恢复原值并提示失败")]
    public void Failed_icon_save_restores_the_control_and_shows_failure() => RunOnSta(() =>
    {
        var window = new SettingsWindow(PerformanceSettings.Default,
            _ => throw new IOException("模拟文件写入失败"), recoveredInvalidSettings: false);
        try
        {
            var toggle = Assert.IsType<ToggleButton>(window.FindName("CpuIconToggle"));
            toggle.IsChecked = false;
            Assert.True(toggle.IsChecked);
            Assert.Equal("保存失败", Assert.IsType<TextBlock>(window.FindName("SaveStatusText")).Text);
        }
        finally
        {
            window.Close();
        }
    });

    [Theory(DisplayName = "实际 WPF 图标隐藏移除占位且保留文字，恢复自然宽并维持最小命中区")]
    [InlineData(10, BarTheme.Light)]
    [InlineData(12, BarTheme.Dark)]
    [InlineData(18, BarTheme.Light)]
    public void Real_bar_layout_removes_icon_space_and_restores_width(int fontSize, BarTheme theme) => RunOnSta(() =>
    {
        var vm = new PerformanceBarViewModel();
        var settings = PerformanceSettings.Default with { FontSize = fontSize, Theme = theme };
        vm.ApplySettings(settings);
        // 创建真实 WPF 窗口，但放在屏幕之外，避免验证时覆盖用户桌面。
        var window = new PerformanceBarWindow(() => { }) { DataContext = vm, Left = -20000, Top = -20000 };
        try
        {
            window.Show();
            FlushLayout();
            var fullWidth = window.Width;
            var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            var icons = Descendants<System.Windows.Shapes.Path>(content).ToArray();
            var texts = Descendants<TextBlock>(content).ToArray();
            var originalText = texts.Select(text => text.Text).ToArray();
            Assert.Equal(5, icons.Length);
            Assert.All(icons, icon => Assert.Equal(Visibility.Visible, icon.Visibility));
            // 使用实际测量值，包含当前显示器 DPI 下的布局取整及图标右边距。
            var iconWidth = icons.Sum(icon => icon.DesiredSize.Width);

            vm.ApplySettings(settings with
            {
                MetricIcons = new MetricIconVisibility { Cpu = false, Memory = false, Gpu = false, Network = false, Time = false }
            });
            window.RefreshNaturalWidth();
            FlushLayout();

            Assert.All(icons, icon => Assert.Equal(Visibility.Collapsed, icon.Visibility));
            Assert.All(texts, text => Assert.True(text.IsVisible));
            // 时间读数继续正常走时，只比较其他读数与标签。
            Assert.Equal(originalText[..^1], texts[..^1].Select(text => text.Text).ToArray());
            Assert.Equal(vm.TimeText, texts[^1].Text);
            Assert.InRange(fullWidth - window.Width, iconWidth - 1, iconWidth + 1);

            vm.ApplySettings(settings);
            window.RefreshNaturalWidth();
            FlushLayout();
            Assert.Equal(fullWidth, window.Width);
            Assert.All(icons, icon => Assert.Equal(Visibility.Visible, icon.Visibility));

            vm.ApplySettings(settings with
            {
                Metrics = new MetricVisibility { Cpu = false, Memory = false, Gpu = false, Network = false, Time = false }
            });
            window.RefreshNaturalWidth();
            FlushLayout();
            Assert.Equal(PerformanceBarWindow.CardMinWidthDips + 2 * PerformanceBarWindow.ShadowInsetDips, window.Width);
        }
        finally
        {
            window.Close();
            vm.Dispose();
        }
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed)
            {
                yield return typed;
            }
            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static void FlushLayout() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF 验证线程未在时限内完成。");
        failure?.Throw();
    }
}
