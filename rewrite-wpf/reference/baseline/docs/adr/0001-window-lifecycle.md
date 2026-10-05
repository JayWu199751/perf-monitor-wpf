# 窗口生命周期：常驻托盘、设置窗关闭即隐藏、退出时经 isQuitting 放行 close

> 状态：Accepted。"关闭即隐藏 + 退出经放行标志"这条主干实现在 `electron/src/main/index.ts`：`before-quit` 置位 `isQuitting`，设置窗的 close 处理器据此放行。
>
> 2026-09 补充：隐藏改为**带闲置销毁**——关闭仍先隐藏并复用实例，但隐藏满 5 分钟后销毁窗口，下次打开重建。理由是实测设置窗渲染进程常驻 ~37MB 私有内存，而"打开设置改一项就收工"之后它基本再不会被碰；纯隐藏等于为一次性的操作永久付这份内存。5 分钟足够覆盖"改完又回来补一眼"的连续操作。策略在 `electron/src/main/settingsWindow.ts`，由 `electron/tests/settingsWindow.test.ts` 覆盖。
> 2026-09-30 补充：设置窗高度改成**按内容实测**——整页内容是死的（四张分组卡片 + 头 + 一行状态，实测 850 CSS px），所以取 `SETTINGS_CONTENT_HEIGHT = 880`；工作区不够高时退到「工作区高 − 48」。
> 旧值 680 是照「~660 的内容」写的，之后的字号/行高调整把它甩下了近 170px，表现是常驻滚动条 + 「行为」整组永远在首屏外，而窗口 `resizable: false`，用户只能滚。策略是 `settingsWindow.ts` 的 `settingsWindowHeight()`（纯函数，可单测），工厂只负责把它接到真窗口上。


设置窗关闭一律隐藏（关闭即隐藏，复用实例），但这会在 `app.quit()` 时中止退出——widget 窗已关而进程存活、托盘图标残留（半退出）。决定：`before-quit` 置位 `isQuitting`，设置窗 close 处理器据此放行，让退出（含系统关机/注销）干净走完。

## Considered Options

- **先 destroy 设置窗再 quit**：只修托盘一条路径，不覆盖系统关机；退出逻辑需伸手进窗口内部。放弃。
- **托盘退出改用 `app.exit(0)`**：强杀最彻底，但跳过 before-quit/will-quit，`metrics.stop()` 要手动挪，将来加优雅退出钩子无挂点。放弃。
- **关闭即销毁（不留复用）**：省内存最直接，但每次打开都要重走创建 + 加载 + `ready-to-show`，改一个开关要点两次的连续操作会明显发涩。折中成"隐藏 + 闲置 5 分钟后销毁"。
