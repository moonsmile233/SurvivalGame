# C5 — 生成权重/解锁 + 上限 14/30/50 + 回收 1.5s + 到顶补位 + alloc 归因

> 日期：2026-11-04 · 状态：**已构建 / 自然混合 / 30-40-50 压测 / alloc 归因完成**
> 构建（中间构建，不入 `web/unity`）：
> - #5a `builtAt 2026-11-04T13:03:14Z`（9,651,974 B）：上限 + 权重 + 回收 + 补位
> - #5b `builtAt 2026-11-04T13:15:00Z`（9,635,135 B）：+ alloc 归因探针（quiet/noHud/spawnOps/walk/logRecycle/recycled）
> - 留证：`D:/unityFile/_builds/SurvivalGame-1.2.1-c5-2026-11-04/`、`...-c5b-2026-11-04/`
> 用户看片：`review/m6-c5-recycle/`（Charger 前摇/冲刺/回收）
> 证据：本目录 `perf/*.json|png`

## 1. 本步改动（代码/资产）

| 文件 | 改动 |
|---|---|
| `Assets/Configs/dif_Normal.asset` | `_enemyLimit 20 → 30` |
| `Assets/Configs/dif_Hard.asset` | `_enemyLimit 20 → 50` |
| `Assets/Configs/cfg_GameConfig.asset` | `_enemyRecycleSeconds 3 → 1.5` |
| `Assets/Editor/SceneSetup.cs` | 新建难度资产默认值 30 / 50（已有资产不覆盖） |
| `Assets/Scripts/Core/GameConfig.cs` | `_enemyRecycleSeconds` 默认 3 → 1.5；tooltip 更新（不再写死“20 只”） |
| `Assets/Scripts/Enemy/EnemySpawner.cs` | ①按 `DifficultyProfile` 解锁/权重加权选类型（`PickWeighted`，无分配）②到顶回收视野外最远 + 同帧补位 ③`CapBlockedCount`/`CapRecycledCount`/`RecycleCount` ④Awake 按 Kind 缓存三定义 ⑤perf-only：`enemyMix`/`noSpawn`/`spawnOps` |
| `Assets/Scripts/Enemy/EnemyAI.cs` | 新增只读 `HasBeenSeen`；perf-only 回收日志（前 20 条，验证 1.5s） |
| `Assets/Scripts/Core/PerfProbe.cs` | `enemyMix`/`noSpawn`/`quiet`/`noHud`/`spawnOps`/`walk`/`logRecycle` 参数；summary 新增 `chaser/charger/flanker/charging/flanking/capBlocked/capRecycled/recycled` |
| `Assets/Scripts/UI/HudView.cs` | perf-only `SetPerfSuppressUpdates`（alloc 归因，正常游玩恒 false） |
| `tools/e2e/perf.mjs` | 记录 `[Recycle]` 行到 JSON |
| `tools/e2e/enemy-probe.mjs` | 捕获 `[Recycle]`；`MOVE=straight`（甩开敌人验证 1.5s） |

生成规则（自然游玩，权重全部来自资产，代码无字面量）：
- `t < chargerUnlock` → 100% Chaser；`t < flankerUnlock` → 中期权重；之后晚期权重。
- 到顶：`TryRecycleFarthestOffscreen()` 只回收「进过视野 + 当前在视野外」里离玩家最远的一只，同帧补位；
  没有候选计 `CapBlockedCount`，不静默停刷。

## 2. 自然混合（不发 `enemyMix`，正常游玩路径）

| 局 | 时长 | 到顶 | 稳态敌人数 C/G/F | FPS | p95 | 说明 |
|---|---|---|---|---|---|---|
| Normal | 120s | 40.12s | 30 = 18 / 11 / 1 | 165 | 7ms | Flanker 35s 才解锁，局末只来 1 只属正常 |

- 三种敌人**自然出现**（不使用压测覆盖），说明解锁时间/权重生效。
- 60s 局的节奏：普通档约 40s 到顶 30；困难档约 57s 才到顶 50（局末 2~3 秒才满），符合 D1 预期。
- 证据：`perf/perf-local-chrome-desktop-cap1-c5-natural-normal.json`。

## 3. 回收 1.5s / 到顶补位（黑盒实测）

`?enemyMix=1&enemyLimit=40&walk=1&logRecycle=1`（玩家被探针驱动跑动，HEADED 离屏）：

- 20 条 `[Recycle] offScreen=1.50s threshold=1.50s`，Chaser / Charger / Flanker 都有 →
  **“进过视野后离屏 1.5s 回收”直接实测**，不是只改配置。
