using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PerfMonitor.Core.Shell;
using PerfMonitor.Windows.Displays;
using PerfMonitor.Windows.Shell;
using WpfSize = System.Windows.Size;

namespace PerfMonitor.App;

public partial class PerformanceBarWindow : Window, IPerformanceBarPlacementPort
{
    /// <summary>卡片四周的透明留白（DIP），用于容纳投影；摆放数学按视觉矩形内缩补偿。</summary>
    internal const double ShadowInsetDips = 16;

    /// <summary>卡片可视内容的最小宽度（DIP）；窗口最小宽度在此基础上加两侧留白。</summary>
    internal const double CardMinWidthDips = 64;

    private readonly Action _requestNativeMove;

    public PerformanceBarWindow(Action requestNativeMove)
    {
        _requestNativeMove = requestNativeMove;
        InitializeComponent();

        MinWidth = CardMinWidthDips + ShadowInsetDips * 2;
        // 窗口背景为 null：透明边距环依赖分层窗口的 alpha=0 系统级穿透，
        // 命中区收敛到卡片本身（含透明显示模式，卡片背景保持 alpha=1）。
        Loaded += (_, _) => RefreshNaturalWidth();
        // 胶囊形：圆角跟随卡片渲染高度的一半，与字号/内容变化解耦。
        Card.SizeChanged += (_, args) => Card.CornerRadius = new CornerRadius(args.NewSize.Height / 2);
        // WPF 以隐藏所有者窗口实现 ShowInTaskbar=false：任务栏无按钮，但 Alt+Tab 仍列出悬浮卡片；
        // 样式设置完成后补写 WS_EX_TOOLWINDOW，从任务切换器整体隐匿（ADR 0007）。
        Loaded += (_, _) => NativeWindowStyles.ExcludeFromTaskSwitcher(WindowHandle);
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
    /// 把窗口宽度设为内容自然宽，可增可减，下限为 64 DIP 可视内容 + 两侧投影留白；高度由 SizeToContent 跟随。
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
