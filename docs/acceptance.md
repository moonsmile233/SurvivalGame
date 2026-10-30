# 验收记录（living document）

> 最后更新：2026-10-31（M4：触控/HUD/结算去重/失焦倒计时/大地图与相机/敌人手感；线上 A 构建通过，B0 本地通过、待部署）
> 规则：只写实际跑过的结果；没跑就写「待测」。证据放 `docs/evidence/`。
> 测试用例与门槛见 `docs/测试计划.md`。

## 1. 原始需求 17 条

| # | 需求 | 状态 | 日期 | 设备/浏览器 | 证据 |
|---|---|---|---|---|---|
| 1 | 2D 场景（玩家/敌人/背景/UI） | ✅ | 2026-10-30 | Editor / Chromium | `m2-playing.png`、`m3-hud-enemies.png` |
| 2 | 玩家移动（WASD/方向键，不出界） | ✅ Editor + 浏览器合成按键 | 2026-10-30 | Editor / Chromium | `m3-move-before/after.png` |
| 3 | 敌人定时生成并追踪 | ✅ | 2026-10-30 | Editor / Chromium | `m2-playing-2.png` |
| 4 | HP、受击掉血、无敌帧 | ✅ | 2026-10-30 | Editor | `docs/M1-执行记录.md` |
| 5 | 生存 60 秒胜利 | ✅ 60.1s（M3）、60.0s/800 分（M4 构建，方形走位脚本）；**均为自动化，非人工** | 2026-10-30 / 2026-10-31 | 真实 Chrome（有头、GPU）+ 脚本 | `m3-win-result.png`、`m4-win-automated-local.png`；人工版待门禁 |
| 6 | 失败 | ✅ | 2026-10-30 | 线上 Chromium | `m3-online-result.png` |
| 7 | 重新开始（全量重置） | ✅ | 2026-10-30 | Editor / Chromium | `m3-restarted.png` |
| 8 | 暂停 | ✅ Unity HUD 暂停 + 失焦自动暂停 + 切回 3-2-1 倒计时恢复（真实窗口失焦，非模拟） | 2026-10-31 | Chrome / Edge（线上）、Firefox（本地） | `e2e-local-chrome-desktop-paused.png`、`e2e-local-chrome-desktop-countdown.png`、`e2e-local-chrome-desktop-resumed.png` |
| 9 | 分数与剩余时间 | ✅ | 2026-10-30 | Editor / 线上 | `m3-online-result.png` |
| 10 | Unity WebGL 构建可在浏览器运行 | ✅ | 2026-10-30 | Chromium | 线上地址 |
| 11 | 昵称输入，H5 显示 | ✅ | 2026-10-30 | Chromium | `m3-online-result.png` |
| 12 | 开始按钮（ready 前禁用） | ✅ | 2026-10-30 | Chromium | `docs/M3-执行记录.md` |
| 13 | 嵌入游戏（非 iframe） | ✅ | 2026-10-30 | Chromium | `web/js/bridge.js` |
| 14 | Unity → H5 传分 | ✅ | 2026-10-30 | 线上 Chromium | `m3-online-result.png` |
| 15 | 最高分 localStorage，刷新仍在 | ✅ | 2026-10-30 | 线上 Chromium | `docs/M3-执行记录.md` |
| 16 | 页面适配电脑和手机宽度 | ✅ 布局 + 手机 HUD 放大 + 全屏拖动触控（CDP 真实触摸事件，页面不滚动）；**真机仍待测** | 2026-10-31 | 390×844 / 844×390 视口（Chrome） | `e2e-local-chrome-mobile-playing.png`、`m4-landscape-844x390.png` |
| 17 | 源码 + WebGL 构建文件 + README | ⏳ M5（`web/unity` 当前 ignore） | — | — | — |

## 2. 浏览器 / 设备矩阵

| 平台 | 浏览器 | 状态 | 日期 | 备注 |
|---|---|---|---|---|
| Windows | Chrome 最新 | ✅ | 2026-10-31 | 线上 M4-A：E2E 3 局 + 失焦/倒计时 + 刷新；本地 M4-B0：3 局 + 10 局 + 自动绕圈 60s 胜利 |
| Windows | Edge 最新 | ✅ | 2026-10-31 | 线上 M4-A：加载、开始、失焦暂停/倒计时、1 局结算正常 |
| Windows | Firefox 最新 | ✅ 本地；线上待测 | 2026-10-31 | Playwright Firefox 本地 M4-B0 跑通一局；线上因代理故障未测（见 §4） |
| macOS | Safari 最新 | ⏳ 待测 | — | 有设备则测 |
| iOS | Safari 15+ | ⏳ 待测 | — | 真机门禁（触控/HUD/最高分） |
| Android | Chrome 最新 | ⏳ 待测 | — | 真机门禁（触控/HUD/最高分） |

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

