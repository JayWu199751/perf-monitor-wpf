using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using PerfMonitor.App.Shell;

namespace PerfMonitor.App.Tests;

public sealed class ShellContextMenuThemeTests
{
    [Fact(DisplayName = "暗色主题落性能条深色令牌并挂载根/项样式")]
    public void Dark_theme_uses_bar_dark_tokens() => RunOnSta(() =>
    {
        var menu = new ContextMenu();
        new ShellContextMenuTheme(menu, dark: true);

        AssertBrush(menu, "ShellMenuBackgroundBrush", 0xFF, 0x12, 0x15, 0x19);
        AssertBrush(menu, "ShellMenuForegroundBrush", 0xFF, 0xF3, 0xF5, 0xF7);
        AssertBrush(menu, "ShellMenuSecondaryBrush", 0xFF, 0x92, 0x97, 0x9F);
        AssertBrush(menu, "ShellMenuCheckBrush", 0xFF, 0xF3, 0xF5, 0xF7);
        AssertBrush(menu, "ShellMenuHoverBrush", 0x0F, 0xFF, 0xFF, 0xFF);
        AssertBrush(menu, "ShellMenuBorderBrush", 0x1A, 0xFF, 0xFF, 0xFF);
        AssertBrush(menu, "ShellMenuDividerBrush", 0x21, 0xFF, 0xFF, 0xFF);
        Assert.Equal(0.32, Assert.IsType<DropShadowEffect>(menu.Resources["ShellMenuShadowEffect"]).Opacity);
    });

    [Fact(DisplayName = "亮色主题落推导亮色令牌并降低投影强度")]
    public void Light_theme_uses_derived_light_tokens() => RunOnSta(() =>
    {
        var menu = new ContextMenu();
        new ShellContextMenuTheme(menu, dark: false);

        AssertBrush(menu, "ShellMenuBackgroundBrush", 0xFF, 0xF6, 0xF7, 0xF9);
        AssertBrush(menu, "ShellMenuForegroundBrush", 0xFF, 0x1A, 0x1D, 0x22);
        AssertBrush(menu, "ShellMenuSecondaryBrush", 0xFF, 0x5F, 0x65, 0x6D);
        AssertBrush(menu, "ShellMenuCheckBrush", 0xFF, 0x1A, 0x1D, 0x22);
        AssertBrush(menu, "ShellMenuHoverBrush", 0x0A, 0x00, 0x00, 0x00);
        AssertBrush(menu, "ShellMenuBorderBrush", 0x1A, 0x00, 0x00, 0x00);
        AssertBrush(menu, "ShellMenuDividerBrush", 0x21, 0x00, 0x00, 0x00);
        Assert.Equal(0.16, Assert.IsType<DropShadowEffect>(menu.Resources["ShellMenuShadowEffect"]).Opacity);
    });

    [Fact(DisplayName = "构造即挂载根样式、项容器样式与投影落点补偿")]
    public void Attach_wires_styles_and_placement_compensation() => RunOnSta(() =>
    {
        var menu = new ContextMenu();
        new ShellContextMenuTheme(menu, dark: true);

        Assert.NotNull(menu.Style);
        Assert.Equal(typeof(ContextMenu), menu.Style.TargetType);
        Assert.Contains(menu.Style.Setters, setter =>
            setter is Setter { Property: var property, Value: ControlTemplate }
            && property == Control.TemplateProperty);
        Assert.NotNull(menu.ItemContainerStyle);
        Assert.Equal(typeof(MenuItem), menu.ItemContainerStyle.TargetType);
        Assert.Equal(-ShellContextMenuTheme.ShadowInsetDips, menu.HorizontalOffset);
        Assert.Equal(0, menu.VerticalOffset);
    });

    [Fact(DisplayName = "未勾选的可勾项引用标签色，勾选与动作项引用正文色")]
    public void Foreground_references_follow_check_state() => RunOnSta(() =>
    {
        var menu = new ContextMenu();
        new ShellContextMenuTheme(menu, dark: true);

        var uncheckedItem = new MenuItem { IsCheckable = true, IsChecked = false };
        ShellContextMenuTheme.ApplyForeground(uncheckedItem, isCheckable: true, isChecked: false);
        var checkedItem = new MenuItem { IsCheckable = true, IsChecked = true };
        ShellContextMenuTheme.ApplyForeground(checkedItem, isCheckable: true, isChecked: true);
        var actionItem = new MenuItem();
        ShellContextMenuTheme.ApplyForeground(actionItem, isCheckable: false, isChecked: false);
        // 资源引用沿逻辑树解析，先挂进菜单再求值。
        menu.Items.Add(uncheckedItem);
        menu.Items.Add(checkedItem);
        menu.Items.Add(actionItem);

        // 资源引用存为表达式而非冻结画刷，保证主题切换即时跟随。
        Assert.True(IsResourceReference(uncheckedItem));
        Assert.True(IsResourceReference(checkedItem));
        Assert.True(IsResourceReference(actionItem));

        // 求值后：未勾行取标签色，其余取正文色。
        Assert.Equal(
            ((SolidColorBrush)menu.Resources["ShellMenuSecondaryBrush"]).Color,
            ResolveColor(uncheckedItem));
        Assert.Equal(
            ((SolidColorBrush)menu.Resources["ShellMenuForegroundBrush"]).Color,
            ResolveColor(checkedItem));
        Assert.Equal(
            ((SolidColorBrush)menu.Resources["ShellMenuForegroundBrush"]).Color,
            ResolveColor(actionItem));
    });

    [Fact(DisplayName = "同主题重复 Apply 幂等，切换主题后画刷翻转")]
    public void Apply_is_idempotent_and_flips_on_theme_change() => RunOnSta(() =>
    {
        var menu = new ContextMenu();
        var theme = new ShellContextMenuTheme(menu, dark: true);
        var darkBackground = menu.Resources["ShellMenuBackgroundBrush"];

        theme.Apply(dark: true);
        Assert.Same(darkBackground, menu.Resources["ShellMenuBackgroundBrush"]);

        theme.Apply(dark: false);
        Assert.NotSame(darkBackground, menu.Resources["ShellMenuBackgroundBrush"]);
        var light = (SolidColorBrush)menu.Resources["ShellMenuBackgroundBrush"];
        Assert.Equal(Color.FromArgb(0xFF, 0xF6, 0xF7, 0xF9), light.Color);
    });

    private static void AssertBrush(ContextMenu menu, string key, byte a, byte r, byte g, byte b)
    {
        var brush = Assert.IsType<SolidColorBrush>(menu.Resources[key]);
        Assert.Equal(Color.FromArgb(a, r, g, b), brush.Color);
    }

    private static bool IsResourceReference(MenuItem item) =>
        item.ReadLocalValue(MenuItem.ForegroundProperty) is { } value
        && value != DependencyProperty.UnsetValue
        && value is not Brush;

    private static Color ResolveColor(MenuItem item) =>
        ((SolidColorBrush)item.Foreground).Color;

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF 验证线程未在时限内完成。");
        failure?.Throw();
    }
}