- 同一局 `recycled=71`（总回收）、`capRecycled=12`（到顶补位）、`capBlocked=6`（无合格候选）：
  - 补位机制确实在跑，`alive` 稳定在 38~40；
  - 被挡的生成有计数，可以区分「停刷」和「正常挡下」。
- 帧率不受影响：165 FPS / p95 7ms。
- 证据：`perf/perf-local-chrome-desktop-cap1-c5-walk-40-recycle.json`（`recycleLogs` 字段）。

## 4. 30 / 40 / 50 混合压测（`enemyMix=1`，桌面真 GPU、有头离屏）

| 上限 | 到顶 | 稳态 | FPS | p95 | p99 | alloc KB/s | C/G/F |
|---|---|---|---|---|---|---|---|
| 20 | 29.28s | 20 | 165 | 7 | 7 | 41.4 | 12/6/2 |
| 30（资产值） | 40.13s | 30 | 165 | 7 | 7 | 41.2 | 15/11/4 |
| 40 | 49.46s | 40 | 165 | 7 | 7 | 41.2 | 23/14/3 |
| 50 | 57.45s | 50 | 165 | 7 | 7 | 41.2 | 28/14/8 |

- 判定：**50 只达标**（≥160 FPS、p95 ≤7.5ms），不需要停报、也没有降回 20。
- `minPairDist` 稳态 0.015~0.079：由 Charger 冲刺穿过人群造成（C3 的 charger-only 记录就是 0.008，
  C4.2 的纯 Chaser 是 0.745）。这是 D4 允许的「冲刺保留 <0.5 横向防重合」，不是两只长期重合；
  50 只截图里三种颜色都可分辨，未见两块完全叠死。仍建议 C7 人工手感确认。
- 证据：`perf/perf-local-chrome-desktop-cap1-c5-mix-{20,30-asset,40,50}.json`。

## 5. alloc 归因（0 敌 / 探针负载 / HUD / per-spawn）

| 跑法 | 稳态敌人数 | alloc KB/s |
|---|---|---|
| `noSpawn=1`（0 敌，探针正常） | 0 | **48.2** |
| `noSpawn=1&quiet=1&noHud=1`（0 敌，关窗口日志+挤动统计+HUD 文本） | 0 | **47.8** |
| 20 mix | 20 | 41.4 |
| 30 mix | 30 | 41.2 |
| 40 mix | 40 | 41.2 |
| 50 mix | 50 | 41.2 |
| 40 mix + `spawnOps=7`（跳过 `renderer.color`/`localScale`/传 renderer→走 `GetComponent`） | 40 | 41.4 |
| 40 mix + `walk=1`（持续回收/补位，生成最频繁） | 38.2 | 42.4 |

结论：
1. **不是敌人代码**：0 敌反而 48.2 KB/s，比 20~50 只（41.2~41.4）还高；
   每多一只敌人没有正相关，`spawnOps` 三项全跳过也只差 0.2 KB/s（噪声内）。
2. **不是探针日志/挤动统计**：`quiet=1` 只降 0.4 KB/s；**不是 HUD/TMP**：`noHud=1` 无可见变化。
3. C3 起的 +11 KB/s（C2 29.6 → C3 40.8）在当前环境**无法复现**：今天 0 敌基线就是 47~48，
   比任何敌人跑法都高。该差值更像环境/采样漂移（同机 Chrome 版本、GC 时机变化），不是 C3 引入的泄漏。
4. 因此 **≤35 KB/s 不能靠敌人/生成代码达成**；残余 41~48 KB/s 是基线（Unity WebGL 运行时 + 物理/UI 内部）。
   要做必须单独开票做基线排查（换干净 profile 重测 / 逐组件禁用），不在 C5 范围内偷偷改。
5. 采样口径提醒：`steadyAllocKBps` 只累加 `GC.GetTotalMemory` 的**正增量**，GC 越频繁数字反而越小，
   所以 0 敌 > 40 敌 是正常的测量假象，不能当“敌人省内存”解读。

## 6. 回归

- `tools/e2e/smoke.mjs` 全绿（菜单/EnterGame/暂停/结算 265/再来一局/刷新保留最高分，console 无 error）。
- 未跑：standalone-menu / focus / h5-shell / 6 视口（C5 不改菜单/协议，留 C7 全量）。

## 7. 遗留 / 风险

- alloc ≤35 KB/s 未达成，原因见 §5；需要策划决定是否单开基线排查票。
- `minPairDist` 在含 Charger 的混编局长期 <0.6（冲刺穿过人群的瞬时值），C7 需人工确认手感。
- 困难档 50 只在 60s 局末才满，实际持续压力有限；5 局基线后再决定是否调整生成间隔。
- 中间构建未入 `web/unity`；C7 才做 1.3.0 干净构建。
