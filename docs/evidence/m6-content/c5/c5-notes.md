# C5 — 生成权重/解锁 + 上限 14/30/50 + 回收 1.5s + 到顶补位 + Dash 穿阵 + alloc 归因

> 日期：2026-11-04 · 状态：**已构建 / 自然混合 / 30-40-50 压测 / 隔离 / alloc 对照完成**
> 最终构建（中间构建，不入 `web/unity`）：`builtAt 2026-11-04T15:29:40Z`，
> 留证 `D:/unityFile/_builds/SurvivalGame-1.2.1-c5l-2026-11-04/`（其余过程构建 c5…c5k 同目录保留）。
> 证据：本目录 `perf/*.json|png`；用户看片 `review/m6-c5-recycle/`（Charger 前摇/冲刺/回收）、
> `review/m6-c5-dash/`（Dash 半透明穿阵截图，待确认）。

## 1. 本步改动

| 文件 | 改动 |
|---|---|
| `Assets/Configs/dif_Normal.asset` | `_enemyLimit 20 → 30` |
| `Assets/Configs/dif_Hard.asset` | `_enemyLimit 20 → 50` |
| `Assets/Configs/cfg_GameConfig.asset` | `_enemyRecycleSeconds 3 → 1.5` |
| `Assets/Editor/SceneSetup.cs` | 新建难度资产默认值 30 / 50（已存在资产不覆盖） |
| `Core/GameConfig.cs` | 回收默认 3→1.5；tooltip 更新 |
| `Enemy/EnemySpawner.cs` | ①难度解锁/阶段权重加权选类型（无分配）②到顶回收视野外最远 + 同帧补位 ③`CapBlocked/CapRecycled/Recycle/Spawned*` 计数 ④`MinPairDistanceAll` / `DashPairCount`（Dash pair 单独统计，不计入验收指标）⑤perf-only：`enemyMix`/`noSpawn`/`spawnOps` |
| `Enemy/EnemyAI.cs` | ①`HasBeenSeen` ②**Dash 完全跳过敌人-敌人分离/防重合**（软墙/让位/兜底都不参与，也不被别人推开），方向/速度/距离/撞边界不变，玩家 Trigger 不变 ③Dash 半透明（alpha 0.72）+ 1.15 缩放作可读信号 ④Windup/Recover 允许切向让位（站桩时不变成堆叠“柱子”）⑤perf-only 回收日志 |
| `Core/PerfProbe.cs` | `enemyMix`/`noSpawn`/`quiet`/`noHud`/`spawnOps`/`walk`/`logRecycle`；summary 增加 per-kind、spawned*、cap*、recycled、`minPairDistAll`、`dashPairs`；每帧同步真实上限（修 hard 局 summary.limit 仍是 30 的问题） |
| `UI/HudView.cs` | perf-only `SetPerfSuppressUpdates`（alloc 归因） |
| `tools/e2e/perf.mjs` | 记录 `[Recycle]` 行到 JSON |
| `tools/e2e/enemy-probe.mjs` | 捕获 `[Recycle]`；`MOVE=straight` |

## 2. 自然混合（不发 `enemyMix`，玩家走动的真实路径）

`walk=1` 让玩家沿 X 往返跑动（探针平移 transform，仍受 PlayerController 边界钳制），
这样会持续触发视野外回收/补位，不是“站桩把人群吸住”的假稳态。

| 局 | 上限 | 到顶 | 稳态 | 生成累计 C/G/F（占比） | capBlocked | capRecycled | recycled | FPS / p95 |
|---|---|---|---|---|---|---|---|---|
| Normal 120s | 30 | 45.84s | 29.7 = 23/4/3 | 66/28/8 = 102（**65/27/8%**） | 46 | 37 | 72 | 165 / 7ms |
| Hard 120s | 50 | 73.25s | 42.3 = 28/9/13 | 76/48/35 = 159（**48/30/22%**） | 2 | 7 | 109 | 165 / 7ms |

- 与资产权重预期对照：Normal 晚期 55/30/15、Hard 晚期 45/35/20；计入早/中期 100% 追击后，
  期望约 Normal 61/27/12、Hard 49/33/18 —— 实测 **65/27/8** 与 **48/30/22**，在 n=102/159 的抽样噪声内。
- 三种敌人自然出现；`capBlocked/capRecycled/recycled` 都非 0，说明到顶不是静默停刷。
- 证据：`perf/*c5l-natural-walk-{normal,hard}.json`。

## 3. 回收 1.5s / 到顶补位（直接实测）

- 20 条 `[Recycle] offScreen=1.50s threshold=1.50s`（Chaser/Charger/Flanker 都有） →
  “进过视野后离屏 1.5s 才回收”实测成立（`perf/*c5-walk-40-recycle.json` 的 `recycleLogs`）。
- 同局 `recycled=71 / capRecycled=12 / capBlocked=6`；本轮最终构建 walk 局的 recycled/capRecycled 见 §2。

## 4. 30 / 40 / 50 混合压测（`enemyMix=1&walk=1`）与 Dash 穿阵验收

