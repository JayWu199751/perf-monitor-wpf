using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.Settings;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace PerfMonitor.App;

public sealed class PerformanceBarViewModel : INotifyPropertyChanged
{
    private long _metricGeneration;
    private string _cpuPercentageText = "--%";
    private string _memoryPercentageText = "--%";
    private string _gpuPercentageText = "--%";
    private string _gpuMemoryPercentageText = "--%";
    private string _gpuTemperatureText = "--°";
    private string _cpuTemperatureText = "--°";
    private string _networkDownloadSpeedText = "--";
    private string _networkUploadSpeedText = "--";
    private string _timeText = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    private readonly DispatcherTimer _clockTimer;
    private PerformanceSettings _settings = PerformanceSettings.Default;
    private Visibility _cpuVisibility = Visibility.Visible;
    private Visibility _memoryVisibility = Visibility.Visible;
    private Visibility _gpuVisibility = Visibility.Visible;
    private Visibility _networkVisibility = Visibility.Visible;
    private Visibility _timeVisibility = Visibility.Visible;
    private Visibility _cpuDividerVisibility = Visibility.Visible;
    private Visibility _memoryDividerVisibility = Visibility.Visible;
    private Visibility _gpuDividerVisibility = Visibility.Visible;
    private Visibility _networkDividerVisibility = Visibility.Visible;
    private Brush _backgroundBrush = CreateBrush(Color.FromArgb(0xE5, 0x1F, 0x22, 0x28));
    private Brush _foregroundBrush = CreateBrush(Color.FromRgb(0xF4, 0xF6, 0xF8));
    private Brush _secondaryBrush = CreateBrush(Color.FromRgb(0xB7, 0xC0, 0xCA));
    private Brush _borderBrush = CreateBrush(Color.FromArgb(0x40, 0x55, 0x60, 0x70));
    private Brush _dividerBrush = CreateBrush(Color.FromArgb(0x70, 0xA4, 0xAE, 0xB8));

