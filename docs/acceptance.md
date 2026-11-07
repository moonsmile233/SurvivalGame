# 验收记录（living document）

> 最后更新：2026-11-07（v1.3 内容扩展：1.3.0 构建、三种敌人、分段计分、全量回归，见 §3.8；用户确认人工 60 秒/5 局已完成，具体数据待补）
> 2026-11-02（v1.2 D 收尾：1.2.1 证据与 Netlify 部署，见 §3.7；v1.2 第一阶段：Unity 菜单/状态机 + 协议 v3 骨架，见 §3.5；
> 2026-11-01：L1.1 全屏口径见 §3.4；M6 第 2 步三档难度 + H5 主界面 + `ReturnToMenu` 见 §3.3；
> 此前 M5-3：无水印 1.0.0 构建 `2026-10-31T14:53:47Z`、M4.7 摇杆/响应式/全屏验收、无头 E2E、build 入库 `f19fcb79`、
> Release `v1.0.0`、干净克隆通过；人工/真机仍待测）
> 规则：只写实际跑过的结果；没跑就写「待测」。证据放 `docs/evidence/`。
> 测试用例与门槛见 `docs/测试计划.md`。

## 1. 原始需求 17 条

| # | 需求 | 状态 | 日期 | 设备/浏览器 | 证据 |
|---|---|---|---|---|---|
| 1 | 2D 场景（玩家/敌人/背景/UI） | ✅ | 2026-10-30 | Editor / Chromium | `m2-browser-playing-1280.png`、`m3-hud-enemies.png` |
| 2 | 玩家移动（WASD/方向键，不出界） | ✅ Editor + 浏览器合成按键 | 2026-10-30 | Editor / Chromium | `m3-move-before/after.png` |
| 3 | 敌人定时生成并追踪 | ✅ | 2026-10-30 | Editor / Chromium | `m3-hud-enemies.png` |
| 4 | HP、受击掉血、无敌帧 | ✅ | 2026-10-30 | Editor | `docs/M1-执行记录.md` |
| 5 | 生存 60 秒胜利 | ✅ 已实现：自动化 60.0s；**用户确认人工 60 秒已完成**（具体难度/胜负/设备待用户补） | 2026-11-07 | 用户实测（细节待补） | `docs/evidence/m6-content/c7/` |
| 6 | 失败 | ✅ | 2026-10-30 | 线上 Chromium | `m3-online-result.png` |
| 7 | 重新开始（全量重置） | ✅ | 2026-10-30 | Editor / Chromium | `m3-restarted.png` |
| 8 | 暂停 | ✅ 已验收：按钮 / Esc / P / 失焦；失焦保持暂停，主动恢复走 3-2-1 | 2026-11-01 | Chrome 桌面 1280×800 + 移动视口 390×844（无头） | `e2e-local-chrome-desktop-focus-return-paused.png`、`e2e-local-chrome-mobile-countdown.png`；全屏内 P：`resp-1280x720-fullscreen-paused.png` |
| 9 | 分数与剩余时间 | ✅ | 2026-10-30 | Editor / 线上 | `m3-online-result.png` |
| 10 | Unity WebGL 构建可在浏览器运行 | ✅ | 2026-10-30 | Chromium | 线上地址 |
| 11 | 昵称输入，H5 显示 | ✅ | 2026-10-30 | Chromium | `m3-online-result.png` |
| 12 | 开始按钮（ready 前禁用） | ✅ | 2026-10-30 | Chromium | `docs/M3-执行记录.md` |
| 13 | 嵌入游戏（非 iframe） | ✅ | 2026-10-30 | Chromium | `web/js/bridge.js` |
| 14 | Unity → H5 传分 | ✅ | 2026-10-30 | 线上 Chromium | `m3-online-result.png` |
| 15 | 最高分 localStorage，刷新仍在 | ✅ | 2026-10-30 | 线上 Chromium | `docs/M3-执行记录.md` |
| 16 | 页面适配电脑和手机宽度 | ✅ 已实现：布局 + 手机 HUD 放大 + 触控；320～1920 视口无溢出；**真机安全区：未测**（无设备，未到门禁） | 2026-11-01 | Chrome 视口 320×568 / 390×844 / 844×390 / 768×1024 / 1280×720 / 1920×1080 | `resp-320x568-playing.png`、`resp-1920x1080-playing.png`、`m47-joystick-mobile-offset.png` |
| 17 | 源码 + WebGL 构建文件 + README | ✅ 已验收：build 提交 `f19fcb79`；Release `v1.0.0` 附件 sha256 一致；干净克隆可跑 | 2026-11-01 | git clone main + Chrome | `docs/M5-执行记录.md`、<https://github.com/moonsmile233/SurvivalGame/releases/tag/v1.0.0> |

## 2. 浏览器 / 设备矩阵

