# 验收记录（living document）

> 最后更新：2026-11-01（M5-3：无水印 1.0.0 构建 `2026-10-31T14:53:47Z`、M4.7 摇杆/响应式/全屏验收、无头 E2E、build 入库 `f19fcb79`、Release `v1.0.0`、干净克隆通过；人工/真机仍待测）
> 规则：只写实际跑过的结果；没跑就写「待测」。证据放 `docs/evidence/`。
> 测试用例与门槛见 `docs/测试计划.md`。

## 1. 原始需求 17 条

| # | 需求 | 状态 | 日期 | 设备/浏览器 | 证据 |
|---|---|---|---|---|---|
| 1 | 2D 场景（玩家/敌人/背景/UI） | ✅ | 2026-10-30 | Editor / Chromium | `m2-browser-playing-1280.png`、`m3-hud-enemies.png` |
| 2 | 玩家移动（WASD/方向键，不出界） | ✅ Editor + 浏览器合成按键 | 2026-10-30 | Editor / Chromium | `m3-move-before/after.png` |
| 3 | 敌人定时生成并追踪 | ✅ | 2026-10-30 | Editor / Chromium | `m3-hud-enemies.png` |
| 4 | HP、受击掉血、无敌帧 | ✅ | 2026-10-30 | Editor | `docs/M1-执行记录.md` |
| 5 | 生存 60 秒胜利 | ✅ 已实现：自动化 60.0s / 800 分；**人工 60 秒胜利：未测**（未到人工门禁） | 2026-10-30 / 2026-10-31 | 真实 Chrome（有头）+ 脚本 | `m3-win-result.png`、`m4-win-automated-local.png`；人工版未测 |
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
| Windows | Chrome 最新 | ✅ | 2026-10-31 | 线上 M4.6（deploy `6ae5bd3b…`）：Esc/P、失焦保持暂停→主动恢复 3-2-1、3 局结算/刷新、结算按钮首屏可见，PASS |
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

## 4. 未关闭项

1. **人工 60 秒胜利未测**：自动化脚本（M3 60.1s、M4 60.0s）只证明功能链路，**不算人工验收**；需在最终构建上人工跑 1 次。
2. **人工 5 局平衡基线未测**：用户此前决定延后到难度分级之后；未做正式统计，不能宣称难度达标。
3. **真机未测**（Honor 90 Pro / iPhone Safari）：触控跟手、HUD 可读、安全区、横竖屏、帧率均未测；
   目前只有浏览器视口证据。Honor 90 Pro 线上加载受阻（2026-10-31），先局域网确认设备侧，见 `docs/遗留问题.md` 第 6 节。
4. **最终构建未上线**：线上 Netlify 仍是 M4.6 构建（`2026-10-31T10:02:10Z`，deploy `6ae5bd3b…`）；
   1.0.0 构建已入库并发布 Release，但**国内托管未做、Netlify 未更新**，不能宣称「线上可玩最终版」。
5. **正式美术 / 音效**：仍是占位圆点 + 网格。
6. **加载性能**：约 9 MB/次；Netlify 国内实测很慢（首次 50～61s），国内托管方案见 `docs/部署与托管.md`。
7. **M4.5 §2/§3**：暂停菜单「重新开始」、Unity 主菜单 + 难度未做（不阻塞交付）。全屏 §1 已完成。

## 5. 结论

- 17 条中 1–4、6–15、17 已验收；第 5 条功能已实现但**人工 60 秒未测**；第 16 条视口级已验收、**真机未测**。
- 最终构建 **1.0.0**（`2026-10-31T14:53:47Z`，无水印）已入 `main`（build `f19fcb79`）并发布
  Release `v1.0.0`；干净克隆可运行。
- 线上 Netlify 仍是 M4.6 构建；**国内托管未做**，在线链接只能标「待定」。
- **不能宣称 M4/M5 收口**：人工 60 秒、5 局基线、Honor 90 Pro / iPhone 真机仍未做。
- M5-5 人工门禁在最终构建上执行后，把日期/设备/结果补进本文件。
