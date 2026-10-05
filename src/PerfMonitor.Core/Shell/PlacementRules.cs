using PerfMonitor.Core.Settings;

namespace PerfMonitor.Core.Shell;

/// <summary>
/// 贴边与落位的纯几何规则：不依赖 WPF/Win32，坐标均为物理像素。
/// 阈值与安全边框以 DIP 定义，按目标显示器 DPI 缩放后使用。
/// </summary>
public static class PlacementRules
{
    public const double SnapThresholdDips = 8;

    public const double SnapSafeBorderDips = 1;

    /// <summary>按卡片中心解析所在显示器（含负坐标）；中心不在任何显示器内时返回 null。</summary>
    public static DisplayInformation? FindDisplayByCenter(
        IReadOnlyList<DisplayInformation> displays,
        PlacementRect frame)
    {
        for (var index = 0; index < displays.Count; index++)
        {
            var bounds = displays[index].Bounds;
            if (frame.CenterX >= bounds.X && frame.CenterX < bounds.Right &&
                frame.CenterY >= bounds.Y && frame.CenterY < bounds.Bottom)
            {
                return displays[index];
            }
        }

        return null;
    }

    /// <summary>按卡片中心到显示器边界的距离解析最近显示器；列表为空时返回 null。</summary>
    public static DisplayInformation? FindNearestDisplay(
        IReadOnlyList<DisplayInformation> displays,
        PlacementRect frame)
    {
        if (displays.Count == 0)
        {
            return null;
        }

        var nearest = displays[0];
        var nearestDistance = DistanceToBounds(nearest.Bounds, frame);
        for (var index = 1; index < displays.Count; index++)
        {
            var distance = DistanceToBounds(displays[index].Bounds, frame);
            if (distance < nearestDistance)
            {
                nearest = displays[index];
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    /// <summary>
    /// 松手/显示器变化后结算贴边：卡片任一边在阈值内即贴边（四角自然组合），
    /// 已贴边被推出工作区外侧时保持吸附，只有向屏幕内离开阈值才解除。
    /// </summary>
    public static (PlacementRect Frame, DockedEdges Docked) SettleSnap(
        PlacementRect frame,
        DisplayInformation display,
        DockedEdges currentDocked,
        PlacementInsets insets)
    {
        var threshold = SnapThresholdDips * display.DpiScale;
        var border = SnapSafeBorderDips * display.DpiScale;
        var card = Deflate(frame, insets);
        var work = display.WorkArea;

        var docked = DockedEdges.None;
        if (IsWithinSnapRange(card.Y - work.Y, threshold, currentDocked, DockedEdges.Top))
        {
            docked |= DockedEdges.Top;
        }

        if (IsWithinSnapRange(work.Bottom - card.Bottom, threshold, currentDocked, DockedEdges.Bottom))
        {
            docked |= DockedEdges.Bottom;
        }

        if (IsWithinSnapRange(card.X - work.X, threshold, currentDocked, DockedEdges.Left))
        {
            docked |= DockedEdges.Left;
        }

        if (IsWithinSnapRange(work.Right - card.Right, threshold, currentDocked, DockedEdges.Right))
        {
            docked |= DockedEdges.Right;
        }

        var settled = ApplyDockedMask(frame, work, border, docked, insets);
        return (settled, docked);
    }

    /// <summary>把框架整体夹入指定显示器可见工作区（框架不小于工作区时贴其原点）。</summary>
    public static PlacementRect ClampIntoWorkArea(PlacementRect frame, DisplayInformation display)
    {
        var work = display.WorkArea;
        var x = Clamp(frame.X, work.X, Math.Max(work.X, work.Right - frame.Width));
        var y = Clamp(frame.Y, work.Y, Math.Max(work.Y, work.Bottom - frame.Height));
        return frame with { X = x, Y = y };
    }

    /// <summary>
    /// 恢复待恢复位置：先按存储中心点选择显示器（消失时取最近），
    /// 行内标志置位时优先按当前任务栏几何恢复行内落位（无行时回落），
    /// 否则夹回可见工作区后按贴边掩码重新落位。返回恢复后的框架与生效掩码。
    /// </summary>
    public static (PlacementRect Frame, DockedEdges Docked) RestorePlacement(
        WidgetPlacement stored,
        PlacementRect frameSize,
        IReadOnlyList<DisplayInformation> displays,
        PlacementInsets insets,
        bool centerInRow = false)
    {
        var center = frameSize with { X = stored.X, Y = stored.Y };
        var resolved = FindDisplayByCenter(displays, center) ?? FindNearestDisplay(displays, center);
        if (resolved is null)
        {
            return (center, stored.Docked);
        }

        var target = resolved.Value;

        if (stored.InTaskbarRow &&
            TaskbarRowRules.TryRestoreRowPlacement(stored, frameSize, target, centerInRow, out var rowFrame, out var rowDocked))
        {
            return (rowFrame, rowDocked);
        }

        var frame = ClampIntoWorkArea(frameSize with { X = stored.X, Y = stored.Y }, target);
        var border = SnapSafeBorderDips * target.DpiScale;
        var restored = ApplyDockedMask(frame, target.WorkArea, border, stored.Docked, insets);
        return (restored, stored.Docked);
    }

    private static bool IsWithinSnapRange(
        double distance,
        double threshold,
        DockedEdges currentDocked,
        DockedEdges edge)
    {
        // 距离为正表示卡片边缘在工作区内侧；阈值内双向吸附，
        // 推出外侧超过阈值的已贴边保持吸附，向内超过阈值才解除。
        return distance <= threshold && (distance >= -threshold || (currentDocked & edge) != 0);
    }

    private static PlacementRect ApplyDockedMask(
        PlacementRect frame,
        PlacementRect work,
        double border,
        DockedEdges docked,
        PlacementInsets insets)
    {
        var x = (docked & DockedEdges.Left) != 0
            ? work.X + border - insets.Left
            : (docked & DockedEdges.Right) != 0
                ? work.Right - frame.Width - border + insets.Right
                : frame.X;
        var y = (docked & DockedEdges.Top) != 0
            ? work.Y + border - insets.Top
            : (docked & DockedEdges.Bottom) != 0
                ? work.Bottom - frame.Height - border + insets.Bottom
                : frame.Y;
        return frame with { X = x, Y = y };
    }

    private static PlacementRect Deflate(PlacementRect frame, PlacementInsets insets) => new(
        frame.X + insets.Left,
        frame.Y + insets.Top,
        Math.Max(0, frame.Width - insets.Left - insets.Right),
        Math.Max(0, frame.Height - insets.Top - insets.Bottom));

    private static double Clamp(double value, double minimum, double maximum) =>
        Math.Max(minimum, Math.Min(value, maximum));

    private static double DistanceToBounds(PlacementRect bounds, PlacementRect frame)
    {
        var dx = Math.Max(bounds.X - frame.CenterX, Math.Max(0, frame.CenterX - bounds.Right));
        var dy = Math.Max(bounds.Y - frame.CenterY, Math.Max(0, frame.CenterY - bounds.Bottom));
        return Math.Max(dx, dy);
    }
}