| 平台 | 浏览器 | 状态 | 日期 | 备注 |
|---|---|---|---|---|
| Windows | Chrome 最新（线上 1.0.0） | ✅ | 2026-11-01 | Netlify deploy `6ae6d3d1…`，1.0.0 / `2026-10-31T14:53:47Z`；无头 smoke 1 局 PASS（加载→失焦保持暂停→主动恢复 3-2-1→结算→刷新），console 0 error |
| Windows | Chrome 最新（线上 M4.6） | ✅ | 2026-10-31 | 线上 M4.6（deploy `6ae5bd3b…`）：Esc/P、失焦保持暂停→主动恢复 3-2-1、3 局结算/刷新、结算按钮首屏可见，PASS |
| Windows | Edge 最新 | ✅ | 2026-10-31 | 线上 M4.6：加载、开始、Esc/P、失焦保持暂停→主动恢复、1 局结算、刷新，PASS |
| Windows | Firefox 最新 | ✅ | 2026-10-31 | 线上 M4.6 一局 PASS（Firefox 跳过失焦自动化；早期 wasm MIME 失败为瞬时问题） |
| macOS | Safari 最新 | ⏳ 待测 | — | 有设备则测 |
| iOS | Safari 15+ | ⏳ 待测 | — | 真机门禁（触控/HUD/最高分） |
| Android | Chrome 最新 | ⚠️ 加载受阻 | 2026-10-31 | Honor 90 Pro 打开线上 URL 基本加载不出来；初步判断为境外 CDN 带宽（直连实测 wasm ~31KB/s），待局域网服务确认设备侧；触控/HUD/最高分仍未测 |

## 3. M3 已验证项（摘要）

- **浏览器自然 60 秒胜利**（2026-10-30 补测）：真实 Chrome 有头运行，由**自动走位脚本**操作，真实耗时 60.1s，H5 显示「胜利 / 800 / 最高分 800 / 存活 60.0 秒 / 新纪录」，刷新后最高分与昵称仍在，console 0 error。
  这证明的是功能链路，**不等同人工体验**；人工 60 秒胜利仍待 M4 真机/人手门禁。
- **开始兜底 hotfix**（2026-10-30）：兜底 3s → 9s；超时不再把页面打回 Ready，只提示「启动较慢，若已在游戏中请直接玩；未开始请刷新」。
  已在部署后的线上 URL 复验：正常开始 10 秒内无「无响应」；模拟指令丢失时保持 Starting（按钮禁用）并显示上述提示（`m3-hotfix-playing.png`、`m3-hotfix-timeout.png`）。
- **发现并修复 H5 时序缺陷**：`unityInstance.SendMessage` 是同步的，`onStateChange(Playing)` 会在 `sendMessage()` 返回前到达；旧 `startGame` 先发指令再设 `Starting` + 3 秒兜底，把 Playing 覆盖回 Starting，3 秒后打回 Ready（游戏实际在跑，页面提示「开始指令无响应」）。已改为先设状态与计时器再发送，并复验失败→重开路径
- **Edge**：本地修复构建加载 4.3s、开始正常；线上部署修复后复测通过（加载 52s、开始正常、1 分钟后正常结算）。
  部署前线上旧构建可稳定复现上述缺陷（`m3-edge-online.png` 里能看到游戏在跑、页面却提示「开始指令无响应」）
- 昵称：空 →「玩家」；超长/emoji 按码点截 16；`<img src=x>` 纯文本
- localStorage 不可用 → 内存回退 + 提示；文件协议/加载失败 → 错误卡片 + 重试
- 可见性自动暂停；手动暂停不被自动恢复
- 连续重开 3 次；`onGameOver` 每局一次；平分不算新纪录
- 线上部署 + 刷新最高分

详见 `docs/M3-执行记录.md`。

## 3.1 M4 已验证项（摘要，2026-10-31）

- **线上 B1 构建 smoke（2026-10-31 16:20–16:23，deploy `6ae5a42c…`）**：Chrome 3 局 / Edge 1 局 / Firefox 1 局全部 PASS；
  覆盖加载 → 昵称 → 开始 → 真实窗口失焦 → 暂停 → 3-2-1 恢复 → 结算/重开 → 刷新最高分，console 0 error
  （旧语义：切回自动 3-2-1；M4.6 已改为切回保持暂停）；证据 `e2e-online-{chrome,edge,firefox}-desktop-*`。
- **键盘暂停 + 暂停恢复语义（M4 §1.7 / M4.6）**：Esc 暂停、P 主动恢复；恢复**先 3-2-1**（倒计时中重复 P 忽略），
  失焦切回**保持暂停**、不自动恢复；手机点「继续」也走 3-2-1。本地 Chrome 桌面 3 局 + 390×844 1 局、
  线上 M4.6 构建（deploy `6ae5bd3b…`）Chrome/Edge/Firefox 全 PASS；证据
  `e2e-local-chrome-{desktop,mobile}-{focus-return-paused,countdown,hotkey-countdown}.png`、
  `e2e-online-{chrome,edge,firefox}-desktop-*`。全屏下 P / Esc 行为随 M4.5 全屏一起验。
