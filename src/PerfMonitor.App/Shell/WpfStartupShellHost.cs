using System.ComponentModel;
using System.IO;
using DrawingIcon = System.Drawing.Icon;
using Forms = System.Windows.Forms;
using PerfMonitor.Core.Shell;
using WpfApplication = System.Windows.Application;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using System.Windows.Controls.Primitives;

namespace PerfMonitor.App.Shell;

internal sealed class WpfStartupShellHost : IStartupShellHost, IDisposable
{
    private readonly WpfApplication _application;
    private readonly WpfContextMenu _sharedContextMenu = new();
    private PerformanceBarWindow? _performanceBar;
    private SettingsWindow? _settingsWindow;
    private Forms.NotifyIcon? _trayIcon;
    private Action? _performanceBarMoveRequestHandler;
    private bool _shutdownRequested;
    private bool _disposed;

    public WpfStartupShellHost(WpfApplication application)
    {
        _application = application;
    }

    public event EventHandler? PerformanceBarRightClickRequested;

    public event EventHandler? PerformanceBarClosed;

    public event EventHandler? SettingsWindowCloseRequested;

    public void SetPerformanceBarMoveRequestHandler(Action handler)
    {
        _performanceBarMoveRequestHandler = handler;
    }

    public void BeginPerformanceBarNativeMove()
    {
        _performanceBar?.BeginNativeMove();
    }

    public void ShowPerformanceBar(bool activate)
    {
        EnsurePerformanceBar();
        _performanceBar!.ShowActivated = activate;
        _performanceBar.Show();

        if (activate)
        {
            _performanceBar.Activate();
        }
    }

    public void SetPerformanceBarVisible(bool visible, bool activate)
    {
        EnsurePerformanceBar();

        if (!visible)
        {
            _performanceBar!.Hide();
            return;
        }

        _performanceBar!.ShowActivated = activate;
        _performanceBar.Show();

        if (activate)
        {
            _performanceBar.Activate();
        }
    }

    public void CreateTrayIcon(Action leftClick, Action rightClick)
    {
        if (_trayIcon is not null)
        {
            return;
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "icon.ico");
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = new DrawingIcon(iconPath),
            Text = "性能小窗",
            Visible = true
        };
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                leftClick();
            }
            else if (e.Button == Forms.MouseButtons.Right)
            {
                rightClick();
            }
        };
    }

    public void ShowContextMenu(
        ShellMenuOrigin origin,
        IReadOnlyList<ShellMenuItem> items,
        Action<ShellMenuAction> selectItem)
    {
        _sharedContextMenu.IsOpen = false;
        _sharedContextMenu.Items.Clear();

        foreach (var item in items)
        {
            var menuItem = new WpfMenuItem { Header = item.Label };
            menuItem.Click += (_, _) => selectItem(item.Action);
            _sharedContextMenu.Items.Add(menuItem);
        }

        _sharedContextMenu.PlacementTarget = _performanceBar;
        _sharedContextMenu.Placement = PlacementMode.MousePoint;
        _sharedContextMenu.IsOpen = true;
    }

    public void ShowSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closing += OnSettingsWindowClosing;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void HideSettingsWindow()
    {
        _settingsWindow?.Hide();
    }

    public void Shutdown()
    {
        if (_shutdownRequested)
        {
            return;
        }

        _shutdownRequested = true;
        _sharedContextMenu.IsOpen = false;
        DisposeTrayIcon();

        if (_settingsWindow is not null)
        {
            _settingsWindow.Closing -= OnSettingsWindowClosing;
            _settingsWindow.Close();
            _settingsWindow = null;
        }

        _application.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sharedContextMenu.IsOpen = false;
        _sharedContextMenu.Items.Clear();
        DisposeTrayIcon();
    }

    private void EnsurePerformanceBar()
    {
        if (_performanceBar is not null)
        {
            return;
        }

        _performanceBar = new PerformanceBarWindow(
            () => _performanceBarMoveRequestHandler?.Invoke());
        _performanceBar.ContextMenuRequested += (_, _) => PerformanceBarRightClickRequested?.Invoke(this, EventArgs.Empty);
        _performanceBar.Closed += (_, _) =>
        {
            if (!_shutdownRequested)
            {
                PerformanceBarClosed?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    private void OnSettingsWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownRequested)
        {
            return;
        }

        e.Cancel = true;
        SettingsWindowCloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
        {
            return;
        }

        var icon = _trayIcon.Icon;
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        icon?.Dispose();
        _trayIcon = null;
    }
}
