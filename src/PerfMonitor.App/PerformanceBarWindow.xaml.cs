using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PerfMonitor.App;

public partial class PerformanceBarWindow : Window
{
    private readonly Action _requestNativeMove;

    public PerformanceBarWindow(Action requestNativeMove)
    {
        _requestNativeMove = requestNativeMove;
        InitializeComponent();

        // 分层窗口的 alpha=0 像素会穿透输入；alpha=1 保留近透明外观并接收命中。
        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
    }

    public event EventHandler? ContextMenuRequested;

    internal void BeginNativeMove() => DragMove();

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
