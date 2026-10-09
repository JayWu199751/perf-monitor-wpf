using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using PerfMonitor.App;
using PerfMonitor.Core.Settings;
using PerfMonitor.Core.Shell;
using SharedNativeMethods = PerfMonitor.Windows.Native.NativeMethods;

namespace PerfMonitor.App.Tests;

public sealed class SettingsWindowDpiTests
{
    [Fact(DisplayName = "设置窗最大高度按所在显示器真实 DPI 将工作区换算成 DIP")]
    public void Maximum_height_uses_monitor_work_area_in_dips()
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            var previousContext = SetThreadDpiAwarenessContext(new nint(-4));
            SettingsWindow? window = null;
            try
            {
                Assert.NotEqual(nint.Zero, previousContext);
                window = new SettingsWindow(PerformanceSettings.Default,
                    patch => PerformanceSettings.Default.Apply(patch), false)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000, Top = -20000, ShowActivated = false
                };
                window.Show();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var handle = new WindowInteropHelper(window).Handle;
                var monitor = SharedNativeMethods.MonitorFromWindow(handle, SharedNativeMethods.MonitorDefaultToNearest);
                var info = new SharedNativeMethods.MonitorInfoW
                {
                    CbSize = (uint)Marshal.SizeOf<SharedNativeMethods.MonitorInfoW>()
                };
                Assert.True(SharedNativeMethods.GetMonitorInfoW(monitor, ref info));
                // 独立读取窗口 DPI，不能复用被测的 GetDpiForMonitor 成功判断。
                var dpi = GetDpiForWindow(handle);
                Assert.True(dpi > 0);
                var workAreaDips = (info.RcWork.Bottom - info.RcWork.Top) / (dpi / 96.0);
                Assert.Equal(SettingsWindowSizingRules.ResolveMaxWindowHeight(workAreaDips), window.MaxHeight, 6);
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
                if (previousContext != nint.Zero)
                    SetThreadDpiAwarenessContext(previousContext);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF DPI 验证线程未在时限内完成。");
        failure?.Throw();
    }

    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
}