    public PerformanceBarViewModel()
    {
        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += OnClockTick;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ApplySettings(_settings);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Visibility CpuVisibility { get => _cpuVisibility; private set => SetField(ref _cpuVisibility, value); }

    public Visibility MemoryVisibility { get => _memoryVisibility; private set => SetField(ref _memoryVisibility, value); }

    public Visibility GpuVisibility { get => _gpuVisibility; private set => SetField(ref _gpuVisibility, value); }

    public Visibility NetworkVisibility { get => _networkVisibility; private set => SetField(ref _networkVisibility, value); }

    public Visibility TimeVisibility { get => _timeVisibility; private set => SetField(ref _timeVisibility, value); }

    public Visibility CpuDividerVisibility { get => _cpuDividerVisibility; private set => SetField(ref _cpuDividerVisibility, value); }

    public Visibility MemoryDividerVisibility { get => _memoryDividerVisibility; private set => SetField(ref _memoryDividerVisibility, value); }

    public Visibility GpuDividerVisibility { get => _gpuDividerVisibility; private set => SetField(ref _gpuDividerVisibility, value); }

    public Visibility NetworkDividerVisibility { get => _networkDividerVisibility; private set => SetField(ref _networkDividerVisibility, value); }

    public double FontSize => _settings.FontSize;

    public double LabelFontSize => _settings.FontSize * 0.78;

    public CornerRadius CornerRadius => new(_settings.FontSize * 0.8);

    public Thickness LabelGap => new(_settings.FontSize * 0.58, 0, 0, 0);

    public Thickness ReadingGap => new(_settings.FontSize * 0.5, 0, 0, 0);

    public Thickness DividerGap => new(_settings.FontSize * 0.416, 0, _settings.FontSize * 0.416, 0);

    public double DividerHeight => _settings.FontSize;

    public Brush BackgroundBrush { get => _backgroundBrush; private set => SetField(ref _backgroundBrush, value); }

    public Brush ForegroundBrush { get => _foregroundBrush; private set => SetField(ref _foregroundBrush, value); }

    public Brush SecondaryBrush { get => _secondaryBrush; private set => SetField(ref _secondaryBrush, value); }

    public Brush BorderBrush { get => _borderBrush; private set => SetField(ref _borderBrush, value); }

    public Brush DividerBrush { get => _dividerBrush; private set => SetField(ref _dividerBrush, value); }

    public string TimeText { get => _timeText; private set => SetField(ref _timeText, value); }

    public string CpuPercentageText
    {
        get => _cpuPercentageText;
        private set => SetField(ref _cpuPercentageText, value);
    }

    public string MemoryPercentageText
    {
        get => _memoryPercentageText;
        private set => SetField(ref _memoryPercentageText, value);
    }

    public string GpuPercentageText
    {
        get => _gpuPercentageText;
        private set => SetField(ref _gpuPercentageText, value);
    }

    public string GpuMemoryPercentageText
    {
        get => _gpuMemoryPercentageText;
        private set => SetField(ref _gpuMemoryPercentageText, value);
    }

    public string GpuTemperatureText
    {
        get => _gpuTemperatureText;
        private set => SetField(ref _gpuTemperatureText, value);
    }

    public string CpuTemperatureText
    {
        get => _cpuTemperatureText;
        private set => SetField(ref _cpuTemperatureText, value);
    }

    public string NetworkDownloadSpeedText
    {
        get => _networkDownloadSpeedText;
        private set => SetField(ref _networkDownloadSpeedText, value);
    }

    public string NetworkUploadSpeedText
    {
        get => _networkUploadSpeedText;
        private set => SetField(ref _networkUploadSpeedText, value);
    }

    internal void SetMetricGeneration(long generation) => _metricGeneration = generation;

    internal void Apply(PerformanceMetricsSnapshot snapshot)
    {
        if (snapshot.Generation != _metricGeneration)
        {
            return;
        }

        CpuPercentageText = FormatPercentage(snapshot.CpuPercentage);
        MemoryPercentageText = FormatPercentage(snapshot.MemoryPercentage);
        GpuPercentageText = FormatPercentage(snapshot.GpuPercentage);
        GpuMemoryPercentageText = FormatPercentage(snapshot.GpuMemoryPercentage);
        GpuTemperatureText = FormatTemperature(snapshot.GpuTemperatureCelsius);
        CpuTemperatureText = FormatTemperature(snapshot.CpuTemperatureCelsius);
        NetworkDownloadSpeedText = FormatNetworkSpeed(snapshot.NetworkDownloadMegabytesPerSecond);
        NetworkUploadSpeedText = FormatNetworkSpeed(snapshot.NetworkUploadMegabytesPerSecond);
    }

    internal bool ApplySettings(PerformanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var layoutChanged = _settings.FontSize != settings.FontSize || _settings.Metrics != settings.Metrics;
        _settings = settings;
        UpdateSegmentVisibility();
        UpdateThemeBrushes();
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(LabelFontSize));
        OnPropertyChanged(nameof(CornerRadius));
        OnPropertyChanged(nameof(LabelGap));
        OnPropertyChanged(nameof(ReadingGap));
        OnPropertyChanged(nameof(DividerGap));
        OnPropertyChanged(nameof(DividerHeight));
        _clockTimer.IsEnabled = settings.Metrics.Time;
        if (settings.Metrics.Time)
        {
            UpdateTime();
        }

        return layoutChanged;
    }

