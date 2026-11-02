# C3 — Charger（冲锋者）实现与调参

> 日期：2026-11-03 · 状态：**代码完成并已重建验证（含冷却后退修正）；待提交**
> 最终构建：`builtAt 2026-11-03T12:15:40Z`（内部 1.2.1，**未入库/未部署**）
> 仓库外副本：`D:/unityFile/_builds/SurvivalGame-1.2.1-c3-final-2026-11-03/`
> 构建 v3（后退修正前）：`2026-11-03T11:56:54Z`，副本 `_builds/SurvivalGame-1.2.1-c3-v3-2026-11-03/`
> `web/unity` 已 restore 回线上 1.2.1；C3 不提交构建产物。

## 实现

- `EnemyAI` 新增 Charger 状态机：`Approach → Windup(0.45s) → Dash(0.45s) → Recover(0.8s) → Cooldown(1.5s)`。
- `Dash` 方向在冲刺开始时锁定；速度 `5.0 × 难度速度倍率`（困难 5.5），**不受 EnemySpeedMax 2.2 限制**，
  不吃 jitter/压力曲线；撞地图边界（从内向外）提前结束。
- 伤害：普通接触 12×难度倍率；冲刺 15×难度倍率，**同一次冲刺最多一次**（`_chargeHitDone`）。
- 表现：风窗改为亮黄 `(1,0.85,0.2)` + 1.2 倍缩放；冲刺纯红 + 1.15 倍；恢复回基色/基缩放。
- Dash 跳过软墙/切向让位，只保留 `<0.5` 的**横向**防重合（不改变冲刺方向/前进速度）。
- `EnemySpawner` 应用 `EnemyDefinition.Tint/VisualScale`；`PerfProbe` 支持 `enemyKind=charger` 强制类型
  （只在 `enemyLimit≤2` 时打状态日志，避免 40 只压测被日志污染）。
- 新增 `tools/e2e/enemy-probe.mjs`：默认 **headless**（`HEADED=1` 才开窗，`launch.mjs` 会离屏），
  监听 `[Charger] state=`，在 Windup/Dash/Recover 自动截图；`INVINCIBLE=0` 可验证伤害。

## 调参（M6 原值与实测冲突）

- M6 原值：触发 4.5 / 前摇 0.45 / 冲刺 0.45×5.0（=2.25u）。从 4.5 触发时冲刺只覆盖 2.25u，
  **停在射程外打空**；且冷却期会贴到停距 0.70 才前摇，前摇不可读。
- 改为：
  - `_chargeTriggerDistance = 2.8`（资产 + `SceneSetup`/`EnemyDefinition` 默认值）；
  - 冷却期：距离 < 触发距离 − 0.1 时**后退**到触发距离；±0.1 内原地等；更远才接近。
    这样冲刺穿过玩家后不会在 0.70 处立刻再次前摇。
- 冲刺从 2.8 出发覆盖 2.25u，终点约 0.55u（接触半径约 0.78），稳定命中；玩家有 0.45s 前摇可横向闪避。
- 后退修正已在最终构建验证：冲刺穿过后在冷却期退到 ~2.7～2.8，再前摇；不再贴脸。

## 实测（v3 before 后退 / 最终 after 后退）

- 单 Charger（headless，`enemyKind=charger&enemyLimit=1`）：
  - 首次 Windup 距离 2.8；Dash 0.46s；Recover 0.45s；Cooldown 0.81s；
  - 后退修正后：Recover 结束约 0.5u → Cooldown 后退到 ~2.7 → 再次 Windup 距离 2.8；
  - 伤害日志：冲刺 `damage=15`、普通接触 `damage=12`（`probe/charger-final-damage-console.log`）；
  - 截图：`review/m6-c3-charger/charger-windup-00.png`、`charger-dash-01.png`、`charger-windup-105.png`
    （用户先看，水印为内部、非最终视觉）。
- 40 只（headed 离屏，`enemyLimit=40&perfSeconds=90`）：

| 场景 | FPS | p95 | alloc KB/s | capAt | 备注 |
|---|---|---|---|---|---|
| C2 构建 40 默认（控制） | 165 | 7 | 29.6 | 49.49s | 当前环境复跑 |
| C3 40 默认 Chaser | 165 | 7 | 41.0 | 49.44s | +11.4 vs C2 |
| C3 40 `enemyKind=chaser` | 165 | 7 | 41.4 | 49.44s | 同上 |
| C3 40 `enemyKind=charger` | 165 | 7 | 40.6 | 49.43s | 与 Chaser 一致，不是 Charger 专属 |
| C3 最终 40 `enemyKind=charger` | 165 | 7 | 40.8 | 49.54s | 后退修正后；仍 +11 vs C2 |

- 结论：FPS/p95 达标；**alloc +11 KB/s 是 C3 构建全局的，不是 Charger 专属**；fillAlloc 也从 1.77→2.08MB。
  已去掉 spawn 路径的 `GetComponent<SpriteRenderer>()`（改为 Spawner 传入 renderer），待重建确认。
  若仍 >35KB/s，C5 继续查（优先看 `SpriteRenderer.color`/`localScale` 的每敌一次性分配与探针口径）。

## 收口与下一步

- UnityMCP 已恢复；最终构建、单 Charger 截图/伤害、40 charger 压测均已完成；`web/unity` 已回 1.2.1。
- 提交 C3：`EnemyAI.cs`、`EnemySpawner.cs`、`PerfProbe.cs`、`EnemyDefinition.cs`、`SceneSetup.cs`、
  `def_Enemy_Charger.asset`、`enemy-probe.mjs`、本目录证据。
- **未解决**：C3 构建 40 只 alloc 40.8KB/s（C2 29.6，+11）；FPS/p95 达标，且不是 Charger 专属。
  C5 用 30/40/50 混合再复核；若仍超 35，再定位（优先查 Spawner 的 color/scale 一次性分配与探针口径）。
- 下一步 C4：Flanker（侧绕/扑/超时直追），完成先给用户看片。

## 清理

- `web/unity` 已回 1.2.1（`builtAt 2026-11-02T13:05:32Z`）。
- `Assets/Fonts/NotoSansSC-UI-SDF.asset` / `Assets/Scenes/Game.unity` 已还原；`Assets/Resources/PerformanceTestRun*` 不存在。
- 工作区只保留本步代码/证据；本 checkpoint 提交到 wip（不动 main、不部署）。
