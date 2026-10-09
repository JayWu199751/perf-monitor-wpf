using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PerfMonitor.Core.Settings;
using PerfMonitor.Core.Shell;
using SharedNativeMethods = PerfMonitor.Windows.Native.NativeMethods;
using NativeWindowStyles = PerfMonitor.Windows.Shell.NativeWindowStyles;
using WindowInteropHelper = System.Windows.Interop.WindowInteropHelper;
using ComboBox = System.Windows.Controls.ComboBox;
using Color = System.Windows.Media.Color;
using Microsoft.Win32;

namespace PerfMonitor.App;

public partial class SettingsWindow : Window
{
    private static readonly TimeSpan SavedMessageDuration = TimeSpan.FromSeconds(2);
    private readonly Func<SettingsPatch, PerformanceSettings> _updateSettings;
    private readonly DispatcherTimer _savedMessageTimer;
    private PerformanceSettings _currentSettings;
    private bool _isSynchronizingControls = true;
    private bool _isListeningForThemeChanges;

    public SettingsWindow(
        PerformanceSettings settings,
        Func<SettingsPatch, PerformanceSettings> updateSettings,
        bool recoveredInvalidSettings)
    {
        _currentSettings = settings.Validate();
        _updateSettings = updateSettings;
        InitializeComponent();
        _savedMessageTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = SavedMessageDuration
        };
        _savedMessageTimer.Tick += OnSavedMessageTimerTick;
        Closed += (_, _) => _savedMessageTimer.Stop();
        IsVisibleChanged += OnIsVisibleChanged;
        Closed += (_, _) => StopListeningForThemeChanges();
        SourceInitialized += (_, _) => UpdateMaxHeightForCurrentScreen();
        LocationChanged += (_, _) => UpdateMaxHeightForCurrentScreen();
        // ShowInTaskbar=false 的所有权机制不影响 Alt+Tab；Loaded 后补工具窗口样式隐匿任务切换器（ADR 0007）。
        Loaded += (_, _) => NativeWindowStyles.ExcludeFromTaskSwitcher(new WindowInteropHelper(this).Handle);
#if DEBUG
        AutostartToggle.IsEnabled = false;
        AutostartDisabledNote.Visibility = Visibility.Visible;
#endif
        ResynchronizeControls();
        if (recoveredInvalidSettings)
        {
            ShowStatus("配置损坏，已备份原文件并恢复默认设置", isError: true, persistent: true);
        }
    }    public void ApplySettings(PerformanceSettings settings, bool recoveredInvalidSettings)
    {
        _currentSettings = settings.Validate();
        ResynchronizeControls();
        if (recoveredInvalidSettings && string.IsNullOrEmpty(SaveStatusText.Text))
        {
            ShowStatus("配置损坏，已备份原文件并恢复默认设置", isError: true, persistent: true);
        }
    }

    private void MetricChanged(object sender, RoutedEventArgs e)
    {
        if (_isSynchronizingControls || sender is not ToggleButton toggle || toggle.Tag is not string metric)
        {
            return;
        }

        var enabled = toggle.IsChecked == true;
        var patch = metric switch
        {
            "Cpu" => new SettingsPatch { Metrics = new MetricsSettingsPatch { Cpu = enabled } },
            "Memory" => new SettingsPatch { Metrics = new MetricsSettingsPatch { Memory = enabled } },
            "Gpu" => new SettingsPatch { Metrics = new MetricsSettingsPatch { Gpu = enabled } },
            "Network" => new SettingsPatch { Metrics = new MetricsSettingsPatch { Network = enabled } },
            "Time" => new SettingsPatch { Metrics = new MetricsSettingsPatch { Time = enabled } },
            _ => null
        };
        if (patch is not null)
        {
            Save(patch);
        }
    }

    private void BehaviorChanged(object sender, RoutedEventArgs e)
    {
        if (_isSynchronizingControls || sender is not ToggleButton toggle || toggle.Tag is not string behavior)
        {
            return;
        }

        var enabled = toggle.IsChecked == true;
        var patch = behavior switch
        {
            "Autostart" => new SettingsPatch { Autostart = enabled },
            "AutoHideOnFullscreen" => new SettingsPatch { AutoHideOnFullscreen = enabled },
            _ => null
        };
        if (patch is not null)
        {
            Save(patch);
        }
    }

    private void FastRefreshChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isSynchronizingControls && TryGetSelectedInteger(FastRefreshComboBox, out var milliseconds))
        {
            Save(new SettingsPatch { FastRefreshMilliseconds = milliseconds });
        }
    }

    private void SlowRefreshChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isSynchronizingControls && TryGetSelectedInteger(SlowRefreshComboBox, out var milliseconds))
        {
            Save(new SettingsPatch { SlowRefreshMilliseconds = milliseconds });
        }
    }

    private void ThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isSynchronizingControls && ThemeComboBox.SelectedValue is string themeName
            && Enum.TryParse<BarTheme>(themeName, ignoreCase: false, out var theme))
        {
            Save(new SettingsPatch { Theme = theme });
        }
    }

    private void OpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSynchronizingControls || OpacityValueText is null)
        {
            return;
        }

        var steppedOpacity = PerformanceSettings.SnapOpacity(OpacitySlider.Value);
        if (Math.Abs(OpacitySlider.Value - steppedOpacity) > 0.0001)
        {
            _isSynchronizingControls = true;
            OpacitySlider.Value = steppedOpacity;
            _isSynchronizingControls = false;
        }

        OpacityValueText.Text = $"{steppedOpacity.ToString("0%", CultureInfo.InvariantCulture)}";
        Save(new SettingsPatch { Opacity = steppedOpacity });
    }

    private void FontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isSynchronizingControls && FontSizeValueText is not null)
        {
            var fontSize = (int)Math.Round(FontSizeSlider.Value, MidpointRounding.AwayFromZero);
            FontSizeValueText.Text = fontSize.ToString(CultureInfo.InvariantCulture);
            Save(new SettingsPatch { FontSize = fontSize });
        }
    }

    private void Save(SettingsPatch patch)
    {
        try
        {
            _currentSettings = _updateSettings(patch);
            ResynchronizeControls();
            ShowStatus("已保存", isError: false, persistent: false);
        }
        catch (Exception exception)
        {
            ResynchronizeControls();
            ShowStatus("保存失败", isError: true, persistent: false);
            System.Diagnostics.Trace.WriteLine($"保存设置失败：{exception}");
        }
    }

    /// <summary>按最新设置重挂控件与主题；期间抑制控件事件回写。</summary>
    private void ResynchronizeControls()
    {
        _isSynchronizingControls = true;
        SynchronizeControls(_currentSettings);
        ApplyTheme(_currentSettings.Theme);
        _isSynchronizingControls = false;
    }

    private void SynchronizeControls(PerformanceSettings settings)
    {
        CpuToggle.IsChecked = settings.Metrics.Cpu;
        MemoryToggle.IsChecked = settings.Metrics.Memory;
        GpuToggle.IsChecked = settings.Metrics.Gpu;
        NetworkToggle.IsChecked = settings.Metrics.Network;
        TimeToggle.IsChecked = settings.Metrics.Time;
        AutostartToggle.IsChecked = settings.Autostart;
        FullscreenAutoHideToggle.IsChecked = settings.AutoHideOnFullscreen;
        FastRefreshComboBox.SelectedValue = settings.FastRefreshMilliseconds.ToString(CultureInfo.InvariantCulture);
        SlowRefreshComboBox.SelectedValue = settings.SlowRefreshMilliseconds.ToString(CultureInfo.InvariantCulture);
        ThemeComboBox.SelectedValue = settings.Theme.ToString();
        OpacitySlider.Value = settings.Opacity;
        OpacityValueText.Text = settings.Opacity.ToString("0%", CultureInfo.InvariantCulture);
        FontSizeSlider.Value = settings.FontSize;
        FontSizeValueText.Text = settings.FontSize.ToString(CultureInfo.InvariantCulture);
    }

    private void ApplyTheme(BarTheme theme)
    {
        var dark = PerformanceBarViewModel.IsDarkEffectiveTheme(theme);
        Resources["PageBackgroundBrush"] = CreateBrush(
            dark ? Color.FromRgb(0x11, 0x11, 0x13) : Color.FromRgb(0xF5, 0xF5, 0xF7));
        Resources["SurfaceBrush"] = CreateBrush(
            dark ? Color.FromRgb(0x1E, 0x1E, 0x21) : Color.FromRgb(0xFF, 0xFF, 0xFF));
        Resources["PrimaryTextBrush"] = CreateBrush(
            dark ? Color.FromRgb(0xF5, 0xF5, 0xF7) : Color.FromRgb(0x1D, 0x1D, 0x1F));
        Resources["SecondaryTextBrush"] = CreateBrush(
            dark ? Color.FromRgb(0xA1, 0xA1, 0xA6) : Color.FromRgb(0x70, 0x70, 0x70));
        Resources["AccentBrush"] = CreateBrush(
            dark ? Color.FromRgb(0x29, 0x97, 0xFF) : Color.FromRgb(0x00, 0x71, 0xE3));
        Resources["ControlBackgroundBrush"] = CreateBrush(
            dark ? Color.FromRgb(0x2C, 0x2C, 0x2E) : Color.FromRgb(0xE8, 0xE8, 0xED));
        Resources["SwitchTrackBrush"] = CreateBrush(dark
            ? Color.FromArgb(0x52, 0x78, 0x78, 0x80)
            : Color.FromArgb(0x3D, 0x78, 0x78, 0x80));
        Resources["DividerBrush"] = CreateBrush(dark
            ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x0F, 0x00, 0x00, 0x00));
        Resources["HoverBrush"] = CreateBrush(
            dark ? Color.FromRgb(0x3A, 0x3A, 0x3C) : Color.FromRgb(0xE0, 0xE0, 0xE6));
        Resources["ThumbBrush"] = CreateBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
    }

    /// <summary>
    /// 按窗口当前所在显示器的工作区重新约束最大高度（DIP）；
    /// 跨屏移动、工作区变化时重新计算。内容自适应高度，超出即由内容区滚动。
    /// </summary>
    private void UpdateMaxHeightForCurrentScreen()
    {
        var workAreaHeight = TryGetMonitorWorkAreaHeight(out var monitorWorkAreaHeight)
            ? monitorWorkAreaHeight
            : SystemParameters.WorkArea.Height;
        MaxHeight = SettingsWindowSizingRules.ResolveMaxWindowHeight(workAreaHeight);
    }

    /// <summary>读取窗口所在显示器工作区高度并换算为 DIP；失败时返回 false 走主屏回退。</summary>
    private bool TryGetMonitorWorkAreaHeight(out double workAreaHeight)
    {
        workAreaHeight = 0;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return false;
        }

        try
        {
            var monitor = SharedNativeMethods.MonitorFromWindow(
                handle, SharedNativeMethods.MonitorDefaultToNearest);
            if (monitor == nint.Zero)
            {
                return false;
            }

            var info = new SharedNativeMethods.MonitorInfoW
            {
                CbSize = (uint)Marshal.SizeOf<SharedNativeMethods.MonitorInfoW>()
            };
            if (!SharedNativeMethods.GetMonitorInfoW(monitor, ref info))
            {
                return false;
            }

            var dpiScale = 1.0;
            try
            {
                if (SharedNativeMethods.GetDpiForMonitor(
                        monitor, SharedNativeMethods.DpiEffective, out var dpiX, out _) == 0
                    && dpiX > 0)
                {
                    dpiScale = dpiX / 96.0;
                }
            }
            catch (Exception exception) when (
                exception is DllNotFoundException or EntryPointNotFoundException)
            {
            }

            if (dpiScale <= 0)
            {
                return false;
            }

            workAreaHeight = (info.RcWork.Bottom - info.RcWork.Top) / dpiScale;
            return workAreaHeight > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void ShowStatus(string message, bool isError, bool persistent)
    {
        _savedMessageTimer.Stop();
        SaveStatusText.Text = message;
        SaveStatusText.Foreground = CreateBrush(isError ? Color.FromRgb(0xB4, 0x2B, 0x2B) : Color.FromRgb(0x2A, 0x7D, 0x46));
        if (!persistent)
        {
            _savedMessageTimer.Start();
        }
    }

    private void OnSavedMessageTimerTick(object? sender, EventArgs e)
    {
        _savedMessageTimer.Stop();
        SaveStatusText.Text = string.Empty;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            StartListeningForThemeChanges();
        }
        else
        {
            StopListeningForThemeChanges();
        }
    }

    private void StartListeningForThemeChanges()
    {
        if (_isListeningForThemeChanges)
        {
            return;
        }

        _isListeningForThemeChanges = true;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void StopListeningForThemeChanges()
    {
        if (!_isListeningForThemeChanges)
        {
            return;
        }

        _isListeningForThemeChanges = false;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea))
        {
            UpdateMaxHeightForCurrentScreen();
        }

        if (_currentSettings.Theme == BarTheme.System)
        {
            ApplyTheme(BarTheme.System);
        }
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_currentSettings.Theme == BarTheme.System
            && !Dispatcher.HasShutdownStarted
            && !Dispatcher.HasShutdownFinished)
        {
            _ = Dispatcher.BeginInvoke(new Action(() => ApplyTheme(BarTheme.System)));
        }
    }

    private static bool TryGetSelectedInteger(ComboBox comboBox, out int value)
    {
        if (comboBox.SelectedValue is not null
            && int.TryParse(Convert.ToString(comboBox.SelectedValue, CultureInfo.InvariantCulture), out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>头部标题行即拖动把手；关闭按钮自身吃掉鼠标按下事件，不会误触发拖动。</summary>
    private void HeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void CloseWindowButton_OnClick(object sender, RoutedEventArgs e) => Close();
}
