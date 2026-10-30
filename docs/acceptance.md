# 验收记录（living document）

> 最后更新：2026-10-30（M3 门禁补测：浏览器自然 60 秒胜利 + Edge）
> 规则：只写实际跑过的结果；没跑就写「待测」。证据放 `docs/evidence/`。
> 测试用例与门槛见 `docs/测试计划.md`。

## 1. 原始需求 17 条

| # | 需求 | 状态 | 日期 | 设备/浏览器 | 证据 |
|---|---|---|---|---|---|
| 1 | 2D 场景（玩家/敌人/背景/UI） | ✅ | 2026-10-30 | Editor / Chromium | `m2-playing.png`、`m3-hud-enemies.png` |
| 2 | 玩家移动（WASD/方向键，不出界） | ✅ Editor + 浏览器合成按键 | 2026-10-30 | Editor / Chromium | `m3-move-before/after.png` |
| 3 | 敌人定时生成并追踪 | ✅ | 2026-10-30 | Editor / Chromium | `m2-playing-2.png` |
| 4 | HP、受击掉血、无敌帧 | ✅ | 2026-10-30 | Editor | `docs/M1-执行记录.md` |
| 5 | 生存 60 秒胜利 | ✅ 有头 Chrome + 自动走位脚本，60.1s 胜利，H5 结算/最高分正确（**自动化，非人工**） | 2026-10-30 | 真实 Chrome（有头、GPU）+ 脚本 | `m3-win-result.png`、`m3-win-refresh.png`；人工版待 M4 门禁 |
| 6 | 失败 | ✅ | 2026-10-30 | 线上 Chromium | `m3-online-result.png` |
| 7 | 重新开始（全量重置） | ✅ | 2026-10-30 | Editor / Chromium | `m3-restarted.png` |
| 8 | 暂停 | ✅（Unity HUD + 可见性自动暂停） | 2026-10-30 | Chromium | `m3-paused.png` |
| 9 | 分数与剩余时间 | ✅ | 2026-10-30 | Editor / 线上 | `m3-online-result.png` |
| 10 | Unity WebGL 构建可在浏览器运行 | ✅ | 2026-10-30 | Chromium | 线上地址 |
| 11 | 昵称输入，H5 显示 | ✅ | 2026-10-30 | Chromium | `m3-online-result.png` |
| 12 | 开始按钮（ready 前禁用） | ✅ | 2026-10-30 | Chromium | `docs/M3-执行记录.md` |
| 13 | 嵌入游戏（非 iframe） | ✅ | 2026-10-30 | Chromium | `web/js/bridge.js` |
| 14 | Unity → H5 传分 | ✅ | 2026-10-30 | 线上 Chromium | `m3-online-result.png` |
| 15 | 最高分 localStorage，刷新仍在 | ✅ | 2026-10-30 | 线上 Chromium | `docs/M3-执行记录.md` |
| 16 | 页面适配电脑和手机宽度 | ⚠️ 布局通过；**手机触控未实现（M4）** | 2026-10-30 | 390×844 视口 | `m3-mobile-390.png` |
| 17 | 源码 + WebGL 构建文件 + README | ⏳ M5（`web/unity` 当前 ignore） | — | — | — |

## 2. 浏览器 / 设备矩阵

| 平台 | 浏览器 | 状态 | 日期 | 备注 |
|---|---|---|---|---|
| Windows | Chrome 最新 | ✅ | 2026-10-30 | 本地：自然 60 秒胜利 + 刷新最高分；线上 M3 构建可玩 |
| Windows | Edge 最新 | ✅ | 2026-10-30 | 本地修复构建加载 4.3s；线上部署修复后复测：加载 52s、开始正常、1 分钟后正常结算 |
| Windows | Firefox 最新 | ⏳ 待测 | — | M4 门禁 |
| macOS | Safari 最新 | ⏳ 待测 | — | 有设备则 M4 |
| iOS | Safari 15+ | ⏳ 待测 | — | M4 真机必测 |
| Android | Chrome 最新 | ⏳ 待测 | — | M4 真机必测 |

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

## 4. 未关闭项

1. **手机真机 + 触控**（M4）
2. **人工 60 秒胜利**（M4 真机/人手门禁）：自动化脚本只证明功能链路，不能替代人工体验
3. **M3 观察项**：首次构建曾出现一次状态回调全丢失，重建后未复现；本次已定位同类时序问题的根因（§3），保留两条诊断日志备用。若 M4「连续重开 10 次 / 5 分钟」复现，再上 `RequestState` 或重发机制
4. **加载性能**：WebGL 产物约 9.4MB（wasm 6.96MB + data 2.45MB + framework/loader），Netlify 实测约 165KB/s、首次加载 50～61s；M4 优先评估国内托管，M4/M5 评估 stripping / 缓存
5. **M5 交付**：`web/unity` 入库/Release zip、README/交付报告、干净克隆构建

## 5. 结论

- M0–M3 的演示闭环可用；线上 <https://survivalgame-demo.netlify.app>（Unity 构建 2026-10-30 21:30，H5 时序修复已于同日部署：deploy `6ae4a9c7b9f43373ae9bd439`）。
- 17 条中只剩第 16 条（手机触控）、第 17 条（M5 交付）未绿；第 5 条已于 2026-10-30 由自动化脚本验证通过，人工体验仍待 M4。
- M4 收口时更新本表；M5 时把状态改为最终结论并附设备/日期。
