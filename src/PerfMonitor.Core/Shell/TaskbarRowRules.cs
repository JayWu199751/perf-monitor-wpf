using PerfMonitor.Core.Settings;

namespace PerfMonitor.Core.Shell;

public enum TaskbarRowKind
{
    None,
    Top,
    Bottom
}

/// <summary>显示器顶部或底部由任务栏占据的水平带；横向覆盖整个显示器宽度。</summary>
public readonly record struct TaskbarRow(TaskbarRowKind Kind, PlacementRect Rect);

/// <summary>
/// 任务栏可见性守卫端口：由平台 adapter 实现原生 z 序检查与恢复（判据为根窗口
/// 归属或 z 序，不按截图）；Core 决定守卫节奏（快守卫/慢刷新）与启停时机。
/// </summary>
public interface ITaskbarVisibilityGuardPort
{
    /// <summary>快守卫：卡片根窗口被任务栏根窗口遮挡时恢复其可见性。</summary>
    void EnsureAboveTaskbar();

    /// <summary>慢刷新：重新枚举任务栏根窗口句柄，explorer 重启后自动恢复。</summary>
    void RefreshTaskbarHandles();

    /// <summary>守卫停止（隐藏或离开行内）后回调，把卡片恢复到普通 z 序层。</summary>
    void OnGuardStopped();
}

/// <summary>
/// 任务栏行派生与行内落位的纯几何规则：不依赖 WPF/Win32，坐标均为物理像素。
/// 左右侧任务栏（工作区左右收窄）不产生行；无有效行时保持普通贴边行为。
/// </summary>
public static class TaskbarRowRules
{
    /// <summary>按显示器 bounds 与工作区差分派生顶/底任务栏行（顶行在前）。</summary>
    public static IReadOnlyList<TaskbarRow> DeriveRows(DisplayInformation display)
    {
        var bounds = display.Bounds;
        var work = display.WorkArea;
        var rows = new List<TaskbarRow>(2);

        var topHeight = work.Y - bounds.Y;
        if (topHeight > 0)
        {
            rows.Add(new TaskbarRow(
                TaskbarRowKind.Top,
                new PlacementRect(bounds.X, bounds.Y, bounds.Width, topHeight)));
        }

        var bottomHeight = bounds.Bottom - work.Bottom;
        if (bottomHeight > 0)
        {
            rows.Add(new TaskbarRow(
                TaskbarRowKind.Bottom,
                new PlacementRect(bounds.X, work.Bottom, bounds.Width, bottomHeight)));
        }

        return rows;
    }

    /// <summary>
    /// 行内落位结算：卡片中心位于某行内（左闭右开）时垂直居中到该行并叠加对应顶/底贴边；
    /// centerInRow 开启时再弹回整行水平中心；行外原样返回普通贴边结果。
    /// </summary>
    public static (PlacementRect Frame, DockedEdges Docked, bool InRow) SettleRowPlacement(
        PlacementRect frame,
        DisplayInformation display,
        DockedEdges settledDocked,
        bool centerInRow)
    {
        var rows = DeriveRows(display);
        foreach (var row in rows)
        {
            var rect = row.Rect;
            var isInRow = frame.CenterX >= rect.X && frame.CenterX < rect.Right &&
                frame.CenterY >= rect.Y && frame.CenterY < rect.Bottom;
            if (!isInRow)
            {
                continue;
            }

            var rowEdge = row.Kind == TaskbarRowKind.Top ? DockedEdges.Top : DockedEdges.Bottom;
            var docked = settledDocked | rowEdge;
            var x = centerInRow ? rect.CenterX - frame.Width / 2 : frame.X;
            var y = rect.Y + (rect.Height - frame.Height) / 2;
            return (frame with { X = x, Y = y }, docked, true);
        }

        return (frame, settledDocked, false);
    }
}
