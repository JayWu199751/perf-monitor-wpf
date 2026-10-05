using System.ComponentModel;
using System.IO;
using DrawingIcon = System.Drawing.Icon;
using Forms = System.Windows.Forms;
using PerfMonitor.Core.Shell;
using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.Settings;
using PerfMonitor.Windows.Displays;
using PerfMonitor.Windows.Fullscreen;
using PerfMonitor.Windows.Metrics;
using PerfMonitor.Windows.ScheduledTasks;
using PerfMonitor.Windows.Settings;
using PerfMonitor.Windows.Shell;
using WpfApplication = System.Windows.Application;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace PerfMonitor.App.Shell;

internal sealed class WpfStartupShellHost : IStartupShellHost, IDisposable
{
    private readonly WpfApplication _application;
    private readonly WpfContextMenu _sharedContextMenu = new();
    private readonly PerformanceBarViewModel _performanceBarViewModel = new();
    private readonly WindowsSystemMetricsSource _systemMetricsSource = new();
    private readonly WindowsSlowMetricsSource _slowMetricsSource = new();
    private readonly WindowsDisplayEnvironmentSource _displayEnvironmentSource = new();
    private readonly WindowsFullscreenWatcher _fullscreenWatcher = new();
    private readonly ISettingsStore _settingsStore;
    private PerformanceSettings _currentSettings = PerformanceSettings.Default;
    private Func<SettingsPatch, PerformanceSettings>? _updateSettings;
    private PerformanceBarWindow? _performanceBar;
    private SettingsWindow? _settingsWindow;
    private Forms.NotifyIcon? _trayIcon;
    private Action? _performanceBarMoveRequestHandler;
    private bool _shutdownRequested;
    private volatile bool _disposed;

    public WpfStartupShellHost(WpfApplication application, ISettingsStore settingsStore)
    {
        _application = application;
        _settingsStore = settingsStore;
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

    public ISystemMetricsSource? SystemMetricsSource => _systemMetricsSource;

    public ISlowMetricsSource? SlowMetricsSource => _slowMetricsSource;

    public IDisplayEnvironmentSource? DisplayEnvironmentSource => _displayEnvironmentSource;

    public IPerformanceBarPlacementPort? PerformanceBarPlacement => _performanceBar;

    public ITaskbarVisibilityGuardPort? TaskbarVisibilityGuard => _taskbarGuard ??= CreateTaskbarGuard();

    public IFullscreenWatcher? FullscreenWatcher => _fullscreenWatcher;

    private DispatchedTaskbarGuard? _taskbarGuard;

    private DispatchedTaskbarGuard CreateTaskbarGuard()
    {
        var inner = new WindowsTaskbarVisibilityGuard(() =>
            _performanceBar is { IsLoaded: true } bar && !_shutdownRequested ? bar.RootWindowHandle : nint.Zero);
        return new DispatchedTaskbarGuard(inner, _application.Dispatcher);
    }

    /// <summary>
    /// 把守卫端口调用编组到 UI 线程执行（SetWindowPos 作用于 UI 线程所属窗口）；
    /// pending 标志防止 UI 繁忙时回调积压。
    /// </summary>
    private sealed class DispatchedTaskbarGuard : ITaskbarVisibilityGuardPort
    {
        private readonly WindowsTaskbarVisibilityGuard _inner;
        private readonly Dispatcher _dispatcher;
        private volatile bool _fastPending;
        private volatile bool _slowPending;
        private volatile bool _stopPending;

        public DispatchedTaskbarGuard(WindowsTaskbarVisibilityGuard inner, Dispatcher dispatcher)
        {
            _inner = inner;
            _dispatcher = dispatcher;
        }

        public void EnsureAboveTaskbar() => BeginInvoke(() => _fastPending, value => _fastPending = value, _inner.EnsureAboveTaskbar);

        public void RefreshTaskbarHandles() => BeginInvoke(() => _slowPending, value => _slowPending = value, _inner.RefreshTaskbarHandles);

        public void OnGuardStopped() => BeginInvoke(() => _stopPending, value => _stopPending = value, _inner.OnGuardStopped);

        private void BeginInvoke(Func<bool> isPending, Action<bool> setPending, Action action)
        {
            if (isPending() || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            {
                return;
            }

            setPending(true);
            _ = _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                setPending(false);
                action();
            }));
        }
    }

    public ISettingsStore? SettingsStore => _settingsStore;

    public IAutostartPort? AutostartPort
    {
        get
        {
#if DEBUG
            // Debug 构建不创建或修改系统自启；设置窗中该开关禁用并注明原因。
            return null;
#else
            _autostartPort ??= new WindowsScheduledTaskAutostart();
            return _autostartPort;
#endif
        }
    }

#if !DEBUG
    private WindowsScheduledTaskAutostart? _autostartPort;
#endif

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
            if (item.Action == ShellMenuAction.Separator)
            {
                _sharedContextMenu.Items.Add(new System.Windows.Controls.Separator());
                continue;
            }

            var menuItem = new WpfMenuItem
            {
                Header = item.Label,
                IsCheckable = item.IsCheckable,
                IsChecked = item.IsChecked
            };
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
            var updateSettings = _updateSettings
                ?? throw new InvalidOperationException("设置窗必须使用控制器提供的设置更新入口。");
            _settingsWindow = new SettingsWindow(
                _currentSettings,
                updateSettings,
                _settingsStore.RecoveredInvalidSettingsOnLastLoad);
            _settingsWindow.Closing += OnSettingsWindowClosing;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void ShowSettingsWindow(
        PerformanceSettings settings,
        Func<SettingsPatch, PerformanceSettings> updateSettings,
        bool recoveredInvalidSettings)
    {
        _currentSettings = settings;
        _updateSettings = updateSettings;
        _settingsWindow?.ApplySettings(settings, recoveredInvalidSettings);
        ShowSettingsWindow();
    }

    public void HideSettingsWindow()
    {
        _settingsWindow?.Hide();
    }

    public void SetMetricGeneration(long generation) =>
        _performanceBarViewModel.SetMetricGeneration(generation);

    public void ApplySettings(PerformanceSettings settings)
    {
        _currentSettings = settings;
        var resetWidth = _performanceBarViewModel.ApplySettings(settings);
        _performanceBar?.RefreshNaturalWidth(resetWidth);
        _settingsWindow?.ApplySettings(settings, _settingsStore.RecoveredInvalidSettingsOnLastLoad);
    }

    public void UpdatePerformanceMetrics(PerformanceMetricsSnapshot snapshot)
    {
        var dispatcher = _application.Dispatcher;
        if (_disposed || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            _ = dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!_disposed && !dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                {
                    _performanceBarViewModel.Apply(snapshot);
                    _performanceBar?.RefreshNaturalWidth();
                }
            }));
        }
        catch (InvalidOperationException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
        }
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
        _fullscreenWatcher.Dispose();
        _sharedContextMenu.IsOpen = false;
        _sharedContextMenu.Items.Clear();
        DisposeTrayIcon();
        _performanceBarViewModel.Dispose();
        _displayEnvironmentSource.Dispose();
    }

    private void EnsurePerformanceBar()
    {
        if (_performanceBar is not null)
        {
            return;
        }

        _performanceBar = new PerformanceBarWindow(
            () => _performanceBarMoveRequestHandler?.Invoke())
        {
            DataContext = _performanceBarViewModel
        };
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
