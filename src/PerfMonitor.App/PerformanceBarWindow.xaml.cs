using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfSize = System.Windows.Size;

namespace PerfMonitor.App;

public partial class PerformanceBarWindow : Window
{
    private readonly Action _requestNativeMove;
    private double _peakWidth = 64;
    private bool _pendingWidthReset;

    public PerformanceBarWindow(Action requestNativeMove)
    {
        _requestNativeMove = requestNativeMove;
        InitializeComponent();

        // 分层窗口中，Alpha=0 的完全透明像素会穿透输入；Alpha=1 保持近透明外观并接收命中。
        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
        Loaded += (_, _) => RefreshNaturalWidth(resetPeakWidth: _pendingWidthReset);
    }

    public event EventHandler? ContextMenuRequested;

    internal void BeginNativeMove() => DragMove();

    internal void RefreshNaturalWidth(bool resetPeakWidth = false)
    {
        _pendingWidthReset |= resetPeakWidth;
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

            if (_pendingWidthReset)
            {
                _peakWidth = MinWidth;
                Width = _peakWidth;
                _pendingWidthReset = false;
            }

            if (Content is not FrameworkElement content)
            {
                return;
            }

            content.Measure(new WpfSize(double.PositiveInfinity, double.PositiveInfinity));
            var desiredWidth = Math.Max(MinWidth, Math.Ceiling(content.DesiredSize.Width));
            if (desiredWidth > _peakWidth)
            {
                _peakWidth = desiredWidth;
                Width = _peakWidth;
            }
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