- **E2E smoke 可重复跑通**（`tools/e2e/smoke.mjs`）：
  - 线上 Chrome（2026-10-30，部署 `6ae4b4bf…`）：加载 → 昵称 → 开始 → **真实窗口失焦**（关掉 Playwright 焦点模拟 + 另开窗口）→ 已暂停 → 切回 3-2-1 → 恢复 → 3 局结算/重开 → 刷新最高分，全部 PASS，console 0 error。
  - 线上 Edge（同日）：同一流程 1 局，PASS。
  - 本地 Chrome（2026-10-31）：同一流程 3 局 + 失焦用例，PASS；证据见 `e2e-local-chrome-desktop-*.png`。
  - 本地 Firefox（Playwright 内置版）：加载、开始、1 局结算、刷新最高分，PASS（Firefox 没有焦点模拟开关，失焦用例跳过）。
  - 本地连续 **10 局**重开：每局都能重新进入 Playing、分数不累加、无回调丢失、console 0 error（`e2e-local-chrome-desktop-summary-10rounds.json`）。
  - 说明：`docs/evidence/` 里带 `local` 前缀的截图来自本地构建；线上那次的截图没有单独留档（之后被本地运行覆盖，已改为按目标命名），线上结果以执行记录中的运行输出为准。
- **失焦暂停 + 3-2-1 倒计时（M4.6 语义）**：失焦切回保持暂停、不自动恢复；**玩家主动恢复**（继续 / Esc / P）后
  暂停面板才显示大号 3→2→1（`unscaledDeltaTime`，倒计时期间隐藏「已暂停」标题避免叠加），结束才 Resume。
- **结算只有一套**：WebGL 嵌入模式下 Unity ResultPanel 不再显示，H5 卡片是唯一结算；Editor 仍显示 Unity 面板（`m4-editor-result.png`）。
- **手机 HUD**：390×844 竖屏 HUD 放大（参考分辨率 1280×720 → 640×360），时间/生命/分数可读不重叠；844×390 横屏无纵向滚动（`m4-landscape-844x390.png`）。
- **触控**：CDP 真实触摸事件拖动画布，页面 `scrollY` 保持 0；`touch-action: none`。
- **敌人 P0/P1（B1 拥挤修复）**：20 只同屏两两最小间距 ≈0.92（目标 0.95）；12 个站位槽、停距 ≈0.7；
  连续采样单只位移 0.02～0.09/0.3～0.5 秒，无高频抖动；把玩家放进 20 只中间 2 秒内 100→28 HP（接触伤害仍生效）；
  速度 ±12% 扰动 + 压力曲线 ×1.15；离屏 3 秒回收、列表无 null、后续继续生成（Editor 实测）。
- **性能（桌面）**：游戏内 6 秒 rAF 采样 165 FPS / p95 6.2ms（受 165Hz 屏限制，`m4-fps-desktop.json`）。
- **自动走位 60 秒胜利**：A / B0 / B1 构建本地都跑出 60.0s / 800 分（`m4-win-automated-local.png`，自动化；非确定性，也有 37～52 秒失败场次）。
- **大地图 / 相机（B0）**：地图 36×20；玩家贴边被钳在 (17.44, 9.44)、相机钳在 (7.18,5.00)（Editor 2.16 宽高比）；
  浏览器按住方向键 3 秒，玩家屏幕位置只偏移 26px（相机跟随），证明玩家移动 + 相机跟随。
- **出生 / 回收（B0）**：出生点以玩家为中心、1.15×相机半对角线且在视野外；进过视野的敌人离屏 3 秒被回收，
  从未进过视野的出生敌不会被误杀；10 局重开列表无残留。
- **触控语义**：锚点相对拖动，手指按住不动时玩家持续移动（相机滞后约 0.33 单位），松手归零（DPR2 390×844）。

## 3.2 M5 已验证项（2026-11-01）

- **无水印最终构建**：Unity `6000.3.22f1`，`2026-10-31T14:53:47Z`，1.0.0，约 9 MB；画面右下角无 `trial version`
  （`m5-build-no-watermark-playing-1280x720.png`、`m5-build-no-watermark-corner.png`）。
- **M4.7 浮动摇杆**：非中心触摸底座与手指点重合（±1px）；knob 只沿拖动方向、夹在环内；松手/暂停立即隐藏；
  桌面鼠标不显示；可视半径随 Canvas 短边缩放（390×844 实测 ≈27.5 CSS px，期望 28.8）。
  证据 `m47-joystick-{mobile,landscape,1280x720}{,-offset}*.png`、`m47-joystick-desktop.png`。
- **响应式 6 视口**：320×568 / 390×844 / 844×390 / 768×1024 / 1280×720 / 1920×1080 的 Playing / Paused / Countdown / Result
  均可读、无横向溢出、结算按钮可见（`resp-<vp>-*.png`）。已知小瑕疵：1280×720 非全屏页面高 730>720，约 10px 纵向滚动条（全屏无影响）。
- **H5 全屏（M4.5 §1）**：390×844 / 844×390 / 1280×720 真全屏 + 16:9 letterbox；页头/页脚/提示隐藏；
  全屏内 `P` 暂停；死亡自动退出全屏并显示结算卡；iPhone 失败降级提示；console 0 error。
  门禁：Ready/Result 隐藏「全屏」按钮，Playing/Paused 可见（`fullscreen-states.mjs` 5/5 PASS，
  `resp-1280x720-fullscreen-gate-{ready,result}.png`）。
