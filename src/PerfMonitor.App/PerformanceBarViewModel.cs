using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using PerfMonitor.Core.Metrics;
using PerfMonitor.Core.Settings;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
    // 视觉令牌取自 docs/UI/preview.html（深色为原值，亮色为推导值）；字号与间距体系维持原规格。
    private Brush _backgroundBrush = CreateBrush(Color.FromArgb(0xF5, 0x12, 0x15, 0x19));
    private Brush _foregroundBrush = CreateBrush(Color.FromRgb(0xF3, 0xF5, 0xF7));
    private Brush _labelBrush = CreateBrush(Color.FromRgb(0x92, 0x97, 0x9F));
    private Brush _iconBrush = CreateBrush(Color.FromRgb(0x9D, 0xA3, 0xAA));
    private Brush _temperatureBrush = CreateBrush(Color.FromRgb(0x9D, 0xA2, 0xA9));
    private Brush _missingBrush = CreateBrush(Color.FromArgb(0x52, 0xF3, 0xF5, 0xF7));
    private Brush _borderBrush = CreateBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    private Brush _dividerBrush = CreateBrush(Color.FromArgb(0x21, 0xFF, 0xFF, 0xFF));
    private Brush _hoverBrush = CreateBrush(Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF));
    private DropShadowEffect? _shadowEffect = CreateShadowEffect(0.32);
    private Brush _cpuTokenBrush = CreateBrush(Color.FromRgb(0x74, 0xE2, 0x6E));
    private Brush _memoryTokenBrush = CreateBrush(Color.FromRgb(0x4B, 0xAD, 0xFF));
    private Brush _gpuTokenBrush = CreateBrush(Color.FromRgb(0x52, 0xD6, 0x85));
    private Brush _downloadTokenBrush = CreateBrush(Color.FromRgb(0x27, 0xAA, 0xFF));
    private Brush _uploadTokenBrush = CreateBrush(Color.FromRgb(0x49, 0xDC, 0x8B));
    private Brush _cpuPercentageBrush = CreateBrush(Color.FromRgb(0x74, 0xE2, 0x6E));
    private Brush _memoryPercentageBrush = CreateBrush(Color.FromRgb(0x4B, 0xAD, 0xFF));
    private Brush _gpuPercentageBrush = CreateBrush(Color.FromRgb(0x52, 0xD6, 0x85));
    private Brush _gpuMemoryPercentageBrush = CreateBrush(Color.FromRgb(0xF3, 0xF5, 0xF7));
    private Brush _gpuTemperatureBrush = CreateBrush(Color.FromRgb(0x9D, 0xA2, 0xA9));
    private Brush _cpuTemperatureBrush = CreateBrush(Color.FromRgb(0x9D, 0xA2, 0xA9));
    private Brush _networkDownloadBrush = CreateBrush(Color.FromRgb(0xF3, 0xF5, 0xF7));
    private Brush _networkUploadBrush = CreateBrush(Color.FromRgb(0xF3, 0xF5, 0xF7));
    private Brush _networkDownloadArrowBrush = CreateBrush(Color.FromRgb(0x27, 0xAA, 0xFF));
    private Brush _networkUploadArrowBrush = CreateBrush(Color.FromRgb(0x49, 0xDC, 0x8B));
    private Brush _networkDownloadUnitBrush = CreateBrush(Color.FromRgb(0x92, 0x97, 0x9F));
    private Brush _networkUploadUnitBrush = CreateBrush(Color.FromRgb(0x92, 0x97, 0x9F));

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

    public Visibility CpuIconVisibility => ToVisibility(_settings.Metrics.Cpu && _settings.MetricIcons.Cpu);

    public Visibility MemoryIconVisibility => ToVisibility(_settings.Metrics.Memory && _settings.MetricIcons.Memory);

    public Visibility GpuIconVisibility => ToVisibility(_settings.Metrics.Gpu && _settings.MetricIcons.Gpu);

    public Visibility NetworkIconVisibility => ToVisibility(_settings.Metrics.Network && _settings.MetricIcons.Network);

    public Visibility TimeIconVisibility => ToVisibility(_settings.Metrics.Time && _settings.MetricIcons.Time);

    public Visibility CpuDividerVisibility { get => _cpuDividerVisibility; private set => SetField(ref _cpuDividerVisibility, value); }

    public Visibility MemoryDividerVisibility { get => _memoryDividerVisibility; private set => SetField(ref _memoryDividerVisibility, value); }

    public Visibility GpuDividerVisibility { get => _gpuDividerVisibility; private set => SetField(ref _gpuDividerVisibility, value); }

    public Visibility NetworkDividerVisibility { get => _networkDividerVisibility; private set => SetField(ref _networkDividerVisibility, value); }

    public double FontSize => _settings.FontSize;

    public double LabelFontSize => Math.Clamp(_settings.FontSize * 0.82, 9, 12);

    public double UnitFontSize => Math.Clamp(_settings.FontSize * 0.82, 8, 12);

    /// <summary>段图标边长；html 基准为 19px / 17px 主字号。</summary>
    public double IconSize => Math.Round(_settings.FontSize * 1.12, 2);

    /// <summary>图标与后续文字的间距（加在图标右侧）；比段内读数间距略宽，把图标分成独立的视觉单元。</summary>
    public Thickness IconGap => new(0, 0, _settings.FontSize * 0.75, 0);

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

    public Brush LabelBrush { get => _labelBrush; private set => SetField(ref _labelBrush, value); }

    public Brush IconBrush { get => _iconBrush; private set => SetField(ref _iconBrush, value); }

    public Brush TemperatureBrush { get => _temperatureBrush; private set => SetField(ref _temperatureBrush, value); }

    public Brush MissingBrush { get => _missingBrush; private set => SetField(ref _missingBrush, value); }

    public Brush BorderBrush { get => _borderBrush; private set => SetField(ref _borderBrush, value); }

    public Brush DividerBrush { get => _dividerBrush; private set => SetField(ref _dividerBrush, value); }

    public Brush HoverBrush { get => _hoverBrush; private set => SetField(ref _hoverBrush, value); }

    /// <summary>四周环绕的卡片投影（高斯、偏下）；透明显示下返回 null 关闭。</summary>
    public DropShadowEffect? ShadowEffect { get => _shadowEffect; private set => SetField(ref _shadowEffect, value); }

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

    internal void ApplySettings(PerformanceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        _settings = settings;
        UpdateSegmentVisibility();
        OnPropertyChanged(nameof(CpuIconVisibility));
        OnPropertyChanged(nameof(MemoryIconVisibility));
        OnPropertyChanged(nameof(GpuIconVisibility));
        OnPropertyChanged(nameof(NetworkIconVisibility));
        OnPropertyChanged(nameof(TimeIconVisibility));
        UpdateThemeBrushes();
        OnPropertyChanged(nameof(FontSize));
        OnPropertyChanged(nameof(LabelFontSize));
        OnPropertyChanged(nameof(UnitFontSize));
        OnPropertyChanged(nameof(IconSize));
        OnPropertyChanged(nameof(IconGap));
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
        var dark = IsDarkEffectiveTheme(_settings.Theme);
        var hide = _settings.TransparentDisplay;
        var background = dark
            ? Color.FromRgb(0x12, 0x15, 0x19)
            : Color.FromRgb(0xF6, 0xF7, 0xF9);
        // 透明显示把背景降到 alpha=1 而不是 0：分层窗口中 alpha=0 像素被系统穿透，
        // 命中区必须留在卡片本身；装饰（描边/分隔/hover/高光/投影）则全部归零隐藏。
        var backgroundAlpha = hide ? (byte)1 : (byte)Math.Round(_settings.Opacity * byte.MaxValue);
        BackgroundBrush = CreateBrush(Color.FromArgb(backgroundAlpha, background.R, background.G, background.B));

        var foreground = dark ? Color.FromRgb(0xF3, 0xF5, 0xF7) : Color.FromRgb(0x1A, 0x1D, 0x22);
        ForegroundBrush = CreateBrush(foreground);
        MissingBrush = CreateBrush(Color.FromArgb(0x52, foreground.R, foreground.G, foreground.B));
        LabelBrush = CreateBrush(dark ? Color.FromRgb(0x92, 0x97, 0x9F) : Color.FromRgb(0x5F, 0x65, 0x6D));
        IconBrush = CreateBrush(dark ? Color.FromRgb(0x9D, 0xA3, 0xAA) : Color.FromRgb(0x61, 0x66, 0x6D));
        TemperatureBrush = CreateBrush(dark ? Color.FromRgb(0x9D, 0xA2, 0xA9) : Color.FromRgb(0x6A, 0x6F, 0x76));
        _cpuTokenBrush = CreateBrush(dark ? Color.FromRgb(0x74, 0xE2, 0x6E) : Color.FromRgb(0x2F, 0x9E, 0x44));
        _memoryTokenBrush = CreateBrush(dark ? Color.FromRgb(0x4B, 0xAD, 0xFF) : Color.FromRgb(0x19, 0x71, 0xC2));
        _gpuTokenBrush = CreateBrush(dark ? Color.FromRgb(0x52, 0xD6, 0x85) : Color.FromRgb(0x2B, 0x8A, 0x3E));
        _downloadTokenBrush = CreateBrush(dark ? Color.FromRgb(0x27, 0xAA, 0xFF) : Color.FromRgb(0x1C, 0x7E, 0xD6));
        _uploadTokenBrush = CreateBrush(dark ? Color.FromRgb(0x49, 0xDC, 0x8B) : Color.FromRgb(0x0C, 0xA6, 0x78));

        BorderBrush = CreateBrush(WithHiddenAlpha(
            dark ? Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1A, 0x00, 0x00, 0x00), hide));
        DividerBrush = CreateBrush(WithHiddenAlpha(
            dark ? Color.FromArgb(0x21, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x21, 0x00, 0x00, 0x00), hide));
        HoverBrush = CreateBrush(WithHiddenAlpha(
            dark ? Color.FromArgb(0x06, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x08, 0x00, 0x00, 0x00), hide));
        ShadowEffect = CreateShadowEffect(dark ? 0.32 : 0.16, hide);
        UpdateReadingBrushes();
    }

    private void UpdateReadingBrushes()
    {
        CpuPercentageBrush = _hasCpuPercentage ? _cpuTokenBrush : MissingBrush;
        MemoryPercentageBrush = _hasMemoryPercentage ? _memoryTokenBrush : MissingBrush;
        GpuPercentageBrush = _hasGpuPercentage ? _gpuTokenBrush : MissingBrush;
        GpuMemoryPercentageBrush = _hasGpuMemoryPercentage ? ForegroundBrush : MissingBrush;
        GpuTemperatureBrush = _hasGpuTemperature ? TemperatureBrush : MissingBrush;
        CpuTemperatureBrush = _hasCpuTemperature ? TemperatureBrush : MissingBrush;
        NetworkDownloadBrush = _hasNetworkDownload ? ForegroundBrush : MissingBrush;
        NetworkUploadBrush = _hasNetworkUpload ? ForegroundBrush : MissingBrush;
        NetworkDownloadArrowBrush = _hasNetworkDownload ? _downloadTokenBrush : MissingBrush;
        NetworkUploadArrowBrush = _hasNetworkUpload ? _uploadTokenBrush : MissingBrush;
        NetworkDownloadUnitBrush = _hasNetworkDownload ? LabelBrush : MissingBrush;
        NetworkUploadUnitBrush = _hasNetworkUpload ? LabelBrush : MissingBrush;
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

    /// <summary>装饰色在透明显示下降为 alpha=0；读数、标签等内容色不受影响。</summary>
    private static Color WithHiddenAlpha(Color color, bool hide) =>
        hide ? Color.FromArgb(0, color.R, color.G, color.B) : color;

    /// <summary>四周环绕投影（html 的 box-shadow 语义）：高斯模糊四向扩散、偏下；
    /// 可见范围约 3σ + 偏移 ≈ 15.5 DIP，必须完整落在 16 DIP 窗口留白内，超出会在窗口边缘被硬切。</summary>
    private static DropShadowEffect? CreateShadowEffect(double opacity, bool hide = false)
    {
        if (hide)
        {
            return null;
        }

        var effect = new DropShadowEffect
        {
            BlurRadius = 9,
            ShadowDepth = 2,
            Direction = 270,
            Color = Colors.Black,
            Opacity = opacity
        };
        effect.Freeze();
        return effect;
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

    /// <summary>按主题设置解析是否深色：System 主题跟随系统，单一来源供各处复用。</summary>
    internal static bool IsDarkEffectiveTheme(BarTheme theme) => theme switch
    {
        BarTheme.Dark => true,
        BarTheme.Light => false,
        _ => IsDarkSystemTheme()
    };

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
