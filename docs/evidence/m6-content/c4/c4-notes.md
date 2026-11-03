# C4 — Flanker（绕行者）实现与验证

> 日期：2026-11-03 · 状态：**代码完成，已构建/探针/40 只压测验证**
> 最终构建：`builtAt 2026-11-03T13:17:33Z`（内部 1.2.1，**未入库/未部署**）
> 仓库外副本：`D:/unityFile/_builds/SurvivalGame-1.2.1-c4-final-2026-11-03/`
> 首次 C4 构建：`2026-11-03T12:39:45Z`（`_builds/SurvivalGame-1.2.1-c4-2026-11-03/`，仅留证）
> `web/unity` 已 restore 回线上 1.2.1；C4 不提交构建产物。
> 截图右上/右下可能有 `trial version` 水印，仅内部证据；C7 必须用干净授权重建。

## 实现

- `EnemyAI` 新增 Flanker 状态机：`Flank → Pounce / Direct`。
  - `Flank`：目标点 = 玩家 + 侧后方 `2.4`；玩家在动用玩家速度方向做 forward，
    **玩家静止用出生时的固定参考方位**（`_flankReferenceBearing`）。若用每帧“玩家→自己”的实时方位，
    目标点会跟着自己转，实际只是轻微内旋（实测 bearing 只到 ~43°），近似正面直冲。
  - 到达目标点（<0.6）后 `_flankPointReached` 闩锁，之后一路 seek 玩家；
    否则每帧在“追目标点/追玩家”间切换，会在 `2.4-0.6=1.8` 处抖动不扑（首轮实测卡 4s 后靠超时转 Direct）。
  - 距玩家 ≤`1.2` → `Pounce`；进入 `4.5` 交战距离后计时 `6s` 未切入 → `Direct`。
    出生点 11.7u 赶路不计时，避免 6s 在半路耗尽。
  - 目标点钳在地图内；接触伤害仍 12×难度倍率。
- `PerfProbe`：`enemyKind=flanker` 强制类型；`flankTimeout=秒` 为 **perf-only** 覆盖，
  压短超时用于黑盒验证 `Direct` 分支（正常游玩不传该参数）。
- `EnemyAI` 探针日志（仅 `s_logFlankerStates`，即 `enemyLimit≤2`）：
  - 出生/状态切换：`[Flanker] state=... t=... pos=...`
  - `Flank` 期间每 0.5s：`dist`、`bearing`（相对玩家速度方向；静止时相对出生方位）、`abs`（世界角）、`moving`。
- `tools/e2e/enemy-probe.mjs`：
  - `Flank` 日志每 0.5s 只拍首帧状态图，另在进入 4.5 时补一张、第 5 条轨迹日志补一张，避免几十张截图。
  - `MOVE=circle`：W→D→S→A 每 0.9s 换向，黑盒驱动玩家绕圈。
  - `FLANK_TIMEOUT=秒`：perf-only 透传。

## 实测

### 静止玩家（`KIND=flanker`，headless，30s）

- 轨迹：`bearing` 0 → 127°（侧后），`dist` 10.99 → 1.42，`Pounce t=9.67`。
- 中途在 1.5～1.9 距离有约 1s 绕圈（目标点半径 2.4，进入后需重新 seek 玩家），可接受。
- 截图（`review/m6-c4-flanker/` 与 `probe/flanker/`）：
  `flanker-flank-00.png`（出生）、`flanker-flank-mid-01.png`（侧绕途中）、
  `flanker-flank-engage-02.png`（进入 4.5）、`flanker-pounce-03.png`（扑）、`flanker-final-04.png`。
- 控制台：`probe/flanker/flanker-console.log`；摘要 `flanker-summary.json`。

### Direct 超时（`flankTimeout=0.5`，headless，20s）

- 黑盒 `MOVE=circle` 在 6s 超时内仍会追上（两次实测 Pounce 6.14 / 7.73），无法自然触发 Direct；
  按策划允许的兜底改为 perf-only 覆盖。
- 结果：进入 4.5 后 0.5s 转 `state=Direct t=5.90`，截图 `flanker-direct-03.png`。
- 证据：`probe/flanker-direct/`。

### 40 只（headed 离屏，`enemyLimit=40&perfSeconds=90&enemyKind=flanker`）

| 构建 | FPS | p95 | p99 | alloc KB/s | 到顶 s | 备注 |
|---|---|---|---|---|---|---|
| C2 控制（Chaser） | 165 | 7 | 7 | 29.6 | 49.49 | 历史对照 |
| C3 最终（Charger） | 165 | 7 | 7 | 40.8 | 49.54 | 历史对照 |
| C4 首构建（旧行为） | 165 | 7 | 7 | 41.4 | 49.47 | 12:39 构建 |
| **C4 最终** | **165** | **7** | **7** | **41.2** | **49.48** | 13:17 构建 |

- FPS/p95 达标；alloc 与 C3 一致（41 左右），**不是 Flanker 专属**，C5 继续归因。
- 证据：`perf/perf-local-chrome-desktop-cap40-c4-40-final.json` + 两张截图。

## 收口

- `web/unity` 已 restore 回 1.2.1（`builtAt 2026-11-02T13:05:32Z`）。
- `Assets/Fonts/NotoSansSC-UI-SDF.asset` / `Assets/Scenes/Game.unity` 已还原；`Assets/Resources/PerformanceTestRun*` 不存在。
- 本步提交：`EnemyAI.cs`、`PerfProbe.cs`、`enemy-probe.mjs`、本目录证据。
- **未解决**：40 只 alloc 仍 ~41KB/s（C2 29.6，+11），C5 用 30/40/50 混合复核并归因。
- 下一步 C5（上限 14/30/50、alloc 归因、回收 1.5s、到顶补位、权重解锁）；C4 未验收前不开 C5。