- **E2E（无头默认）**：Chrome 桌面 1 局 + 移动 390×844 1 局 PASS，console 0 error；
  summary `e2e-local-chrome-desktop-summary.json`（2026-11-01T05:30:21Z）。无头为 SwiftShader 软件渲染，**不作帧率结论**。
- **交付物**：build 提交 `f19fcb79`（`web/unity/**` 入库）；Release `v1.0.0`（附件 9,433,571 字节，
  sha256 `dccc7bf6…`，公开下载校验一致）；干净克隆 main → 自带 `start-local.sh` 起服务 → 加载/开始/Playing，console 0 error。
  详见 `docs/M5-执行记录.md`。
- **线上 1.0.0 部署**：Netlify deploy `6ae6d3d110461e4c04a7a2cc`（2026-11-01），线上 `build-info` 已核对为
  `1.0.0 / 2026-10-31T14:53:47Z`；经代理跑无头 smoke 1 局 **PASS**（console 0 error，summary
  `e2e-online-chrome-desktop-summary.json`，finishedAt `2026-11-01T05:57:33Z`）。
  国内网络仍慢/不稳定，国内主链接仍待 CloudBase。

## 3.3 M6 第 2 步已验证项（2026-11-01，三档难度 + H5 主界面 + ReturnToMenu）

构建 `2026-11-01T12:44:35Z`（含协议 v2 / `ReturnToMenu`，尚未部署）；H5 为静态文件，无独立版本号。

- **难度端到端（`difficulty-e2e.mjs`，47 PASS / 0 FAIL）**：
  - A 困难站立不动 → 死亡；`damage=14`（12×1.15）、`onGameOver.difficulty=hard`、分数按 ×1.6 结算、结算卡显示「困难」、
    只写 `bestScore.hard`（`m6-difficulty-hard-lose.png`）。
  - B 困难 60 秒胜利 = **1160**（`breakdown.survival=960` + `winBonus=200` 不乘倍率）（`m6-difficulty-hard-win.png`）。
  - C v1 载荷（只有昵称）→ Unity 按 `normal` 开局；D 非法 `difficulty=bogus` → `normal` + bridge 警告。
  - E 开始 3-2-1；倒计时中点卡取消（不后台开局、不写分）；Playing 中点「简单」→ ReturnToMenu 回 Ready、本局不计分；
    再开简单局 → `difficulty=easy`、5 秒胜利 250、只写 `bestScore.easy`。
  - F 重新开始也有 3-2-1；倒计时中点「困难」→ Ready + 困难选中；返回菜单后暂停/恢复 3-2-1 正常；
    困难 5 秒胜利 280、`bestScore.hard` 更新、`easy` 保持（`m6-return-menu-hard-result.png`）。
  - G 开始倒计时中失焦 → 取消，不产生新结算。
- **H5 主界面/响应式（`menu-check.mjs`，137 PASS / 0 FAIL）**：6 视口（320×568 / 390×844 / 844×390 / 768×1024 /
  1280×720 / 1920×1080）Ready + Playing + Result；难度卡首屏可见且命中区 ≥44px（短横屏 ≥36）；无横向溢出；
  **画布在 Ready/Playing 同尺寸**且绘制缓冲 = CSS × DPR；`touch-action:none`；分档最高分旧键迁移/跨档不串分；
  （M6 验收时）Ready 全屏可开始；**L1 起 Ready 不再提供全屏**（见 §3.4）。
  证据 `m6-h5-ready-*.png`、`m6-h5-playing-*.png`、`m6-h5-layout.json`、`m6-h5-menu-check.log`。
  页面允许纵向滚动：**画布上滚轮由 H5 接管**（`onCanvasWheel`，单次 40px 恰好滚 40px、无双重），
  画布外走原生滚动；全屏内不滚（`fullscreen-states` 断言 scrollY=0）。
- **画布滚轮穿透（2026-11-01，策划确认）**：`onCanvasWheel` 在 document 捕获阶段接管画布 wheel，
  `preventDefault` 后 `scrollBy`（deltaMode 行/页换算）；全屏与 Ctrl+滚轮放行。
  320×568 / 844×390 / 1280×720 实测**单次 40px 恰好滚 40px（无双重）**，画布外原生滚动正常；
  全屏内 scrollY=0。证据 `m6-h5-menu-check.log`、`m6-fullscreen-states.log`。
- **回归（`smoke.mjs` 桌面 3 局 = 25 PASS / 0 FAIL；`fullscreen-states.mjs` 11 PASS / 0 FAIL）**：
  加载 → 昵称 → 开始 3-2-1 → 暂停/Esc/P/失焦保持暂停 → 恢复 3-2-1 → 结算 → 重开 ×2 → 刷新昵称/最高分；
  （M6 验收时）Ready 全屏可开始、Playing/Paused 可全屏、Result 隐藏全屏；L1 后口径见 §3.4
  （`m6-step2-h5-smoke.log`、`m6-fullscreen-states.log`）。