- **E2E smoke 可重复跑通**（`tools/e2e/smoke.mjs`）：
  - 线上 Chrome（2026-10-30，部署 `6ae4b4bf…`）：加载 → 昵称 → 开始 → **真实窗口失焦**（关掉 Playwright 焦点模拟 + 另开窗口）→ 已暂停 → 切回 3-2-1 → 恢复 → 3 局结算/重开 → 刷新最高分，全部 PASS，console 0 error。
  - 线上 Edge（同日）：同一流程 1 局，PASS。
  - 本地 Chrome（2026-10-31）：同一流程 3 局 + 失焦用例，PASS；证据见 `e2e-local-chrome-desktop-*.png`。
  - 本地 Firefox（Playwright 内置版）：加载、开始、1 局结算、刷新最高分，PASS（Firefox 没有焦点模拟开关，失焦用例跳过）。
  - 本地连续 **10 局**重开：每局都能重新进入 Playing、分数不累加、无回调丢失、console 0 error（`e2e-local-chrome-desktop-summary-10rounds.json`）。
  - 说明：`docs/evidence/` 里带 `local` 前缀的截图来自本地构建；线上那次的截图没有单独留档（之后被本地运行覆盖，已改为按目标命名），线上结果以执行记录中的运行输出为准。
- **失焦暂停 + 3-2-1 倒计时**：Unity 暂停面板显示大号 3→2→1（`unscaledDeltaTime`，倒计时期间隐藏「已暂停」标题避免叠加），结束才 Resume；切回不瞬时恢复。
- **结算只有一套**：WebGL 嵌入模式下 Unity ResultPanel 不再显示，H5 卡片是唯一结算；Editor 仍显示 Unity 面板（`m4-editor-result.png`）。
- **手机 HUD**：390×844 竖屏 HUD 放大（参考分辨率 1280×720 → 640×360），时间/生命/分数可读不重叠；844×390 横屏无纵向滚动（`m4-landscape-844x390.png`）。
- **触控**：CDP 真实触摸事件拖动画布，页面 `scrollY` 保持 0；`touch-action: none`。
- **敌人 P0/P1**：20 只同屏时中心最小间距 ≈0.75（分离半径 0.76）、包围站位、±12% 速度扰动；把 6 只敌人移到 50 单位外后 3 秒全部回收、列表无 null、后续继续生成（Editor 实测）。
- **性能（桌面）**：游戏内 6 秒 rAF 采样 165 FPS / p95 6.2ms（受 165Hz 屏限制，`m4-fps-desktop.json`）。
- **自动走位 60 秒胜利**：A 与 B0 构建本地都跑出 60.0s / 800 分（`m4-win-automated-local.png`，自动化；非确定性，也有 37～52 秒失败场次）。
- **大地图 / 相机（B0）**：地图 36×20；玩家贴边被钳在 (17.44, 9.44)、相机钳在 (7.18,5.00)（Editor 2.16 宽高比）；
  浏览器按住方向键 3 秒，玩家屏幕位置只偏移 26px（相机跟随），证明玩家移动 + 相机跟随。
- **出生 / 回收（B0）**：出生点以玩家为中心、1.15×相机半对角线且在视野外；进过视野的敌人离屏 3 秒被回收，
  从未进过视野的出生敌不会被误杀；10 局重开列表无残留。
- **触控语义**：锚点相对拖动，手指按住不动时玩家持续移动（相机滞后约 0.33 单位），松手归零（DPR2 390×844）。

## 4. 未关闭项

1. **B0 构建未部署 + 线上门禁未跑**：2026-10-31 本机代理（`127.0.0.1:7897`）对所有 HTTPS 握手失败
   （`schannel: failed to receive handshake`），`deploy-netlify.sh` 与线上 E2E 都无法执行。
   代理恢复后：部署 → `BASE_URL=... node smoke.mjs`（Chrome/Edge/Firefox）。
2. **手机真机**（iPhone Safari / Android Chrome）：触控、HUD、安全区、DPR/帧率仍未测；目前只有 390×844 / 844×390 / 768×1024 的浏览器视口证据
3. **人工 60 秒胜利 + 5 局平衡数据**：自动化脚本（M3 60.1s、M4 60.0s）只证明功能链路，胜率/首次受击统计需要人手
4. **正式美术 / 音效**：仍是占位圆点 + 网格；素材接缝已在 `EnemyDefinition`
5. **加载性能**：WebGL 产物约 9.3MB（wasm 6.97MB + data 2.45MB + framework/loader），Netlify 实测约 165KB/s、首次加载 50～61s；国内托管评估见 `docs/部署与托管.md`
6. **M5 交付**：`web/unity` 入库/Release zip、README/交付报告、干净克隆构建

## 5. 结论

- M0–M4-A 的演示闭环可用；线上 <https://survivalgame-demo.netlify.app> 仍是 A 构建（Unity `2026-10-30T23:11Z`，H5/CSS `0.1.0-m4.2`）。
- M4-B0（大地图 + 相机 + 视野外生成/回收 + 压力曲线）已在本地构建（`2026-10-31T07:13:43Z`）验证通过，**因代理故障尚未部署**。
- 17 条中第 16 条（手机适配/触控）已由视口级验证转绿，真机待测；只剩第 17 条（M5 交付）未做。第 5 条人工体验仍待门禁。
- M5 时把状态改为最终结论并附设备/日期。
