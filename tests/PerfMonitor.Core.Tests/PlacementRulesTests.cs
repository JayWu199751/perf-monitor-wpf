using PerfMonitor.Core.Settings;
using PerfMonitor.Core.Shell;

namespace PerfMonitor.Core.Tests;

public sealed class PlacementRulesTests
{
    private static readonly DisplayInformation SingleDisplay = new(
        new PlacementRect(0, 0, 1920, 1080),
        new PlacementRect(0, 0, 1920, 1040),
        DpiScale: 1.0);

    private static PlacementRect Frame(double x, double y) => new(x, y, 200, 40);

    [Fact(DisplayName = "贴边掩码位值按 top/bottom 在 left/right 之前的规范顺序排序")]
    public void Docked_edge_flags_order_top_and_bottom_before_left_and_right()
    {
        Assert.True((int)DockedEdges.Top < (int)DockedEdges.Bottom);
        Assert.True((int)DockedEdges.Bottom < (int)DockedEdges.Left);
        Assert.True((int)DockedEdges.Left < (int)DockedEdges.Right);
    }

    [Theory(DisplayName = "四边在 8 DIP 阈值内贴边并保留 1 DIP 安全边框")]
    [InlineData(5, 300, DockedEdges.Left, 1, 300)]
    [InlineData(1725, 300, DockedEdges.Right, 1719, 300)]
    [InlineData(300, 4, DockedEdges.Top, 300, 1)]
    [InlineData(300, 1005, DockedEdges.Bottom, 300, 999)]
    public void Edges_within_threshold_snap_with_a_one_dip_safe_border(
        double x, double y, DockedEdges docked, double expectedX, double expectedY)
    {
        var (settled, mask) = PlacementRules.SettleSnap(Frame(x, y), SingleDisplay, DockedEdges.None, PlacementInsets.Zero);

        Assert.Equal(docked, mask);
        Assert.Equal(expectedX, settled.X);
        Assert.Equal(expectedY, settled.Y);
    }

    [Fact(DisplayName = "四角在阈值内组合贴边")]
    public void Corners_within_threshold_combine_docked_edges()
    {
        var (settled, mask) = PlacementRules.SettleSnap(Frame(5, 3), SingleDisplay, DockedEdges.None, PlacementInsets.Zero);

        Assert.Equal(DockedEdges.Top | DockedEdges.Left, mask);
        Assert.Equal(1, settled.X);
        Assert.Equal(1, settled.Y);
    }

    [Fact(DisplayName = "超过阈值不贴边且位置保持跟手")]
    public void Edges_beyond_threshold_do_not_snap_and_keep_the_dragged_position()
    {
        var (settled, mask) = PlacementRules.SettleSnap(Frame(9, 300), SingleDisplay, DockedEdges.None, PlacementInsets.Zero);

        Assert.Equal(DockedEdges.None, mask);
        Assert.Equal(9, settled.X);
        Assert.Equal(300, settled.Y);
    }

    [Fact(DisplayName = "混合 DPI 下阈值与安全边框按目标显示器缩放")]
    public void Threshold_and_border_scale_with_the_target_display_dpi()
    {
        var display = SingleDisplay with { DpiScale = 1.5 };

        var inside = PlacementRules.SettleSnap(Frame(12, 300), display, DockedEdges.None, PlacementInsets.Zero);
        var outside = PlacementRules.SettleSnap(Frame(13, 300), display, DockedEdges.None, PlacementInsets.Zero);

        Assert.Equal(DockedEdges.Left, inside.Docked);
        Assert.Equal(1.5, inside.Frame.X);
        Assert.Equal(DockedEdges.None, outside.Docked);
        Assert.Equal(13, outside.Frame.X);
    }

    [Fact(DisplayName = "已贴边向外拖离仍吸附回工作区边缘")]
    public void Docked_edges_pushed_outward_snap_back_to_the_work_area()
    {
        var (settled, mask) = PlacementRules.SettleSnap(Frame(-30, 300), SingleDisplay, DockedEdges.Left, PlacementInsets.Zero);

        Assert.Equal(DockedEdges.Left, mask);
        Assert.Equal(1, settled.X);
    }

