# 16: 应用图标重绘（描边加粗）

**What to build:** 应用图标与托盘图标统一为描边圆角方框母题，线条整体加粗（×1.25），占位不变；图标资产改为可由脚本复现生成，并清掉文档中「三柱」的错误描述。

**Blocked by:** 11: 共享菜单、托盘主题与透明显示（图标资产与档位约定已落地）

**Status:** resolved

- [x] 新增可复现的生成脚本 `tools/gen-icon.py`：母图 `tools/assets/icon-master.png`（256px 描边方框，黑色透明底）经圆盘膨胀加粗后，外接框缩放回原占位，输出 16/20/24/28/32 明暗托盘 PNG 与 16/24/32/48/256 五尺寸 ICO。
- [x] 母图与产物分离，脚本幂等：重复运行得到同一结果，不会逐次累加加粗。
- [x] 加粗幅度 ×1.25（框描边 20px → 25px @256），外接框 235×215 保持不变，图标占位与旧版一致。
- [x] 全部档位同步重绘：tray-light/tray-dark 的 16/20/24/28/32 与 `icon.ico` 五尺寸；明暗仅换单色（黑/白），几何完全一致。
- [x] 清除三柱图形：删除 `rewrite-wpf/ui/index.html` 中唯一的三柱内联 favicon，换成描边方框 SVG（避免 favicon 404 回归）。
- [x] 文档去三柱：`rewrite-wpf/spec.md`、`.scratch/wpf-rewrite/spec.md`、`rewrite-wpf/prompts/01-master.md`、`rewrite-wpf/ui/design.md`、`rewrite-wpf/ui/index.html` 改称「描边方框」；工票 11 按 append-only 约定追加勘误而非改写正文。
- [x] 真机人工复验：托盘（16px 各 DPI 档）、exe 资源管理器图标、安装器与开始菜单快捷方式图标均为新图形且清晰。

## Comments

### 2026-10-06（实现者 impl-16-icon）

**实现摘要**

- 新增 `tools/gen-icon.py`（依赖 Pillow）。流程：母图 alpha 二值化 → ×4 超采样到 1024 → 圆盘结构元膨胀（半径 = 描边宽 ×(倍数-1)/2）→ 裁到膨胀后的外接框并拉回原尺寸 → 放回 1024 画布 → LANCZOS 降采样到各档位。超采样保证小尺寸边缘仍有抗锯齿；外接框回缩保证「线条更粗、占位不变」。
- 产物：`src/PerfMonitor.App/Resources/icon.ico`（16/24/32/48/256）+ `tray-light-{16,20,24,28,32}.png`（黑）+ `tray-dark-{16,20,24,28,32}.png`（白）。明暗仅 RGB 不同、alpha 同源，因此两套几何严格一致。
- 未改 `src/PerfMonitor.Windows/Shell/TrayIconCatalog.cs` 的选档逻辑，也未改 csproj 的资源通配（`Resources\tray-light-*.png;Resources\tray-dark-*.png`），档位与文件名约定不变。
- 三柱清理：位图资产从来不是三柱（逐像素核对：全部为圆角方框描边 + 框内折线），三柱只存在于 `rewrite-wpf/ui/index.html` 的内联 favicon SVG 与若干文档措辞中。favicon 换成描边方框 SVG（`validation.md` 记录的 favicon 404 不会回归）。
- 未动 `rewrite-wpf/reference/**`：该目录是冻结基线，`manifest.json` 用 sha256 锁住 110 个文件（含 `README.md`），改一个字就会让校验和整体失效；其 README 中残留的「三柱」措辞属冻结快照的历史文本，不在本次纠正范围。

**测试证据**

- 生成前后测量（脚本自报）：框描边 20px → 25px @256（×1.25），探测行 y=55 左右段各 20px → 各 25px；外接框 235×215 不变。
- 逐像素核对（`.scratch/icon_probe*.py`）：24/28/32px 框内折线与框之间仍保留可见间隙，未粘连；16px 图形更实但结构仍可分辨（旧 16px 反而更虚、边缘多为半透明像素）。
- `dotnet build PerfMonitor.sln`：0 错误 0 警告；`icon.ico` 实读含 16/24/32/48/256 五尺寸。
- `dotnet test PerfMonitor.sln`：186 项全过（与改前一致，图标不涉逻辑）。

**未自动验证项（人工复验步骤）**

1. 托盘：启动 Release，在 100%/125%/150%/175%/200% 缩放下看托盘图标是否为新的粗描边方框，且与主题联动黑/白。
2. exe 图标：资源管理器中查看 `PerfMonitor.App.exe` 的图标（16/32/48/256 各档）已更新，非旧图缓存（必要时重建图标缓存）。
3. 安装器：`installer/perfmonitor.iss` 取 `publish/fdd/Resources/icon.ico`，重新发布后确认安装向导与开始菜单快捷方式图标为新图形。


### 2026-10-10：用户人工验收通过

- 用户明确确认‘除了内存我已全部验证通过’。本事项非内存验收已通过，状态更新为 `resolved`。
- 证据为用户整体人工确认，详见 [验收记录](../acceptance-2026-10-10.md)；未补写逐项操作过程或测量数据。正文及旧 Comments 中‘待人工验收/未验证’等表述保留为历史情况，以本次确认更新当前结论。
