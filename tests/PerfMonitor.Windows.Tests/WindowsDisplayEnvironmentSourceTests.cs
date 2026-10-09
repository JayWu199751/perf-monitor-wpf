using System.Runtime.InteropServices;
using PerfMonitor.Windows.Displays;
using SharedNativeMethods = PerfMonitor.Windows.Native.NativeMethods;

namespace PerfMonitor.Windows.Tests;

public sealed class WindowsDisplayEnvironmentSourceTests
{
    [Fact(DisplayName = "显示器缩放比例与 PerMonitorV2 下的原生有效 DPI 一致")]
    public void Display_scale_matches_native_effective_dpi()
    {
        // xUnit 宿主没有应用 manifest，显式使用与产品相同的 DPI 上下文。
        var previousContext = SetThreadDpiAwarenessContext(new nint(-4));
        Assert.NotEqual(nint.Zero, previousContext);
        try
        {
            using var source = new WindowsDisplayEnvironmentSource();
            var displays = source.GetDisplays();
            var nativeReadings = new List<(int X, int Y, int Result, uint Dpi)>();
            var callback = new MonitorEnumProc((nint monitor, nint hdc, ref SharedNativeMethods.NativeRect rect, nint data) =>
            {
                // 独立声明 HRESULT，避免测试重复使用错误的 P/Invoke 契约。
                var result = GetDpiForMonitor(monitor, 0, out var dpiX, out _);
                nativeReadings.Add((rect.Left, rect.Top, result, dpiX));
                return true;
            });
            Assert.True(EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero));
            Assert.NotEmpty(nativeReadings);
            Assert.Equal(displays.Count, nativeReadings.Count);
            foreach (var reading in nativeReadings)
            {
                Assert.Equal(0, reading.Result);
                Assert.True(reading.Dpi > 0);
                var display = Assert.Single(displays, display =>
                    display.Bounds.X == reading.X && display.Bounds.Y == reading.Y);
                Assert.Equal(reading.Dpi / 96.0, display.DpiScale);
            }
            GC.KeepAlive(callback);
        }
        finally
        {
            SetThreadDpiAwarenessContext(previousContext);
        }
    }

    [Fact(DisplayName = "显示器 DPI 查询失败时安全回退到 100% 缩放")]
    public void Failed_monitor_query_falls_back_to_default_scale()
    {
        Assert.Equal(1.0, WindowsDisplayEnvironmentSource.GetDpiScale(nint.Zero));
    }

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

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, ref SharedNativeMethods.NativeRect rect, nint data);

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clipRect, MonitorEnumProc callback, nint data);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
