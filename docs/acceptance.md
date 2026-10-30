# 验收记录（living document）

> 最后更新：2026-10-30（M3 收口）
> 规则：只写实际跑过的结果；没跑就写「待测」。证据放 `docs/evidence/`。
> 测试用例与门槛见 `docs/测试计划.md`。

## 1. 原始需求 17 条

| # | 需求 | 状态 | 日期 | 设备/浏览器 | 证据 |
|---|---|---|---|---|---|
| 1 | 2D 场景（玩家/敌人/背景/UI） | ✅ | 2026-10-30 | Editor / Chromium | `m2-playing.png`、`m3-hud-enemies.png` |
| 2 | 玩家移动（WASD/方向键，不出界） | ✅ Editor + 浏览器合成按键 | 2026-10-30 | Editor / Chromium | `m3-move-before/after.png` |
| 3 | 敌人定时生成并追踪 | ✅ | 2026-10-30 | Editor / Chromium | `m2-playing-2.png` |
| 4 | HP、受击掉血、无敌帧 | ✅ | 2026-10-30 | Editor | `docs/M1-执行记录.md` |
| 5 | 生存 60 秒胜利 | ⚠️ **浏览器自然胜利未跑**；Editor 自然/反射已过 | 2026-10-30 | Editor | 待补 |
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
| Windows | Chrome 最新 | ✅ 部分 | 2026-10-30 | M3 本地 + 线上；60 秒胜利待补 |
| Windows | Edge 最新 | ⏳ 待测 | — | 同 Chromium 内核，未单独跑 |
| Windows | Firefox 最新 | ⏳ 待测 | — | M4 门禁 |
| macOS | Safari 最新 | ⏳ 待测 | — | 有设备则 M4 |
| iOS | Safari 15+ | ⏳ 待测 | — | M4 真机必测 |
| Android | Chrome 最新 | ⏳ 待测 | — | M4 真机必测 |

## 3. M3 已验证项（摘要）

- 昵称：空 →「玩家」；超长/emoji 按码点截 16；`<img src=x>` 纯文本
- localStorage 不可用 → 内存回退 + 提示；文件协议/加载失败 → 错误卡片 + 重试
- 可见性自动暂停；手动暂停不被自动恢复
- 连续重开 3 次；`onGameOver` 每局一次；平分不算新纪录
- 线上部署 + 刷新最高分

详见 `docs/M3-执行记录.md`。

## 4. 未关闭项

1. **浏览器 60 秒自然胜利**（M3 门禁）
2. **Edge 单独跑**（M3 门禁）
3. **手机真机 + 触控**（M4）
4. **M3 观察项**：首次构建曾出现一次状态回调全丢失，重建后未复现；保留两条诊断日志
5. **M5 交付**：`web/unity` 入库/Release zip、README/交付报告、干净克隆构建

## 5. 结论

- M0–M3 的演示闭环可用；线上 <https://survivalgame-demo.netlify.app>。
- 不能宣称"17 条全绿"：第 5、16 条分别缺浏览器自然胜利和手机触控，第 17 条等 M5。
- M4 收口时更新本表；M5 时把状态改为最终结论并附设备/日期。
