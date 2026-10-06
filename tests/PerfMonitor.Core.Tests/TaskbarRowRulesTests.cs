using PerfMonitor.Core.Settings;
using PerfMonitor.Core.Shell;

namespace PerfMonitor.Core.Tests;

public sealed class TaskbarRowRulesTests
{
    private static readonly DisplayInformation BottomTaskbarDisplay = new(
        new PlacementRect(0, 0, 1920, 1080),
        new PlacementRect(0, 0, 1920, 1040),
        DpiScale: 1.0);

    private static readonly DisplayInformation TopTaskbarDisplay = new(
        new PlacementRect(0, 0, 1920, 1080),
        new PlacementRect(0, 48, 1920, 1032),
        DpiScale: 1.0);

    private static readonly DisplayInformation LeftTaskbarDisplay = new(
        new PlacementRect(0, 0, 1920, 1080),
        new PlacementRect(16, 0, 1904, 1080),
        DpiScale: 1.0);

    private static readonly DisplayInformation RightTaskbarDisplay = new(
        new PlacementRect(0, 0, 1920, 1080),
        new PlacementRect(0, 0, 1904, 1080),
        DpiScale: 1.0);

    [Fact(DisplayName = "底部任务栏由 bounds 与工作区差分派生为底部水平带")]
    public void Bottom_taskbar_derives_a_bottom_horizontal_row()
    {
        var rows = TaskbarRowRules.DeriveRows(BottomTaskbarDisplay);

        var row = Assert.Single(rows);
        Assert.Equal(TaskbarRowKind.Bottom, row.Kind);
        Assert.Equal(new PlacementRect(0, 1040, 1920, 40), row.Rect);
    }

    [Fact(DisplayName = "顶部任务栏由 bounds 与工作区差分派生为顶部水平带")]
    public void Top_taskbar_derives_a_top_horizontal_row()
    {
        var rows = TaskbarRowRules.DeriveRows(TopTaskbarDisplay);

        var row = Assert.Single(rows);
        Assert.Equal(TaskbarRowKind.Top, row.Kind);
        Assert.Equal(new PlacementRect(0, 0, 1920, 48), row.Rect);
    }

    [Theory(DisplayName = "左右侧任务栏不产生任务栏行")]
    [InlineData(16, 0, 1888, 1080)]
    [InlineData(0, 0, 1904, 1080)]
    public void Side_taskbars_derive_no_rows(double workX, double workY, double workWidth, double workHeight)
    {
        var display = new DisplayInformation(
            new PlacementRect(0, 0, 1920, 1080),
            new PlacementRect(workX, workY, workWidth, workHeight),
            DpiScale: 1.0);

        Assert.Empty(TaskbarRowRules.DeriveRows(display));
    }

    [Fact(DisplayName = "顶底同时存在差分时派生两个行")]
    public void Differencing_on_both_edges_derives_two_rows()
    {
        var display = new DisplayInformation(
            new PlacementRect(0, 0, 1920, 1080),
            new PlacementRect(0, 48, 1920, 1000),
            DpiScale: 1.0);

        var rows = TaskbarRowRules.DeriveRows(display);

        Assert.Equal(2, rows.Count);
        Assert.Equal(TaskbarRowKind.Top, rows[0].Kind);
        Assert.Equal(new PlacementRect(0, 0, 1920, 48), rows[0].Rect);
        Assert.Equal(TaskbarRowKind.Bottom, rows[1].Kind);
        Assert.Equal(new PlacementRect(0, 1048, 1920, 32), rows[1].Rect);
    }

    [Fact(DisplayName = "无任务栏时无任务栏行")]
    public void Full_work_area_derives_no_rows()
    {
        var display = new DisplayInformation(
            new PlacementRect(0, 0, 1920, 1080),
            new PlacementRect(0, 0, 1920, 1080),
            DpiScale: 1.0);

        Assert.Empty(TaskbarRowRules.DeriveRows(display));
    }

    [Fact(DisplayName = "拖入底部行且满足底贴边落点时垂直居中到行且横向跟随拖动")]
    public void Settling_inside_the_bottom_row_centers_vertically_and_keeps_dragged_x()
    {
        var frame = new PlacementRect(100, 1050, 200, 40);

        var (settled, docked, inRow) = TaskbarRowRules.SettleRowPlacement(
            frame, BottomTaskbarDisplay, DockedEdges.Bottom, centerInRow: false);

        Assert.True(inRow);
        Assert.Equal(new PlacementRect(100, 1040, 200, 40), settled);
        Assert.Equal(DockedEdges.Bottom, docked);
    }

