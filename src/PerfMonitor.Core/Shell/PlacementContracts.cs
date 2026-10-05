using PerfMonitor.Core.Settings;

namespace PerfMonitor.Core.Shell;

/// <summary>
/// 以物理像素表示的矩形（虚拟桌面坐标空间）；所有落位计算在该空间进行，
/// 以统一处理负坐标与混合 DPI 显示器。
/// </summary>
public readonly record struct PlacementRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + Width / 2;

    public double CenterY => Y + Height / 2;
}

/// <summary>
/// 宿主窗口框架内的透明留白（正数表示该侧不可见区域）；卡片可视矩形为框架按此收缩后的结果。
/// </summary>
public readonly record struct PlacementInsets(double Left, double Top, double Right, double Bottom)
{
    public static PlacementInsets Zero { get; } = new();
}

/// <summary>
/// 一台显示器的整体边界、可见工作区与有效 DPI 缩放，均以物理像素表示。
/// </summary>
public readonly record struct DisplayInformation(PlacementRect Bounds, PlacementRect WorkArea, double DpiScale);

/// <summary>
/// 显示器与工作区信息端口；由各平台 adapter 实现（如 Windows 枚举）。
/// </summary>
public interface IDisplayEnvironmentSource
{
    /// <summary>枚举当前全部显示器；枚举失败时返回空列表。</summary>
    IReadOnlyList<DisplayInformation> GetDisplays();

    /// <summary>工作区、分辨率或 DPI 变化后触发，通知订阅方重新落位。</summary>
    event EventHandler? DisplaysChanged;
}

/// <summary>
/// 性能条窗口框架的读写端口；坐标为物理像素。窗口 controller 通过该端口
/// 读取原生实际尺寸并写回落位，避免 DIP 换算歧义。
/// </summary>
public interface IPerformanceBarPlacementPort
{
    PlacementRect ReadFrame();

    void SetFramePosition(double x, double y);
}

public static class PerformanceSettingsPlacementExtensions
{
    /// <summary>用窗口 controller 维护的唯一最后位置覆盖设置中的窗口位置。</summary>
    public static PerformanceSettings WithWidget(this PerformanceSettings settings, WidgetPlacement placement) =>
        settings.Widget == placement ? settings : settings with { Widget = placement };
}