- **移动视口 smoke（390×844、`MOBILE=1`，19 PASS / 0 FAIL）**：CDP 真实触摸拖动可移动玩家、拖动时页面不滚动
  （`scrollY=0`）、暂停/恢复倒计时、结算、刷新（`m6-step2-h5-smoke-mobile.log`）。
- 17 条需求：1–16 的自动化回归仍全绿（见 3.1/3.2 证据 + 本次 smoke/menu-check）；第 17 条交付物仍是 1.0.0，
  M6 发版（1.1.0）未做。

## 3.4 L1/L1.1 全屏口径（2026-11-01，用户明确要求）

**决策**：全屏按平台原生习惯——**游戏中（Playing/Paused）全屏不保留任何网页端按钮/外壳**；
**主界面（Ready/Starting）也能进全屏，且全屏里保留难度区 + 最高分行 + 开始按钮**（否则进全屏没法开局）。
删除网页「退出全屏」按钮，退出只靠 `Esc`（桌面）/ 浏览器手势 / 返回键（手机）。

> L1.1 修正：L1 初版把「全屏无网页按钮」过度应用到了 Ready，导致主界面看不到全屏入口；
> 用户反馈后恢复 Ready/Starting 全屏菜单（M4.5 行为），「无网页控件」仅约束游戏中的 Playing/Paused。

- 删除 `#btn-exit-fullscreen`（HTML/CSS/JS）；`fullscreenchange` 负责恢复页面。
- `#btn-fullscreen` 在 Ready/Starting/Playing/Paused 显示，Result 不显示。
- **Ready/Starting 全屏**：保留难度区、最高分行、开始按钮（可点击）、H5 3-2-1 覆盖层；隐藏页头/页脚/操作提示。
- **Playing/Paused 全屏**：header、难度区、controls（开始/全屏/状态提示/全屏失败提示）、footer、op-hint、
  rotate-hint、结算卡全部隐藏；画布内 Unity 暂停按钮/面板保留（不算网页控件）。
- **证据（`fullscreen-states.mjs`，1280×800 无头 Chrome）**：
  - Ready 显示全屏按钮；全屏 Ready 下难度区/最高分行/开始按钮可见且可用，页头隐藏；
  - 全屏 Ready 点开始 → Starting 的 3-2-1 覆盖层可见 → 进入 Playing；
  - Playing 全屏内**网页端可见按钮 = 0**、外壳全隐藏；Paused 全屏内仍无按钮/外壳，P 暂停/恢复正常；
  - `Esc` 或 `document.exitFullscreen()` 退出后，全屏按钮/难度区/页头恢复可见；
  - Result 不提供全屏、结算卡可见。
  截图 `resp-1280x800-fullscreen-gate-{ready-menu,ready,playing,paused,exited}.png`、日志 `m6-fullscreen-states.log`。
- **局域网全新浏览器上下文复核（2026-11-01，`http://192.168.1.100:8000/`，Playwright 新 context 无缓存）**：
  同样 20 项 PASS / 0 FAIL；Ready 显示「开始游戏 + 全屏」，全屏 Ready 显示难度/最高分/开始；Playing 全屏网页按钮 = 0。
  服务端已换成 `web/serve.py`（8000 端口实测 `Cache-Control: no-store`）。
  证据 `m6-fullscreen-states-lan.log`、`resp-lan-1280x800-fullscreen-gate-{ready-menu,ready,playing}.png`。
- **缓存修正（同次）**：`index.html` 的 H5 资源 `?v=` 由 `1.1.0` 升到 `1.1.1`；新增 `web/serve.py`
  （`start-local` 默认使用，`Cache-Control: no-store` + 正确 MIME）。旧 `python -m http.server` 不发缓存头，
  改完必须 `Ctrl+F5`；规则写入 `docs/协作协议.md §6`。
- **回归**：`menu-check` 131 PASS（6 视口 Ready 断言「显示全屏按钮」）；`smoke` 1 局。
- **已知边界**：无头 Chrome 的 `Esc` 不退出全屏却会传给 Unity（Esc 也是暂停热键），测试已按该分支恢复；
  真实浏览器通常由浏览器消费 `Esc`，有头/真机行为仍待确认。

## 3.5 v1.2 第一阶段（Unity 菜单/状态机 + 协议 v3 骨架，2026-11-02）

> 构建：`productVersion 1.2.0`，`builtAt 2026-11-02T05:12:58Z`，约 9.3MB；产物不入库（本地/临时目录）。
> 详细记录：`docs/M6-重构-执行记录.md`；协议：`docs/bridge-protocol.md` v3。

- **Standalone 直开（1280×720，无 H5）**：菜单 → 困难卡（`onDifficultyChanged=hard`）→ 3-2-1 → Playing →
  Esc 暂停 → 继续（3-2-1）→ 暂停重开（3-2-1）→ 2s 短局结果（`onGameOver.difficulty=hard / score=232`）→
  再来一局 → 暂停返回菜单 → 再开一局 → 结果「返回菜单」全链路 PASS。事件 16 条，序列见
  `docs/evidence/m6-refactor/standalone-flow-events.json`；截图 `standalone-flow-v1.2-*.png`。
