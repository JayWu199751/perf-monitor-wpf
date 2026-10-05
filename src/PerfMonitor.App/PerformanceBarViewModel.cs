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
    private const string DigitWidthSpace = "\u2007";
    private long _metricGeneration;
    private string _cpuPercentageText = "--";
    private string _memoryPercentageText = "--";
    private string _gpuPercentageText = "--";
    private string _gpuMemoryPercentageText = "--";
    private string _gpuTemperatureValueText = "--";
    private string _cpuTemperatureValueText = "--";
    private string _gpuTemperatureUnitText = string.Empty;
    private string _cpuTemperatureUnitText = string.Empty;
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
    private bool _hasCpuPercentage;
    private bool _hasMemoryPercentage;
    private bool _hasGpuPercentage;
    private bool _hasGpuMemoryPercentage;
    private bool _hasGpuTemperature;
    private bool _hasCpuTemperature;
    private bool _hasNetworkDownload;
    private bool _hasNetworkUpload;
    private Brush _backgroundBrush = CreateBrush(Color.FromArgb(0xB8, 0x1D, 0x1D, 0x1F));
    private Brush _foregroundBrush = CreateBrush(Color.FromRgb(0xF5, 0xF5, 0xF7));
    private Brush _secondaryBrush = CreateBrush(Color.FromArgb(0xAD, 0xF5, 0xF5, 0xF7));
    private Brush _labelBrush = CreateBrush(Color.FromArgb(0x80, 0xF5, 0xF5, 0xF7));
    private Brush _missingBrush = CreateBrush(Color.FromArgb(0x52, 0xF5, 0xF5, 0xF7));
    private Brush _accentBrush = CreateBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
    private Brush _borderBrush = CreateBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
    private Brush _dividerBrush = CreateBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
    private Brush _cpuPercentageBrush = CreateBrush(Color.FromArgb(0x52, 0xF5, 0xF5, 0xF7));
    private Brush _memoryPercentageBrush = CreateBrush(Color.FromArgb(0x52, 0xF5, 0xF5, 0xF7));
    private Brush _gpuPercentageBrush = CreateBrush(Color.FromArgb(0x52, 0xF5, 0xF5, 0xF7));
    private Brush _gpuMemoryPercentageBrush = CreateBrush(Color.FromArgb(0x52, 0xF5, 0xF5, 0xF7));
    private Brush _gpuTemperatureBrush = CreateBrush(Color.FromArgb(0x52, 0xF5, 0xF5, 0xF7));
    private Brush _cpuTemperatureBrush = CreateBrush(Color.FromArgb(0x52, 0xF5, 0xF5, 0xF7));
    private Brush _networkDownloadBrush = CreateBrush(Color.FromArgb(0x52, 0xF5, 0xF5, 0xF7));
    private Brush _networkUploadBrush = CreateBrush(Color.FromArgb(0x52, 0xF5, 0xF5, 0xF7));
    private Brush _networkDownloadArrowBrush = CreateBrush(Color.FromArgb(0x52, 0xF5, 0xF5, 0xF7));
    private Brush _networkUploadArrowBrush = CreateBrush(Color.FromArgb(0x52, 0xF5, 0xF5, 0xF7));
    private Brush _networkDownloadUnitBrush = CreateBrush(Color.FromArgb(0x52, 0xF5, 0xF5, 0xF7));
    private Brush _networkUploadUnitBrush = CreateBrush(Color.FromArgb(0x52, 0xF5, 0xF5, 0xF7));

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

    public double LabelFontSize => Math.Clamp(_settings.FontSize * 0.82, 9, 12);

    public double UnitFontSize => Math.Clamp(_settings.FontSize * 0.82, 8, 12);

    public CornerRadius CornerRadius => new(_settings.FontSize * 0.8);

    public Thickness LabelGap => new(_settings.FontSize * 0.58, 0, 0, 0);

    public Thickness ReadingGap => new(_settings.FontSize * 0.5, 0, 0, 0);

    public Thickness DividerGap => new(_settings.FontSize * 0.416, 0, _settings.FontSize * 0.416, 0);

    public double DividerHeight => _settings.FontSize;

    public Thickness CardPadding => _settings.TransparentDisplay
        ? new Thickness(8, 2, 8, 2)
        : new Thickness(7, 2, 7, 2);

    public Thickness BorderThickness => _settings.TransparentDisplay ? new Thickness(0) : new Thickness(1);

    public Brush BackgroundBrush { get => _backgroundBrush; private set => SetField(ref _backgroundBrush, value); }

    public Brush ForegroundBrush { get => _foregroundBrush; private set => SetField(ref _foregroundBrush, value); }

    public Brush SecondaryBrush { get => _secondaryBrush; private set => SetField(ref _secondaryBrush, value); }

    public Brush LabelBrush { get => _labelBrush; private set => SetField(ref _labelBrush, value); }

    public Brush MissingBrush { get => _missingBrush; private set => SetField(ref _missingBrush, value); }

    public Brush CpuPercentageBrush { get => _cpuPercentageBrush; private set => SetField(ref _cpuPercentageBrush, value); }

    public Brush MemoryPercentageBrush { get => _memoryPercentageBrush; private set => SetField(ref _memoryPercentageBrush, value); }

    public Brush GpuPercentageBrush { get => _gpuPercentageBrush; private set => SetField(ref _gpuPercentageBrush, value); }

    public Brush GpuMemoryPercentageBrush { get => _gpuMemoryPercentageBrush; private set => SetField(ref _gpuMemoryPercentageBrush, value); }

    public Brush GpuTemperatureBrush { get => _gpuTemperatureBrush; private set => SetField(ref _gpuTemperatureBrush, value); }

    public Brush CpuTemperatureBrush { get => _cpuTemperatureBrush; private set => SetField(ref _cpuTemperatureBrush, value); }

    public Brush NetworkDownloadBrush { get => _networkDownloadBrush; private set => SetField(ref _networkDownloadBrush, value); }

    public Brush NetworkUploadBrush { get => _networkUploadBrush; private set => SetField(ref _networkUploadBrush, value); }

    public Brush NetworkDownloadArrowBrush { get => _networkDownloadArrowBrush; private set => SetField(ref _networkDownloadArrowBrush, value); }

    public Brush NetworkUploadArrowBrush { get => _networkUploadArrowBrush; private set => SetField(ref _networkUploadArrowBrush, value); }

    public Brush NetworkDownloadUnitBrush { get => _networkDownloadUnitBrush; private set => SetField(ref _networkDownloadUnitBrush, value); }

    public Brush NetworkUploadUnitBrush { get => _networkUploadUnitBrush; private set => SetField(ref _networkUploadUnitBrush, value); }

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

    public string GpuTemperatureValueText
    {
        get => _gpuTemperatureValueText;
        private set => SetField(ref _gpuTemperatureValueText, value);
    }

    public string CpuTemperatureValueText
    {
        get => _cpuTemperatureValueText;
        private set => SetField(ref _cpuTemperatureValueText, value);
    }

    public string GpuTemperatureUnitText
    {
        get => _gpuTemperatureUnitText;
        private set => SetField(ref _gpuTemperatureUnitText, value);
    }

    public string CpuTemperatureUnitText
    {
        get => _cpuTemperatureUnitText;
        private set => SetField(ref _cpuTemperatureUnitText, value);
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

        _hasCpuPercentage = snapshot.CpuPercentage.HasValue;
        _hasMemoryPercentage = snapshot.MemoryPercentage.HasValue;
        _hasGpuPercentage = snapshot.GpuPercentage.HasValue;
        _hasGpuMemoryPercentage = snapshot.GpuMemoryPercentage.HasValue;
        _hasGpuTemperature = snapshot.GpuTemperatureCelsius.HasValue;
        _hasCpuTemperature = snapshot.CpuTemperatureCelsius.HasValue;
        _hasNetworkDownload = snapshot.NetworkDownloadMegabytesPerSecond.HasValue;
        _hasNetworkUpload = snapshot.NetworkUploadMegabytesPerSecond.HasValue;
        CpuPercentageText = FormatPercentage(snapshot.CpuPercentage);
        MemoryPercentageText = FormatPercentage(snapshot.MemoryPercentage);
        GpuPercentageText = FormatPercentage(snapshot.GpuPercentage);
        GpuMemoryPercentageText = FormatPercentage(snapshot.GpuMemoryPercentage);
        GpuTemperatureValueText = FormatTemperatureValue(snapshot.GpuTemperatureCelsius);
        CpuTemperatureValueText = FormatTemperatureValue(snapshot.CpuTemperatureCelsius);
        GpuTemperatureUnitText = snapshot.GpuTemperatureCelsius.HasValue ? "°" : string.Empty;
        CpuTemperatureUnitText = snapshot.CpuTemperatureCelsius.HasValue ? "°" : string.Empty;
        NetworkDownloadSpeedText = FormatNetworkSpeed(snapshot.NetworkDownloadMegabytesPerSecond);
        NetworkUploadSpeedText = FormatNetworkSpeed(snapshot.NetworkUploadMegabytesPerSecond);
        UpdateReadingBrushes();
    }

    internal bool ApplySettings(PerformanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var layoutChanged = _settings.FontSize != settings.FontSize
            || _settings.Metrics != settings.Metrics
            || _settings.TransparentDisplay != settings.TransparentDisplay;
        _settings = settings;
        UpdateSegmentVisibility();
        UpdateThemeBrushes();
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(LabelFontSize));
        OnPropertyChanged(nameof(UnitFontSize));
        OnPropertyChanged(nameof(CornerRadius));
        OnPropertyChanged(nameof(LabelGap));
        OnPropertyChanged(nameof(ReadingGap));
        OnPropertyChanged(nameof(DividerGap));
        OnPropertyChanged(nameof(DividerHeight));
        OnPropertyChanged(nameof(CardPadding));
        OnPropertyChanged(nameof(BorderThickness));
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
            ? Color.FromRgb(0x1D, 0x1D, 0x1F)
            : Color.FromRgb(0xFA, 0xFA, 0xFC);
        var alpha = _settings.TransparentDisplay ? (byte)0 : (byte)Math.Round(_settings.Opacity * byte.MaxValue);
        BackgroundBrush = CreateBrush(Color.FromArgb(alpha, background.R, background.G, background.B));
        ForegroundBrush = CreateBrush(dark ? Color.FromRgb(0xF5, 0xF5, 0xF7) : Color.FromRgb(0x1D, 0x1D, 0x1F));
        SecondaryBrush = CreateBrush(dark
            ? Color.FromArgb(0xAD, 0xF5, 0xF5, 0xF7)
            : Color.FromArgb(0xA8, 0x1D, 0x1D, 0x1F));
        LabelBrush = CreateBrush(dark
            ? Color.FromArgb(0x80, 0xF5, 0xF5, 0xF7)
            : Color.FromArgb(0x9E, 0x1D, 0x1D, 0x1F));
        MissingBrush = CreateBrush(dark
            ? Color.FromArgb(0x52, 0xF5, 0xF5, 0xF7)
            : Color.FromArgb(0x4D, 0x1D, 0x1D, 0x1F));
        _accentBrush = CreateBrush(dark ? Color.FromRgb(0x0A, 0x84, 0xFF) : Color.FromRgb(0x00, 0x71, 0xE3));
        BorderBrush = CreateBrush(Color.FromArgb(
            _settings.TransparentDisplay ? (byte)0 : dark ? (byte)0x24 : (byte)0x1F,
            dark ? (byte)0xFF : (byte)0x00,
            dark ? (byte)0xFF : (byte)0x00,
            dark ? (byte)0xFF : (byte)0x00));
        var divider = dark
            ? Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x1A, 0x00, 0x00, 0x00);
        DividerBrush = CreateBrush(_settings.TransparentDisplay ? Color.FromArgb(0, divider.R, divider.G, divider.B) : divider);
        UpdateReadingBrushes();
    }

    private void UpdateReadingBrushes()
    {
        CpuPercentageBrush = _hasCpuPercentage ? ForegroundBrush : MissingBrush;
        MemoryPercentageBrush = _hasMemoryPercentage ? ForegroundBrush : MissingBrush;
        GpuPercentageBrush = _hasGpuPercentage ? ForegroundBrush : MissingBrush;
        GpuMemoryPercentageBrush = _hasGpuMemoryPercentage ? ForegroundBrush : MissingBrush;
        GpuTemperatureBrush = _hasGpuTemperature ? SecondaryBrush : MissingBrush;
        CpuTemperatureBrush = _hasCpuTemperature ? SecondaryBrush : MissingBrush;
        NetworkDownloadBrush = _hasNetworkDownload ? ForegroundBrush : MissingBrush;
        NetworkUploadBrush = _hasNetworkUpload ? ForegroundBrush : MissingBrush;
        NetworkDownloadArrowBrush = _hasNetworkDownload ? _accentBrush : MissingBrush;
        NetworkUploadArrowBrush = _hasNetworkUpload ? _accentBrush : MissingBrush;
        NetworkDownloadUnitBrush = _hasNetworkDownload ? SecondaryBrush : MissingBrush;
        NetworkUploadUnitBrush = _hasNetworkUpload ? SecondaryBrush : MissingBrush;
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

    private static string FormatPercentage(int? value)
    {
        if (value is not { } percentage)
        {
            return "--";
        }

        var formatted = percentage.ToString(CultureInfo.InvariantCulture) + "%";
        return percentage is >= 0 and < 10 ? DigitWidthSpace + formatted : formatted;
    }

    private static string FormatTemperatureValue(int? value) =>
        value is { } temperature
            ? temperature is >= 0 and < 10
                ? DigitWidthSpace + temperature.ToString(CultureInfo.InvariantCulture)
                : temperature.ToString(CultureInfo.InvariantCulture)
            : "--";

    private static string FormatNetworkSpeed(double? value)
    {
        if (value is not { } speed)
        {
            return "--";
        }

        var formatted = speed.ToString("0.0", CultureInfo.InvariantCulture);
        var decimalSeparatorIndex = formatted.IndexOf('.');
        return decimalSeparatorIndex == 1 && formatted[0] is >= '0' and <= '9'
            ? DigitWidthSpace + formatted
            : formatted;
    }

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