| 上限 | 到顶 | 稳态 | FPS | p95 | alloc KB/s | 非 Dash minPair（p50） | 全量 minPair | Dash pair 数 |
|---|---|---|---|---|---|---|---|---|
| 30（资产） | 49.45s | 29.3 | 165 | 7 | 41.0 | **0.771（0.82）** | 0.031 | 0.63 |
| 40 | 72.12s | 39.1 | 165 | 7 | 41.1 | **0.653（0.80）** | 0.054 | 0.91 |
| 50 | 85.20s | 42.2 | 165 | 7 | 41.2 | **0.501（0.77）** | 0.016 | 0.93 |

- 验收口径按 2026-11-04 策划/程序共识：`minPairDist` **排除正在 Dash 的 Charger 参与的 pair**；
  同时上报全量 `minPairDistAll` 与 `dashPairs`，指标没有被“做没”。
- 非冲刺 pair：30/40 只在个别窗口瞬态 <0.6（1/23、1/11），p50 0.80 左右；50 有 3/15 个窗口 <0.6
  （最低 0.50），是人群最密时的挤压，不是完全重合。
- 全量 minPair 0.016~0.054：全部来自冲刺穿阵（DashPair 0.6~0.9 对/帧），按设计由人工看片判断。
- 50 只仍然 **165 FPS / p95 7ms**，不需要停报。
- 截图：`perf/*c5l-mixwalk-50.png` 里可以看到两只半透明（Dash）Charger 穿过人群。

## 5. 隔离（40 只单类型，静止玩家）

| 类型 | 非 Dash minPair（p50） | 全量 minPair | Dash pair 数 | 结论 |
|---|---|---|---|---|
| Chaser | **0.775（0.775）** | 0.775 | 0 | 健康 |
| Flanker | **0.735（0.735）** | 0.735 | 0 | 健康 |
| Charger | **0.442（0.478）** | 0.002 | 13.53 | 纯 Charger 极限压力场景：Dash 穿阵后人群压实，非 Dash pair 稳定在 ~0.48 |

- 纯 Charger 40（100% 都是会冲刺/站桩的敌人、玩家静止）是全场景里最坏的一种；
  在真实混合（Charger 只占 10~30%）里非 Dash pair 的 p50 是 0.77~0.82。
- 期间试过并否掉的三种“修补”（均比现状更差或无效，不再保留代码）：
  - Dash 横向自避让（1.4u 提前量、5.5u/s 上限）→ 非 Dash min 仍 0.5（c5c）；
  - Dash 撞人就停（0.62u）→ 0.448/0.494（c5d），且冲刺几乎打不中；
  - 走廊挤开邻居（ApplyShove）/ 提高防重合下限与强度 → 0.014/0.209，明显更差（c5j/c5k）。

## 6. alloc 归因（同机、同 perf 脚本）

| 构建 / 跑法 | 稳态敌人数 | alloc KB/s |
|---|---|---|
| **C4.2 构建** cap=1 | 1 | 42.4 |
| **C4.2 构建** cap=20 | 20 | 41.0 |
| **C4.2 构建** cap=40 | 40 | 41.4 |
| C5 构建 `noSpawn=1`（0 敌） | 0 | 48.2 |
| C5 构建 0 敌 + `quiet=1&noHud=1` | 0 | 47.8 |
| C5 构建 mix 20 / 30 / 40 / 50 | 20~50 | 41.0~41.4 |
| C5 构建 mix 40 + `spawnOps=7`（跳过 tint/scale/传 renderer→GetComponent） | 40 | 41.4 |

结论：
1. **C4.2 与 C5 在同一环境、同一脚本下都是 41~42 KB/s**：C5 没有引入新的 per-enemy 分配。
2. 0 敌反而 48.2（比任何有敌局都高），`quiet/noHud` 只影响 <0.5 KB/s，`spawnOps` 三项全跳过无差异 →
   `+11KB/s`（C2 29.6 → C3 40.8）在当前环境**复现不了**，属于环境/采样漂移（同机 Chrome、GC 时机），
   不是 C3/C5 的敌人代码。
3. **≤35 KB/s 在本环境不可达**：残余 41~48 KB/s 是基线（Unity WebGL 运行时 + 物理/UI 内部）。
   要达标必须单开基线排查票（干净 profile / 逐组件禁用），不在 C5 里偷偷改。
4. 采样口径：`steadyAllocKBps` 只累加 `GC.GetTotalMemory` 正增量，GC 越频繁数字越小，
   所以“0 敌 > 40 敌”是测量假象，不能读成“敌人省内存”。

## 7. 回归

- `tools/e2e/smoke.mjs` 全绿（菜单/EnterGame/暂停/结算 265/再来一局/刷新保留最高分，console 无 error）。
- 未跑：standalone-menu / focus / h5-shell / 6 视口（C5 不改菜单/协议，留 C7 全量）。

## 8. 遗留 / 风险

- alloc ≤35 KB/s 未达成，原因见 §6；需要策划决定是否单开基线排查票。
- 纯 Charger 40 的非 Dash pair 稳定在 ~0.48（低于 0.6），是“人群压实”而不是穿模；
  真实混合局 p50 0.77~0.82。要进一步提高只能改人群模型（软墙/防重合的等效刚度），属于整体手感决策。
- 构建偶发 “trial version” 水印（本次 c5l 截图出现）；C7 必须先确认 Unity Personal 干净授权再重建。
- 困难档 50 只在 60s 局末才满（120s 压测 73s 到顶），实际持续压力有限；5 局基线后再定是否调生成间隔。
- 中间构建未入 `web/unity`；C7 才做 1.3.0 干净构建。