- **390×844（16:9 画布，640×360 参考分支）**：同一点击链 PASS；`standalone-mobile-*.png`。
- **6 视口（16:9 画布约束）**：320×568 / 390×844 / 844×390 / 768×1024 / 1280×720 / 1920×1080 的
  menu / countdown / paused / result 各一张，无 tofu、无横向溢出；`standalone-viewport-*.png`。
- **失焦**：原生 blur 与切标签都自动 Paused，切回保持 Paused；Starting 中失焦分别回 Menu / Paused，
  全程不出现 Playing。证据 `standalone-focus-native.json`、`standalone-focus-starting-cancel.json`。
- **旧 H5 过渡**：协议 3 warning；点旧开始 → Unity 菜单；可开局；`onGameOver` 后旧页最高分更新为 226、
  页面进入 Result（双结算卡属已知过渡）。证据 `h5-transition.json`、`h5-transition-result.png`。
- **40 只性能（headed Chrome，离屏）**：165 FPS / p95 7ms / alloc 41.4KB/s / capAt 49.46s；
  FPS/p95 与基线一致，alloc 略高于 M6 记录的 26~35KB/s（记录，不宣称零回归）。`perf-40.json`。
- **字体**：菜单/暂停/结算无 tofu；`×` 正常；TMP 图集 75 字符（1024×1024）。

## 3.6 v1.2 清理后重建（2026-11-02）

> 构建 `productVersion 1.2.0`，`builtAt 2026-11-02T11:02:31Z`（本地 19:02），耗时 3:42，
> 总大小 9,650,984 B（wasm 7,002,319 / data 2,435,213 / framework 75,669 / loader 117,894）。
> 产物副本在仓库外 `D:/unityFile/_builds/SurvivalGame-1.2.0-clean-2026-11-02/`；测试后 `web/unity` 已恢复 1.0.0。

- **字体**：按 `UiStrings.AllCharacters` 重生成 **71 码位（含 1 个空格；可见 70）**子集（18KB），
  构建重烤 71 码位图集；菜单/结算截图逐字检查无 tofu（`standalone-flow-v1.2-menu.png`、`-result.png`，
  19:19 clean 构建）。§3.5 的 75 字符是清理前构建。
- **C# 清理**：死代码、`ReturnToMenu` 不再 `BeginRound`、D1 旧页 `difficulty` 别名、D5 autostart 不改难度偏好、
  昵称按码点截断；编译 0 error。
- **回归**：`standalone-menu` flow（16 事件 / hard 232）、`standalone-focus`（原生 blur/visibility + Starting 取消）、
  `h5-shell` core+viewports、`smoke` 9 项，全 PASS。
- **40 只**：交付页 165 FPS / p95 7ms / alloc 29.4KB/s / capAt 49.43s；standalone 30.2KB/s
  （`docs/evidence/perf-local-chrome-desktop-cap40.json`、`m6-refactor/perf-40.json`）。
- **D1/D5**：Editor Play 旧载荷 `{"nickname","difficulty":"hard"}` → `MenuDifficulty Normal→Hard`；
  perf 40 记录 `difficultyAfterStart=hard`。
- **资源**：`NotoSansSC-UI-Subset-SDF.asset`（2.1MB，无引用）已按用户确认删除（C-26）；
  `SampleScene.unity` **保留**（模板默认场景）。`web/unity` 入库/部署/Release 等用户放行。

## 3.7 v1.2 D 收尾（2026-11-02：UI 调整 + 1.2.1 部署）

> 构建 `productVersion 1.2.1`，`builtAt 2026-11-02T13:05:32Z`（本地 21:05；H5 提示文案补丁后重建）；
> 已入库 `web/unity`，`node tools/check-version.mjs` PASS。线上 Netlify 已部署 1.2.1
> （deploy `6ae88c99dbecd388da02630a`）；1.2.0 的短暂部署已被覆盖。最终提交见 `jj log`。

- **H5 布局（D 调整）**：桌面与手机统一为**画布在上、昵称/成绩卡在下**；删除“网页只负责…”等职责说明，
  页脚只保留小号版本号；Shell 遮罩仍挡画布，不自动进游戏；全屏入口只在 Entered 后出现。
- **Shell 提示（1.2.1 补丁）**：遮罩文案改为「游戏已就绪 · 点击下方「开始游戏」进入」，
  避免玩家误以为已经进游戏。
- **暂停菜单（D 调整）**：继续 / 重新开始 / 返回菜单改为**竖排居中**；结算面板仍横排。
- **H5 薄壳**（`h5-shell` core + viewports，21:06–21:07）：协议 3 / 昵称 / 进入 / 难度偏好 / 结算 / 最高分 / 迁移 / 全屏 PASS；
  6 视口 320×568 / 390×844 / 844×390 / 768×1024 / 1280×720 / 1920×1080 全 ok（新布局截图已覆盖）。
  - 移动视口（width < 900）加 `hasTouch: true`，操作提示显示「拖动屏幕移动 · 点画面内暂停」的手机文案；
    1280/1920 仍是键盘文案。
  - **390×844 跑真实 2s 短局**：进入 → 画布菜单开局 → Unity 结算卡（胜利/226）+ H5 数据栏，
    证据 `h5-shell-390x844-gameover-real.png`；其余视口的 GameOver 是注入态，文件名带
    `-gameover-layout-only.png`，不代表真实结算（1280 真实结算见 `h5-shell-gameover-1280.png`）。
  - 桌面 1280×720 下方卡片底边 702px（视口 720），一屏可见，无需滚动。
