namespace PerfMonitor.Core.Settings;

public enum BarTheme
{
    System,
    Dark,
    Light
}

[Flags]
public enum DockedEdges
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 4,
    Right = 8
}

public sealed record MetricVisibility
{
    public bool Cpu { get; init; } = true;

    public bool Memory { get; init; } = true;

    public bool Gpu { get; init; } = true;

    public bool Network { get; init; } = true;

    public bool Time { get; init; } = true;
}

/// <summary>指标图标的独立显示偏好，不改变整个段的显隐。</summary>
public sealed record MetricIconVisibility
{
    public bool Cpu { get; init; } = true;

    public bool Memory { get; init; } = true;

    public bool Gpu { get; init; } = true;

    public bool Network { get; init; } = true;

    public bool Time { get; init; } = true;
}

public sealed record WidgetPlacement
{
    public double X { get; init; } = 24;

    public double Y { get; init; } = 24;

    public DockedEdges Docked { get; init; }

    /// <summary>持久化时性能条是否驻留任务栏行内；恢复时按当前任务栏几何重新落位。</summary>
    public bool InTaskbarRow { get; init; }
}

public sealed record PerformanceSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public MetricVisibility Metrics { get; init; } = new();

    public MetricIconVisibility MetricIcons { get; init; } = new();

    public int FastRefreshMilliseconds { get; init; } = 1000;

    public int SlowRefreshMilliseconds { get; init; } = 3000;

    public bool Autostart { get; init; }

    public bool AutoHideOnFullscreen { get; init; } = true;

    public bool CenterInTaskbarRow { get; init; }

    public bool TransparentDisplay { get; init; }

    public double Opacity { get; init; } = 0.96;

    public int FontSize { get; init; } = 12;

    public BarTheme Theme { get; init; } = BarTheme.System;

    public WidgetPlacement Widget { get; init; } = new();

    public static PerformanceSettings Default { get; } = new();

    public PerformanceSettings Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException($"不支持设置版本 {SchemaVersion}。");
        }

        ArgumentNullException.ThrowIfNull(Metrics);
        ArgumentNullException.ThrowIfNull(MetricIcons);
        ArgumentNullException.ThrowIfNull(Widget);

        if (FastRefreshMilliseconds is not (1000 or 2000 or 5000))
        {
            throw new ArgumentOutOfRangeException(nameof(FastRefreshMilliseconds), "快通道刷新间隔只能是 1000、2000 或 5000 毫秒。");
        }

        if (SlowRefreshMilliseconds is not (3000 or 5000))
        {
            throw new ArgumentOutOfRangeException(nameof(SlowRefreshMilliseconds), "慢通道刷新间隔只能是 3000 或 5000 毫秒。");
        }

        if (!double.IsFinite(Opacity) || Opacity is < 0.20 or > 1.00)
        {
            throw new ArgumentOutOfRangeException(nameof(Opacity), "背景不透明度必须在 0.20 到 1.00 之间。");
        }

        if (Opacity is not (0.72 or 0.96) && Opacity != SnapOpacity(Opacity))
        {
            throw new ArgumentOutOfRangeException(nameof(Opacity), "背景不透明度必须按 0.05 步进；0.72 与 0.96 是保留的默认值。");
        }

        if (FontSize is < 10 or > 18)
        {
            throw new ArgumentOutOfRangeException(nameof(FontSize), "字号必须在 10 到 18 DIP 之间。");
        }

        if (!Enum.IsDefined(Theme))
        {
            throw new ArgumentOutOfRangeException(nameof(Theme), "主题只能为 system、dark 或 light。");
        }

        if (!double.IsFinite(Widget.X) || !double.IsFinite(Widget.Y) || (Widget.Docked & ~ValidDockedEdges) != 0)
        {
            throw new InvalidDataException("窗口位置或贴边状态无效。");
        }

        return this;
    }

    public PerformanceSettings Apply(SettingsPatch patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var metricsPatch = patch.Metrics;
        var iconsPatch = patch.MetricIcons;
        var next = this with
        {
            Metrics = metricsPatch is null
                ? Metrics
                : Metrics with
                {
                    Cpu = metricsPatch.Cpu ?? Metrics.Cpu,
                    Memory = metricsPatch.Memory ?? Metrics.Memory,
                    Gpu = metricsPatch.Gpu ?? Metrics.Gpu,
                    Network = metricsPatch.Network ?? Metrics.Network,
                    Time = metricsPatch.Time ?? Metrics.Time
                },
            MetricIcons = iconsPatch is null
                ? MetricIcons
                : MetricIcons with
                {
                    Cpu = iconsPatch.Cpu ?? MetricIcons.Cpu,
                    Memory = iconsPatch.Memory ?? MetricIcons.Memory,
                    Gpu = iconsPatch.Gpu ?? MetricIcons.Gpu,
                    Network = iconsPatch.Network ?? MetricIcons.Network,
                    Time = iconsPatch.Time ?? MetricIcons.Time
                },
            FastRefreshMilliseconds = patch.FastRefreshMilliseconds ?? FastRefreshMilliseconds,
            SlowRefreshMilliseconds = patch.SlowRefreshMilliseconds ?? SlowRefreshMilliseconds,
            Autostart = patch.Autostart ?? Autostart,
            AutoHideOnFullscreen = patch.AutoHideOnFullscreen ?? AutoHideOnFullscreen,
            CenterInTaskbarRow = patch.CenterInTaskbarRow ?? CenterInTaskbarRow,
            TransparentDisplay = patch.TransparentDisplay ?? TransparentDisplay,
            Opacity = patch.Opacity is { } opacity ? SnapOpacity(opacity) : Opacity,
            FontSize = patch.FontSize ?? FontSize,
            Theme = patch.Theme ?? Theme
        };

        return next.Validate();
    }

    public static double SnapOpacity(double opacity)
    {
        if (!double.IsFinite(opacity) || opacity is < 0.20 or > 1.00)
        {
            throw new ArgumentOutOfRangeException(nameof(opacity), "背景不透明度必须在 0.20 到 1.00 之间。");
        }

        var percentage = (decimal)opacity;
        var stepsFromMinimum = decimal.Round((percentage - 0.20m) / 0.05m, 0, MidpointRounding.AwayFromZero);
        return (double)Math.Clamp(0.20m + stepsFromMinimum * 0.05m, 0.20m, 1.00m);
    }

    private const DockedEdges ValidDockedEdges =
        DockedEdges.Top | DockedEdges.Bottom | DockedEdges.Left | DockedEdges.Right;
}

