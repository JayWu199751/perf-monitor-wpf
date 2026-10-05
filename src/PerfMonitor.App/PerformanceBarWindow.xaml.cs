using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PerfMonitor.Core.Shell;
using PerfMonitor.Windows.Displays;
using WpfSize = System.Windows.Size;

namespace PerfMonitor.App;

public partial class PerformanceBarWindow : Window, IPerformanceBarPlacementPort
{
    private readonly Action _requestNativeMove;

    public PerformanceBarWindow(Action requestNativeMove)
    {
        _requestNativeMove = requestNativeMove;
        InitializeComponent();

        // 分层窗口中，Alpha=0 的完全透明像素会穿透输入；Alpha=1 保持近透明外观并接收命中。
        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
        Loaded += (_, _) => RefreshNaturalWidth();
    }

    public event EventHandler? ContextMenuRequested;

    internal void BeginNativeMove() => DragMove();

    public PlacementRect ReadFrame() => NativeWindowFrame.ReadFrame(WindowHandle);

    public void SetFramePosition(double x, double y) =>
        NativeWindowFrame.SetPosition(WindowHandle, x, y);

    /// <summary>卡片根窗口句柄；供任务栏 z-order 守卫等原生检查使用。</summary>
    internal nint RootWindowHandle => WindowHandle;

    /// <summary>当前窗口的 DPI 缩放比；托盘图标按 16 × 该值就近选物理像素档位。</summary>
    internal double DpiScale => PresentationSource.FromVisual(this) is { } source
        ? source.CompositionTarget.TransformToDevice.M11
        : 1.0;

    private nint WindowHandle => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>
    /// 把窗口宽度设为内容自然宽，可增可减，下限为 64 DIP 命中区；高度由 SizeToContent 跟随。
    /// 个位数读数的等数字宽占位符使 9% 与 10% 同宽，宽度变化集中在位数增减边界；
    /// 重复相同宽度不触发尺寸变化，也就不会重摆窗口。
    /// </summary>
    internal void RefreshNaturalWidth()
    {
        if (!IsLoaded || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!IsLoaded || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return;
            }

            if (Content is not FrameworkElement content)
            {
                return;
            }

            content.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
            Width = Math.Max(MinWidth, Math.Ceiling(content.DesiredSize.Width));
        }));
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (e.Handled)
        {
            return;
        }

        e.Handled = true;
        _requestNativeMove();
    }

    private void OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ContextMenuRequested?.Invoke(this, EventArgs.Empty);
    }
}