    internal void Dispose()
    {
        _clockTimer.Stop();
        _clockTimer.Tick -= OnClockTick;
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void UpdateSegmentVisibility()
    {
        var visible = new[]
        {
            _settings.Metrics.Cpu,
            _settings.Metrics.Memory,
            _settings.Metrics.Gpu,
            _settings.Metrics.Network,
            _settings.Metrics.Time
        };
        var lastVisibleIndex = Array.FindLastIndex(visible, isVisible => isVisible);
        CpuVisibility = ToVisibility(visible[0]);
        MemoryVisibility = ToVisibility(visible[1]);
        GpuVisibility = ToVisibility(visible[2]);
        NetworkVisibility = ToVisibility(visible[3]);
        TimeVisibility = ToVisibility(visible[4]);
        CpuDividerVisibility = ToVisibility(visible[0] && 0 < lastVisibleIndex);
        MemoryDividerVisibility = ToVisibility(visible[1] && 1 < lastVisibleIndex);
        GpuDividerVisibility = ToVisibility(visible[2] && 2 < lastVisibleIndex);
        NetworkDividerVisibility = ToVisibility(visible[3] && 3 < lastVisibleIndex);
    }

    private void UpdateThemeBrushes()
    {
        var dark = _settings.Theme switch
        {
            BarTheme.Dark => true,
            BarTheme.Light => false,
            _ => IsDarkSystemTheme()
        };
        var background = dark
            ? Color.FromRgb(0x1F, 0x22, 0x28)
            : Color.FromRgb(0xF4, 0xF6, 0xF8);
        var alpha = _settings.TransparentDisplay ? (byte)0 : (byte)Math.Round(_settings.Opacity * byte.MaxValue);
        BackgroundBrush = CreateBrush(Color.FromArgb(alpha, background.R, background.G, background.B));
        ForegroundBrush = CreateBrush(dark ? Color.FromRgb(0xF4, 0xF6, 0xF8) : Color.FromRgb(0x24, 0x2A, 0x31));
        SecondaryBrush = CreateBrush(dark ? Color.FromRgb(0xB7, 0xC0, 0xCA) : Color.FromRgb(0x68, 0x72, 0x7D));
        BorderBrush = CreateBrush(Color.FromArgb(_settings.TransparentDisplay ? (byte)0 : (byte)0x40, background.R, background.G, background.B));
        var divider = dark ? Color.FromArgb(0x70, 0xA4, 0xAE, 0xB8) : Color.FromArgb(0x70, 0x68, 0x72, 0x7D);
        DividerBrush = CreateBrush(_settings.TransparentDisplay ? Color.FromArgb(0, divider.R, divider.G, divider.B) : divider);
    }

    private void OnClockTick(object? sender, EventArgs e) => UpdateTime();

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_settings.Theme == BarTheme.System)
        {
            if (_clockTimer.Dispatcher.CheckAccess())
            {
                UpdateThemeBrushes();
            }
            else if (!_clockTimer.Dispatcher.HasShutdownStarted && !_clockTimer.Dispatcher.HasShutdownFinished)
            {
                _ = _clockTimer.Dispatcher.BeginInvoke(new Action(UpdateThemeBrushes));
            }
        }
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_settings.Theme != BarTheme.System)
        {
            return;
        }

        if (_clockTimer.Dispatcher.CheckAccess())
        {
            UpdateThemeBrushes();
        }
        else if (!_clockTimer.Dispatcher.HasShutdownStarted && !_clockTimer.Dispatcher.HasShutdownFinished)
        {
            _ = _clockTimer.Dispatcher.BeginInvoke(new Action(UpdateThemeBrushes));
        }
    }

    private void UpdateTime() => TimeText = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    private static Visibility ToVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    internal static bool IsDarkSystemTheme()
    {
        try
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (personalize?.GetValue("AppsUseLightTheme") is int lightTheme)
            {
                return lightTheme == 0;
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (System.Security.SecurityException)
        {
        }

        return System.Windows.SystemColors.WindowColor.R
            + System.Windows.SystemColors.WindowColor.G
            + System.Windows.SystemColors.WindowColor.B < 384;
    }

    private static string FormatPercentage(int? value) => value is { } percentage
        ? percentage.ToString(CultureInfo.InvariantCulture) + "%"
        : "--";

    private static string FormatTemperature(int? value) =>
        value is { } temperature
            ? temperature.ToString(CultureInfo.InvariantCulture) + "°"
            : "--°";

    private static string FormatNetworkSpeed(double? value) =>
        value is { } speed
            ? speed.ToString("0.0", CultureInfo.InvariantCulture)
            : "--";

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