- **薄壳主流程**（`smoke`，21:07，9/9 PASS）：Shell 遮罩 / 只发一次 EnterGame / 画布菜单开局 / Esc 暂停→继续 /
  短局结算「普通 265」/ 再来一局 / 暂停返回菜单 / 刷新保留最高分与昵称、最近一局清空；console 0 未预期 error。
- **交付页 40 只**（`perf.mjs`，21:07–21:09）：capAt 49.45s，稳态 **165 FPS / p95 7ms / alloc 29.4KB/s**；
  证据 `docs/evidence/perf-local-chrome-desktop-cap40.json`。
- **Standalone**（`web/unity/index.html`，21:09–21:12）：
  - flow 1280×720：16 事件，hard 短局 232（`standalone-flow-events.json`、`standalone-flow-v1.2-*.png`）；
  - mobile 390×844：走 640×360 参考分支（scale 0.609375），16 事件，hard 232；
  - viewports：6 视口 menu / countdown / **paused（竖排）** / result 截图重拍；移动视口带触摸文案；
  - focus：原生 blur 与 visibilitychange 都保持 Paused；Starting 中失焦回 Menu，Paused 继续倒计时中失焦回 Paused；
  - perf 40：**165 FPS / p95 7ms / alloc 30.0KB/s / capAt 49.49s**（`perf-40.json`）。
- **版本一致性**：`web/index.html` 5 处 `?v=1.2.1` == `web/unity/build-info.json.productVersion`；`check-version` PASS。
- **最终产物**（总 **9,651,481 B**）：wasm 7,002,319 / data 2,435,710 / framework 75,669 / loader 117,894。
- **线上核对（Netlify deploy `6ae88c99…`）**：
  - `build-info.json` = **1.2.1** / `2026-11-02T13:05:32Z`；`?v=1.2.1`（5 处）。
  - **完整 smoke（headed 真 GPU，无痕上下文）9/9 PASS**：加载 → 昵称 → 进入 → 菜单开局 → Esc 暂停/继续 →
    短局结算「普通 265」→ 再来一局 → 返回菜单 → 刷新保留；console **0 未预期 error**
    （`smoke-online-chrome-desktop-summary.json`）。
  - 单次加载探针：桌面 34s、手机 390×844 86s 到 ready 并进 Entered；手机 coarse pointer、手机操作文案、
    `v1.2.1`；0 console error；手机证据 `online-1.2.1-mobile-390x844-*.png`。
  - 说明：无头 SwiftShader 在代理下多次遇到 wasm `ERR_CONNECTION_CLOSED`，headed 真 GPU 通过；
    wasm 代理校验 HTTP 200 / 7,002,319 B / sha256 与本地一致。
  - 回滚：Netlify 上一部署 `6ae88855…`（1.2.0）或 Release v1.0.0。
- **仍未做**：GitHub Release（保留 v1.0.0 作回滚）、1.2.1 干净克隆复验、国内托管；真机 / DPR / 全屏语义见 §4。

## 3.8 v1.3 内容扩展（M6 C1–C7，2026-11-07）

> 构建 `productVersion 1.3.0`，`builtAt 2026-11-07T13:46:43Z`，总 **9,670,524 B**
> （wasm 6,991,330 / data 2,465,651 / framework 75,760 / loader 117,894）；
> 已入库 `web/unity`，`?v=1.3.0` 5 处，`node tools/check-version.mjs` PASS；**无 trial 水印**。
> **未部署 Netlify、未发 Release**；线上仍是 1.2.1。证据 `docs/evidence/m6-content/c7/`，
> 执行记录 `docs/M6-内容扩展-执行记录.md`。

- **玩法**：三种敌人（Chaser 含约一半 Interceptor / Charger / Flanker）；5-tick 袋 2 普通 + 2 绕行 + 1 冲锋；
  Flanker 三档 0s 解锁、Charger 25/15/10s；同屏上限 14/30/50；回收 1.5s、到顶回收补位；
  35% 前向出生锥 + 拦截者提前点（0.25s 更新、3.0 开阔地、Arrive 0.5~0.8 绕点）。
- **计分**：分段存活分 10/12/15/20 每秒（满 60s=855）+ 剩余 HP×1，乘难度倍率后 + 胜利 200；
  HUD 只显示存活分×倍率（整数、约 0.1s 一次）；结算显示最终分 +「存活分·血量分·受击」；协议仍 v3（增量字段）。
