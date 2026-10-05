using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PerfMonitor.Core.Settings;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
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
        SynchronizeControls(_currentSettings);
        ApplyTheme(_currentSettings.Theme);
        _isSynchronizingControls = false;
        if (recoveredInvalidSettings)
        {
            ShowStatus("配置损坏，已备份原文件并恢复默认设置", isError: true, persistent: true);
        }
    }

    public void ApplySettings(PerformanceSettings settings, bool recoveredInvalidSettings)
    {
        _currentSettings = settings.Validate();
        _isSynchronizingControls = true;
        SynchronizeControls(_currentSettings);
        ApplyTheme(_currentSettings.Theme);
        _isSynchronizingControls = false;
        if (recoveredInvalidSettings && string.IsNullOrEmpty(SaveStatusText.Text))
        {
            ShowStatus("配置损坏，已备份原文件并恢复默认设置", isError: true, persistent: true);
        }
    }

    private void MetricChanged(object sender, RoutedEventArgs e)
    {
        if (_isSynchronizingControls || sender is not CheckBox checkBox || checkBox.Tag is not string metric)
        {
            return;
        }

        var enabled = checkBox.IsChecked == true;
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
            _isSynchronizingControls = true;
            SynchronizeControls(_currentSettings);
            ApplyTheme(_currentSettings.Theme);
            _isSynchronizingControls = false;
            ShowStatus("已保存", isError: false, persistent: false);
        }
        catch (Exception exception)
        {
            _isSynchronizingControls = true;
            SynchronizeControls(_currentSettings);
            ApplyTheme(_currentSettings.Theme);
            _isSynchronizingControls = false;
            ShowStatus("保存失败", isError: true, persistent: false);
            System.Diagnostics.Trace.WriteLine($"保存设置失败：{exception}");
        }
    }

    private void SynchronizeControls(PerformanceSettings settings)
    {
        CpuCheckBox.IsChecked = settings.Metrics.Cpu;
        MemoryCheckBox.IsChecked = settings.Metrics.Memory;
        GpuCheckBox.IsChecked = settings.Metrics.Gpu;
        NetworkCheckBox.IsChecked = settings.Metrics.Network;
        TimeCheckBox.IsChecked = settings.Metrics.Time;
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
        var dark = theme switch
        {
            BarTheme.Dark => true,
            BarTheme.Light => false,
            _ => PerformanceBarViewModel.IsDarkSystemTheme()
        };
        Background = CreateBrush(dark ? Color.FromRgb(0x20, 0x23, 0x29) : Color.FromRgb(0xF4, 0xF6, 0xF8));
        Foreground = CreateBrush(dark ? Color.FromRgb(0xF4, 0xF6, 0xF8) : Color.FromRgb(0x24, 0x2A, 0x31));
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

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
}