public sealed record MetricsSettingsPatch
{
    public bool? Cpu { get; init; }

    public bool? Memory { get; init; }

    public bool? Gpu { get; init; }

    public bool? Network { get; init; }

    public bool? Time { get; init; }
}

public sealed record MetricIconsSettingsPatch
{
    public bool? Cpu { get; init; }

    public bool? Memory { get; init; }

    public bool? Gpu { get; init; }

    public bool? Network { get; init; }

    public bool? Time { get; init; }
}

public sealed record SettingsPatch
{
    public MetricsSettingsPatch? Metrics { get; init; }

    public MetricIconsSettingsPatch? MetricIcons { get; init; }

    public int? FastRefreshMilliseconds { get; init; }

    public int? SlowRefreshMilliseconds { get; init; }

    public bool? Autostart { get; init; }

    public bool? AutoHideOnFullscreen { get; init; }

    public bool? CenterInTaskbarRow { get; init; }

    public bool? TransparentDisplay { get; init; }

    public double? Opacity { get; init; }

    public int? FontSize { get; init; }

    public BarTheme? Theme { get; init; }
}

public interface ISettingsStore
{
    bool RecoveredInvalidSettingsOnLastLoad => false;

    PerformanceSettings Load();

    void Save(PerformanceSettings settings);
}