- **自检**：`ScoreSelfCheck` PASS（分段 0/150/330/555/855；三档 × 2/5/15/30/60 秒参考分；粒度/Reset/血量钳制）。
- **E2E**：`score.mjs` 7 局全过（easy60 1155、normal 2/15/30/45/60 = 356/525/759/1052/1442、hard60 1728）；
  `standalone-menu` flow/mobile（hard 2s 392）、`standalone-focus`（失焦保持暂停 + Starting 取消）、
  `h5-shell` core/migration/fullscreen（392）、`smoke` 9 项（普通 395）、standalone + h5-shell 6 视口全 PASS。
- **30/40/50 混合**（`enemyMix=1&walk=1`，90s）：到顶 55.9 / 82.9 / 85.8s，稳态 29.8 / 38.6 / 44.3；
  实测 163.4 / 131.6 / 130.9 FPS、p95 7 / 8 / 9ms、alloc 41.4 / 33.8 / 34.1 KB/s。
  本机当前 rAF 上限约 128 FPS（空白页实测），同构建未限帧复测 40 只 **2154 FPS / p95 1ms**，
  说明 FPS/ p95 受显示/合成器限制，不是 CPU 掉帧；非 Dash minPair p50 0.758（C7.5 全关对照 0.727，
  两者低瞬态同量级，C7.5 未引入新退化）。
- **用户验收**：C7.5c 拦截者/围堵经用户试玩**验收通过**；用户确认**人工 60 秒与 5 局基线已完成**
  （具体难度/胜负/设备待用户补，本文不代填）。
- **17 条**：1–16 自动化回归全绿；第 17 条 1.3.0 构建已入库（未部署、未发 Release）。

## 4. 未关闭项

1. **人工 60 秒胜利**：用户 2026-11-07 确认已完成；具体难度/胜负/设备待用户补，补齐前不写死数据。
2. **人工 5 局平衡基线**：用户 2026-11-07 确认已完成；具体每档胜率/受击秒数待用户补。
3. **真机未测**（Honor 90 Pro / iPhone Safari）：触控跟手、HUD 可读、安全区、横竖屏、帧率均未测；
   目前只有浏览器视口证据。Honor 90 Pro 线上加载受阻（2026-10-31），先局域网确认设备侧，见 `docs/遗留问题.md` 第 6 节。
4. **国内托管未做**：Netlify 已更新为 1.0.0（deploy `6ae6d3d1…`，无头 smoke 1 局 PASS），
   但国内 4G 直连仍慢/不稳定；README 的国内链接只能写「待定」，不能宣称「手机 4G 可玩」。
5. **正式美术 / 音效**：仍是占位圆点 + 网格。
6. **加载性能**：约 9 MB/次；Netlify 国内实测很慢（首次 50～61s），国内托管方案见 `docs/部署与托管.md`。
7. **M4.5 §2/§3（历史项）**：v1.2 已实现暂停菜单「重新开始 / 返回菜单」与 Unity 主菜单（难度 + 开始）；
   H5 薄壳改版（昵称 / 进入 / 数据面板 / 全屏入口）已在 v1.2 落地（§3.7）。全屏 §1 已完成。
8. **M6 第 2 步遗留**：真机未测（Honor 90 Pro 三档各 1 局 + 40 只 `?perf=1`）；
   v1.2 的难度在 Unity 主菜单选，局内换档需暂停 →「返回菜单」，H5 不再有难度卡；
   游戏中（Playing/Paused）全屏不显示网页控件（L1.1）。这些不影响功能链路，但人工验收时要按此口径检查。
9. **L1 全屏边界**：无头 Chrome 的 `Esc` 不退出全屏却会传给 Unity 触发暂停；有头浏览器/真机尚未确认
   `Esc` 是否由浏览器消费。若真机上出现「按 Esc 退出全屏后游戏被暂停」，记录后另行决定是否在 H5 捕获阶段拦截。
10. **v1.2 交付未完成**：1.2.1 已入 `web/unity` 并部署 Netlify（deploy `6ae88c99…`），但
    **未发 GitHub Release、未做 1.2.1 干净克隆复验**；`main` 未前移。真机/DPR/全屏语义仍待用户设备验证
    （见 §2、§4.3、`docs/遗留问题.md`）。

## 5. 结论

- 17 条中 1–17 均已实现/验收；第 5 条人工 60 秒与 5 局由用户确认完成（具体数据待补）；第 16 条视口级已验收、**真机未测**。
- **仓库已是 v1.3.0**（`web/unity` 入库 `2026-11-07T13:46:43Z`）；**线上仍是 v1.2.1**（Netlify deploy `6ae88c99…`），
  v1.3.0 未部署；`main` 未前移，1.3.0 只在 `wip`。
- **Release 仍是 v1.0.0**：保留作回滚/离线包；1.3.0 未发 Release、未做干净克隆复验。
- **国内托管未做**，国内在线链接只能标「待定」；Netlify 国内访问慢/不稳定，仅作海外备份。
- **不能宣称真机收口**：Honor 90 Pro / iPhone 真机、DPR>1、全屏真机语义仍未做。
- 用户补完人工 60 秒/5 局的具体难度、胜负、设备后，把数据补进本文件 §3.8 与 §4。