    [Fact(DisplayName = "已贴边向屏幕内拖过阈值后解除贴边")]
    public void Docked_edges_dragged_inward_beyond_threshold_undock()
    {
        var (settled, mask) = PlacementRules.SettleSnap(Frame(20, 300), SingleDisplay, DockedEdges.Left, PlacementInsets.Zero);

        Assert.Equal(DockedEdges.None, mask);
        Assert.Equal(20, settled.X);
    }

    [Fact(DisplayName = "透明宿主留白按卡片可视边缘结算并把透明部分推出工作区")]
    public void Transparent_host_insets_settle_on_the_visible_card_edge()
    {
        var insets = new PlacementInsets(Left: 6, Top: 0, Right: 0, Bottom: 0);

        var (settled, mask) = PlacementRules.SettleSnap(Frame(-5, 300), SingleDisplay, DockedEdges.None, insets);

        Assert.Equal(DockedEdges.Left, mask);
        // 卡片可视边缘落在工作区 + 1 DIP；宿主透明部分留在工作区之外。
        Assert.Equal(1 - 6, settled.X);
    }

    [Fact(DisplayName = "按卡片中心解析目标显示器并支持负坐标")]
    public void Target_display_is_resolved_by_the_card_center_including_negative_coordinates()
    {
        var displays = new[]
        {
            SingleDisplay,
            new DisplayInformation(
                new PlacementRect(-1920, 0, 1920, 1080),
                new PlacementRect(-1920, 0, 1920, 1040),
                DpiScale: 1.25)
        };
        var frame = new PlacementRect(-1100, 400, 200, 40);

        var display = PlacementRules.FindDisplayByCenter(displays, frame);

        Assert.NotNull(display);
        Assert.Equal(-1920, display.Value.Bounds.X);
    }

    [Fact(DisplayName = "中心不在任何显示器内时解析最近显示器")]
    public void Center_outside_all_displays_resolves_the_nearest_display()
    {
        var displays = new[] { SingleDisplay };
        var frame = new PlacementRect(4000, 400, 200, 40);

        var display = PlacementRules.FindNearestDisplay(displays, frame);

        Assert.Equal(SingleDisplay, display);
    }

    [Fact(DisplayName = "目标显示器消失时夹回剩余显示器的可见工作区")]
    public void Frames_are_clamped_back_into_a_visible_work_area_when_the_display_disappears()
    {
        var frame = new PlacementRect(-1100, 400, 200, 40);

        var clamped = PlacementRules.ClampIntoWorkArea(frame, SingleDisplay);

        Assert.Equal(0, clamped.X);
        Assert.Equal(400, clamped.Y);
    }

    [Fact(DisplayName = "恢复位置按贴边掩码重新落位并保留掩码")]
    public void Restored_positions_re_apply_the_docked_mask_and_keep_it()
    {
        var stored = new WidgetPlacement { X = 100, Y = 50, Docked = DockedEdges.Top };
        var frameSize = new PlacementRect(0, 0, 200, 40);

        var (restored, docked) = PlacementRules.RestorePlacement(stored, frameSize, [SingleDisplay], PlacementInsets.Zero);

        Assert.Equal(100, restored.X);
        Assert.Equal(1, restored.Y);
        Assert.Equal(DockedEdges.Top, docked);
    }

    [Fact(DisplayName = "恢复位置先夹回可见工作区再按掩码贴边")]
    public void Restored_positions_are_clamped_before_the_docked_mask_is_applied()
    {
        var stored = new WidgetPlacement { X = 100, Y = -50, Docked = DockedEdges.Top };
        var frameSize = new PlacementRect(0, 0, 200, 40);

        var (restored, docked) = PlacementRules.RestorePlacement(stored, frameSize, [SingleDisplay], PlacementInsets.Zero);

        Assert.Equal(1, restored.Y);
        Assert.Equal(DockedEdges.Top, docked);
    }

    [Fact(DisplayName = "待恢复显示器消失时按最近显示器恢复")]
    public void Restoration_falls_back_to_the_nearest_display_when_the_stored_display_is_gone()
    {
        var stored = new WidgetPlacement { X = -1100, Y = 400, Docked = DockedEdges.None };
        var frameSize = new PlacementRect(0, 0, 200, 40);

        var (restored, docked) = PlacementRules.RestorePlacement(stored, frameSize, [SingleDisplay], PlacementInsets.Zero);

        Assert.Equal(DockedEdges.None, docked);
        Assert.Equal(0, restored.X);
        Assert.Equal(400, restored.Y);
    }
}
