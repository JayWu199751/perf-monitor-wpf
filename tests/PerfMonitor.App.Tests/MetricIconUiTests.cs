using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using PerfMonitor.App;
using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.Settings;

namespace PerfMonitor.App.Tests;

public sealed class MetricIconUiTests
{
    private static readonly string[] MetricNames = ["Cpu", "Memory", "Gpu", "Network", "Time"];

    [Fact(DisplayName = "设置窗重开和重建均无图标设置，整段开关继续正常保存")]
    public void Settings_windows_have_no_icon_controls_and_keep_metric_switches() => RunOnSta(() =>
    {
        var current = PerformanceSettings.Default;
        var saveCount = 0;
        PerformanceSettings Update(SettingsPatch patch)
        {
            saveCount++;
            return current = current.Apply(patch);
        }

        for (var instance = 0; instance < 2; instance++)
        {
            var window = new SettingsWindow(current, Update, recoveredInvalidSettings: false)
            {
                Left = -20000, Top = -20000
            };
            try
            {
                window.Show();
                FlushLayout();
                AssertNoIconSettings(window);
                var cpu = Assert.IsType<ToggleButton>(window.FindName("CpuToggle"));
                Assert.Equal(instance == 0, cpu.IsChecked);
                if (instance == 0)
                {
                    cpu.IsChecked = false;
                    Assert.False(current.Metrics.Cpu);
                    window.Hide();
                    window.Show();
                    FlushLayout();
                    AssertNoIconSettings(window);
                    Assert.False(cpu.IsChecked);
                }
                Assert.Equal(1, saveCount);
            }
            finally
            {
                window.Close();
            }
        }
    });

    [Theory(DisplayName = "无图标性能条保留文字和网络箭头，无专属占位并维持自然宽及最小命中区")]
    [InlineData(10, BarTheme.Light)]
    [InlineData(10, BarTheme.Dark)]
    [InlineData(12, BarTheme.Light)]
    [InlineData(12, BarTheme.Dark)]
    [InlineData(18, BarTheme.Light)]
    [InlineData(18, BarTheme.Dark)]
    public void Real_bar_has_no_icons_and_retains_compact_text_layout(int fontSize, BarTheme theme) => RunOnSta(() =>
    {
        var vm = new PerformanceBarViewModel();
        var settings = PerformanceSettings.Default with { FontSize = fontSize, Theme = theme };
        vm.ApplySettings(settings);
        vm.SetMetricGeneration(1);
        vm.Apply(new PerformanceMetricsSnapshot(
            1, CpuPercentage: 9, MemoryPercentage: 51, MemoryUsedGiB: null, MemoryTotalGiB: null,
            DateTimeOffset.UtcNow, GpuPercentage: 42, GpuMemoryPercentage: 77,
            GpuTemperatureCelsius: 29, CpuTemperatureCelsius: 62,
            NetworkDownloadMegabytesPerSecond: 1.23, NetworkUploadMegabytesPerSecond: 0.45));
        // 在屏幕之外创建真实 WPF 窗口，验证布局但不覆盖用户桌面。
        var window = new PerformanceBarWindow(() => { }) { DataContext = vm, Left = -20000, Top = -20000 };
        try
        {
            window.Show();
            FlushLayout();
            var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            Assert.Empty(Descendants<System.Windows.Shapes.Path>(content));
            var texts = Descendants<TextBlock>(content).ToArray();
            Assert.All(texts, text => Assert.True(text.IsVisible));
            Assert.Contains(texts, text => text.Text == vm.CpuPercentageText);
            Assert.Contains(texts, text => text.Text == vm.GpuMemoryPercentageText);
            Assert.Contains(texts, text => text.Text == vm.NetworkDownloadSpeedText);
            // DesiredSize 包含边距，ActualWidth 只包含元素；允许 DPI 布局取整误差。
            Assert.All(texts, text => Assert.InRange(
                text.DesiredSize.Width - text.Margin.Left - text.Margin.Right - text.ActualWidth, -1, 1));
            foreach (var label in new[] { "CPU", "内存", "GPU", "显存", "网络", "↓", "↑", "MB/s" })
            {
                Assert.Contains(texts, text => text.Text == label);
            }
            Assert.Equal(vm.TimeText, texts[^1].Text);

            var card = Assert.IsType<Border>(window.FindName("Card"));
            var segments = Assert.IsType<StackPanel>(card.Child).Children.OfType<Border>()
                .Where(border => border.Child is StackPanel).ToArray();
            Assert.Equal(5, segments.Length);
            foreach (var segment in segments)
            {
                var first = Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(segment.Child).Children[0]);
                Assert.Equal(new Thickness(0), first.Margin);
            }
            content.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.Equal(Math.Max(window.MinWidth, Math.Ceiling(content.DesiredSize.Width)), window.Width);
            var fullWidth = window.Width;

            vm.ApplySettings(settings with { TransparentDisplay = true });
            window.RefreshNaturalWidth();
            FlushLayout();
            Assert.Empty(Descendants<System.Windows.Shapes.Path>(content));
            Assert.All(texts, text => Assert.True(text.IsVisible));

            vm.ApplySettings(settings with
            {
                Metrics = new MetricVisibility { Cpu = false, Memory = false, Gpu = false, Network = false, Time = false }
            });
            window.RefreshNaturalWidth();
            FlushLayout();
            Assert.Equal(PerformanceBarWindow.CardMinWidthDips + 2 * PerformanceBarWindow.ShadowInsetDips, window.Width);
            Assert.All(segments, segment => Assert.Equal(Visibility.Collapsed, segment.Visibility));

            vm.ApplySettings(settings);
            window.RefreshNaturalWidth();
            FlushLayout();
            Assert.Equal(fullWidth, window.Width);
            Assert.Empty(Descendants<System.Windows.Shapes.Path>(content));
        }
        finally
        {
            window.Close();
            vm.Dispose();
        }
    });

    private static void AssertNoIconSettings(SettingsWindow window)
    {
        foreach (var metric in MetricNames)
        {
            Assert.Null(window.FindName(metric + "IconToggle"));
            Assert.IsType<ToggleButton>(window.FindName(metric + "Toggle"));
        }
        Assert.DoesNotContain(Descendants<TextBlock>(window), text => text.Text == "显示图标");
    }
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