    [Fact(DisplayName = "拖入顶部行且满足顶贴边落点时垂直居中到行并置顶贴边")]
    public void Settling_inside_the_top_row_centers_vertically_and_docks_top()
    {
        var frame = new PlacementRect(100, 20, 200, 40);

        var (settled, docked, inRow) = TaskbarRowRules.SettleRowPlacement(
            frame, TopTaskbarDisplay, DockedEdges.Top, centerInRow: false);

        Assert.True(inRow);
        Assert.Equal(new PlacementRect(100, 4, 200, 40), settled);
        Assert.Equal(DockedEdges.Top, docked);
    }

    [Fact(DisplayName = "行内居中开启且满足贴边落点时松手弹回整行水平中心")]
    public void Center_in_row_snaps_the_card_back_to_the_horizontal_center()
    {
        var frame = new PlacementRect(100, 1050, 200, 40);

        var (settled, docked, inRow) = TaskbarRowRules.SettleRowPlacement(
            frame, BottomTaskbarDisplay, DockedEdges.Bottom, centerInRow: true);

        Assert.True(inRow);
        Assert.Equal(new PlacementRect(860, 1040, 200, 40), settled);
        Assert.Equal(DockedEdges.Bottom, docked);
    }

    [Fact(DisplayName = "行内松手位置横向超出行范围时夹入行内")]
    public void Settling_x_beyond_the_row_range_is_clamped_into_it()
    {
        var frame = new PlacementRect(1800, 1050, 200, 40);

        var (settled, docked, inRow) = TaskbarRowRules.SettleRowPlacement(
            frame, BottomTaskbarDisplay, DockedEdges.Bottom, centerInRow: false);

        Assert.True(inRow);
        Assert.Equal(new PlacementRect(1720, 1040, 200, 40), settled);
        Assert.Equal(DockedEdges.Bottom, docked);
    }

    [Fact(DisplayName = "行内落点保留既有左右贴边掩码并叠加对应顶底贴边")]
    public void Row_settlement_keeps_existing_side_docks_and_adds_the_row_edge()
    {
        var frame = new PlacementRect(16, 1050, 200, 40);

        var (_, docked, inRow) = TaskbarRowRules.SettleRowPlacement(
            frame, BottomTaskbarDisplay, DockedEdges.Left | DockedEdges.Bottom, centerInRow: false);

        Assert.True(inRow);
        Assert.Equal(DockedEdges.Left | DockedEdges.Bottom, docked);
    }

    [Fact(DisplayName = "卡片中心在行外时保持普通贴边结果")]
    public void Settling_outside_any_row_keeps_the_plain_snap_result()
    {
        var frame = new PlacementRect(100, 300, 200, 40);

        var (settled, docked, inRow) = TaskbarRowRules.SettleRowPlacement(
            frame, BottomTaskbarDisplay, DockedEdges.Left, centerInRow: true);

        Assert.False(inRow);
        Assert.Equal(frame, settled);
        Assert.Equal(DockedEdges.Left, docked);
    }

    [Fact(DisplayName = "中心进入行带即落位，无需先满足贴边前提")]
    public void Center_inside_the_row_settles_without_a_prior_dock()
    {
        var frame = new PlacementRect(100, 1050, 200, 40);

        var (settled, docked, inRow) = TaskbarRowRules.SettleRowPlacement(
            frame, BottomTaskbarDisplay, DockedEdges.None, centerInRow: true);

        Assert.True(inRow);
        Assert.Equal(new PlacementRect(860, 1040, 200, 40), settled);
        Assert.Equal(DockedEdges.None, docked);
    }

    [Fact(DisplayName = "顶行落位同样只按中心判定")]
    public void Top_row_settlement_also_only_requires_the_center()
    {
        var frame = new PlacementRect(100, 20, 200, 40);

        var (settled, docked, inRow) = TaskbarRowRules.SettleRowPlacement(
            frame, TopTaskbarDisplay, DockedEdges.None, centerInRow: false);

        Assert.True(inRow);
        Assert.Equal(new PlacementRect(100, 4, 200, 40), settled);
        Assert.Equal(DockedEdges.None, docked);
    }

    [Theory(DisplayName = "行内判定按中心纵坐标左闭右开")]
    [InlineData(1040, true)]
    [InlineData(1045, true)]
    [InlineData(1079, true)]
    [InlineData(1080, false)]
    [InlineData(1039, false)]
    public void Row_membership_uses_a_half_open_center_range(double centerY, bool expectedInRow)
    {
        var frame = new PlacementRect(100, centerY - 20, 200, 40);

        var (_, _, inRow) = TaskbarRowRules.SettleRowPlacement(
            frame, BottomTaskbarDisplay, DockedEdges.Bottom, centerInRow: false);

        Assert.Equal(expectedInRow, inRow);
    }

