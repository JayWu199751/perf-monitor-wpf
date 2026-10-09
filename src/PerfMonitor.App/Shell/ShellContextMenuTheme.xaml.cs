using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using Color = System.Windows.Media.Color;
namespace PerfMonitor.App.Shell;

/// <summary>
/// 托盘/性能条共享菜单的亮暗主题皮肤：XAML 侧定义模板与默认（暗色）画刷，
/// Apply(dark) 按有效主题逐键覆盖画刷（同设置窗 ApplyTheme 的模式），
/// 模板里的 DynamicResource 引用让开着的菜单也即时跟随切换。
/// </summary>
public partial class ShellContextMenuTheme : ResourceDictionary
{
    /// <summary>投影留白；与 PerformanceBarWindow.ShadowInsetDips 同值。</summary>
    public const double ShadowInsetDips = 16;

    public const string SeparatorStyleKey = "ShellMenuSeparatorStyle";

    private const string BackgroundKey = "ShellMenuBackgroundBrush";
    private const string ForegroundKey = "ShellMenuForegroundBrush";
    private const string SecondaryKey = "ShellMenuSecondaryBrush";
    private const string CheckKey = "ShellMenuCheckBrush";
    private const string HoverKey = "ShellMenuHoverBrush";
    private const string BorderKey = "ShellMenuBorderBrush";
    private const string DividerKey = "ShellMenuDividerBrush";
    private const string ShadowKey = "ShellMenuShadowEffect";

    private readonly WpfContextMenu _menu;
    private bool _dark;
    private bool _applied;

    public ShellContextMenuTheme(WpfContextMenu menu, bool dark)
    {
        InitializeComponent();
        _menu = menu;
        menu.Resources.MergedDictionaries.Add(this);
        menu.Style = (Style)this["ShellMenuRootStyle"];
        menu.ItemContainerStyle = (Style)this["ShellMenuItemStyle"];
        // 外围 16 DIP 投影留白把 popup 整体撑大：x 方向 ContextMenu 定位含留白，需 -16 补偿回鼠标点；
        // y 方向 MousePoint 定位不叠加留白（实测 Border 顶 = 鼠标 + VerticalOffset），故归零。
        menu.HorizontalOffset = -ShadowInsetDips;
        menu.VerticalOffset = 0;
        Apply(dark);
    }

    /// <summary>按暗色与否覆盖画刷；同一主题重复调用是空操作。</summary>
    public void Apply(bool dark)
    {
        if (_applied && _dark == dark)
        {
            return;
        }

        _dark = dark;
        _applied = true;
        _menu.Resources[BackgroundKey] = Brush(dark ? 0xFF121519L : 0xFFF6F7F9L);
        _menu.Resources[ForegroundKey] = Brush(dark ? 0xFFF3F5F7L : 0xFF1A1D22L);
        _menu.Resources[SecondaryKey] = Brush(dark ? 0xFF92979FL : 0xFF5F656DL);
        _menu.Resources[CheckKey] = Brush(dark ? 0xFFF3F5F7L : 0xFF1A1D22L);
        _menu.Resources[HoverKey] = Brush(dark ? 0x0FFFFFFFL : 0x0A000000L);
        _menu.Resources[BorderKey] = Brush(dark ? 0x1AFFFFFFL : 0x1A000000L);
        _menu.Resources[DividerKey] = Brush(dark ? 0x21FFFFFFL : 0x21000000L);
        _menu.Resources[ShadowKey] = new DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 9,
            ShadowDepth = 2,
            Direction = 270,
            Opacity = dark ? 0.32 : 0.16
        };
    }

    /// <summary>勾选行与动作项用正文色，未勾的 checkable 行用标签色。</summary>
    public static void ApplyForeground(WpfMenuItem item, bool isCheckable, bool isChecked)
    {
        item.SetResourceReference(
            WpfMenuItem.ForegroundProperty,
            isCheckable && !isChecked ? SecondaryKey : ForegroundKey);
    }

    private static SolidColorBrush Brush(long argb) =>
        Brush((byte)(argb >> 24 & 0xFF), (byte)(argb >> 16 & 0xFF), (byte)(argb >> 8 & 0xFF), (byte)(argb & 0xFF));

    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
