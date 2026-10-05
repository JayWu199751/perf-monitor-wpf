# HTML 原型验证记录

2026-10-03，使用本机系统Chrome与playwright-cli。范围仅为本包网页原型，不是WPF实现。视口1600×1180与390×844；无远程字体/脚本/图片依赖。

23项行为检查通过：默认五段与72%/12号；宽裕视口四组设置完整可见；多位数据扩宽/回落不缩；行内居中菜单同步与近似几何；透明缺失态保留文字；全屏进入自动隐藏/手动显示优先/退出保持/手动隐藏跨周期；关闭自动隐藏恢复；全关64px命中区；指标组合重置峰值；字号18与背景20%文字自身不淡化；设置关闭隐藏/重新打开；小屏无页面横向溢出与完整性能条展示；真实浏览器pointer拖动；演示退出/重置。

检查过程中发现历史峰值宿主比当前卡片宽时，卡片在宿主内左对齐造成行内中心偏差，已改为居中后重跑通过。小屏按预览视口缩放性能条，仅缩放网页展示、不改变配置字号。

重新加载后的浏览器console无错误/警告。初次缺favicon造成404，已加入内嵌SVG图标消除。截图仅浏览器渲染产物，无人工后期修图。

## 预览文件

| 文件 | 内容 |
| --- | --- |
| [01-overview-light.png](previews/01-overview-light.png) | 亮色性能条与完整设置窗 |
| [02-taskbar-dark-menu.png](previews/02-taskbar-dark-menu.png) | 深色任务栏行内、水平居中与共享菜单 |
| [03-transparent-missing.png](previews/03-transparent-missing.png) | 透明显示和不可用读数 |
| [04-mobile.png](previews/04-mobile.png) | 窄屏展示，原型工具响应式排列 |
| [05-overview-dark.png](previews/05-overview-dark.png) | 深色完整概览 |
| [06-widget-detail.png](previews/06-widget-detail.png) | 原尺寸性能条截图 |
| [07-settings-detail.png](previews/07-settings-detail.png) | 完整设置窗截图 |

## 自行打开

双击index.html即可离线使用。需要HTTP预览时，在本包目录运行 `python -m http.server 8137 --bind 127.0.0.1`，打开 `http://127.0.0.1:8137/ui/index.html`；端口被占用时换一个端口。验证时的CLI临时脚本/日志不作为产品测试套件。

未验证：WPF/Win32、真实传感器/ACPI/NVIDIA、任务栏z-order、真正alpha/DPI、单实例/提权、系统自启、WPF内存。以上都有单独的规格与验收，不能由这些网页检查推出已通过。F13低内存限制在规格/提示词中加入，不是网页里增加一个假的内存实测指标。