    [Fact(DisplayName = "行内横向按整个显示器宽度对应行矩形居中，不避让任何任务栏区域")]
    public void Row_centering_spans_the_full_display_width()
    {
        var frame = new PlacementRect(1800, 1050, 200, 40);

        var (settled, _, inRow) = TaskbarRowRules.SettleRowPlacement(
            frame, BottomTaskbarDisplay, DockedEdges.Bottom, centerInRow: true);

        Assert.True(inRow);
        Assert.Equal(860, settled.X);
    }

    [Fact(DisplayName = "行内恢复按当前行垂直居中并保留存储水平位置")]
    public void Restoring_a_row_placement_centers_vertically_and_keeps_the_stored_x()
    {
        var stored = new WidgetPlacement { X = 100, Y = 1050, Docked = DockedEdges.Bottom, InTaskbarRow = true };

        var restored = TaskbarRowRules.TryRestoreRowPlacement(
            stored, new PlacementRect(0, 0, 200, 40), BottomTaskbarDisplay, centerInRow: false,
            out var frame, out var docked);

        Assert.True(restored);
        Assert.Equal(new PlacementRect(100, 1040, 200, 40), frame);
        Assert.Equal(DockedEdges.Bottom, docked);
    }

    [Fact(DisplayName = "行内恢复在行内居中开启时回到整行水平中心")]
    public void Restoring_a_row_placement_with_centering_snaps_to_the_row_center()
    {
        var stored = new WidgetPlacement { X = 100, Y = 1050, Docked = DockedEdges.Bottom, InTaskbarRow = true };

        var restored = TaskbarRowRules.TryRestoreRowPlacement(
            stored, new PlacementRect(0, 0, 200, 40), BottomTaskbarDisplay, centerInRow: true,
            out var frame, out var docked);

        Assert.True(restored);
        Assert.Equal(new PlacementRect(860, 1040, 200, 40), frame);
        Assert.Equal(DockedEdges.Bottom, docked);
    }

    [Fact(DisplayName = "存储水平位置超出当前行范围时夹入行内")]
    public void Stored_x_outside_the_current_row_is_clamped_into_it()
    {
        var stored = new WidgetPlacement { X = 1800, Y = 1050, Docked = DockedEdges.Bottom, InTaskbarRow = true };

        var restored = TaskbarRowRules.TryRestoreRowPlacement(
            stored, new PlacementRect(0, 0, 200, 40), BottomTaskbarDisplay, centerInRow: false,
            out var frame, out _);

        Assert.True(restored);
        Assert.Equal(1720, frame.X);
    }

    [Fact(DisplayName = "顶部任务栏按顶行恢复行内落位")]
    public void Top_taskbar_restores_into_the_top_row()
    {
        var stored = new WidgetPlacement { X = 100, Y = 20, Docked = DockedEdges.Top, InTaskbarRow = true };

        var restored = TaskbarRowRules.TryRestoreRowPlacement(
            stored, new PlacementRect(0, 0, 200, 40), TopTaskbarDisplay, centerInRow: false,
            out var frame, out var docked);

        Assert.True(restored);
        Assert.Equal(new PlacementRect(100, 4, 200, 40), frame);
        Assert.Equal(DockedEdges.Top, docked);
    }

    [Fact(DisplayName = "当前任务栏无行时不恢复行内")]
    public void No_current_row_fails_row_restoration()
    {
        var stored = new WidgetPlacement { X = 100, Y = 1050, Docked = DockedEdges.Bottom, InTaskbarRow = true };

        var restored = TaskbarRowRules.TryRestoreRowPlacement(
            stored, new PlacementRect(0, 0, 200, 40), LeftTaskbarDisplay, centerInRow: false,
            out _, out _);

        Assert.False(restored);
    }

    [Fact(DisplayName = "行内标志置位即按当前行恢复，不要求贴边掩码含顶底边")]
    public void Row_restoration_requires_only_the_stored_row_flag()
    {
        var stored = new WidgetPlacement { X = 100, Y = 300, Docked = DockedEdges.Left, InTaskbarRow = true };

        var restored = TaskbarRowRules.TryRestoreRowPlacement(
            stored, new PlacementRect(0, 0, 200, 40), BottomTaskbarDisplay, centerInRow: false,
            out var frame, out var docked);

        Assert.True(restored);
        Assert.Equal(new PlacementRect(100, 1040, 200, 40), frame);
        Assert.Equal(DockedEdges.Left, docked);
    }
}
