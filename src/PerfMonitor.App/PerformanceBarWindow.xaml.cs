using System.Windows;
using System.Windows.Input;

namespace PerfMonitor.App;

public partial class PerformanceBarWindow : Window
{
    public PerformanceBarWindow()
    {
        InitializeComponent();
    }

    public event EventHandler? ContextMenuRequested;

    private void OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ContextMenuRequested?.Invoke(this, EventArgs.Empty);
    }
}
