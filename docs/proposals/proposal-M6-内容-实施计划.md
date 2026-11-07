# M6 内容扩展实施计划：冲锋者 / 绕行者 / 计分改造 / 回归

> 文档版本：2026-11-07T23:30+08 · 作者：程序 · 状态：**C7 已完成收口：1.3.0 构建 `2026-11-07T13:46:43Z` 入 `web/unity`（9,670,524 B，无 trial 水印），全量回归 PASS；C7.5/C7.5c 围堵（前向出生 + Interceptor）经用户验收；未部署、未发 Release、main 未前移。C1–C6 见下：分段计分/血量分/倍率/胜利 200；HUD 0.1s；结算明细；协议 v3 增量；字体 75 码位；C5.3 Charger 7.0 / Flanker 0s；C5/C5.1 上限 14/30/50、回收 1.5s、到顶补位、Dash 穿阵；证据 `docs/evidence/m6-content/{c5,c6,c7}/`**
> 依据：`docs/tickets/M6-内容扩展.md` §3–§5、`docs/proposals/proposal-M6-重构-游戏优先-v1.2.md`、`docs/参考拆解.md` §2.6；
> 策划 2026-11-02 跨会话指令（敌人种类 → 计分改造 → 全量回归）。
> 基线：`main = wip = 198f1a6e`（v1.2.1 已部署线上）；`@` 空；**本计划只进 wip，不动 main、不部署、不发 Release**。
> 约束：不改已验收的 v1.2 菜单/状态机/协议 v3 语义；不引第三方、不拆架构、不搬目录；每步单独 checkpoint。

## 0. 摘要

1. **敌人从 1 种扩到 3 种**：保留 Chaser，新增 **Charger（冲锋）** 与 **Flanker（绕行）**；全部沿用现有
   网格邻居 / 对象生成 / 回收 / 软墙管线，不新增每帧分配；三种敌人**同圆形轮廓、独立 32×32 PNG**
   （Chaser 原图、Charger 红、Flanker 紫，颜色烤进图；Charger 另有黄色前摇图），不再是同一张图换色。
2. **生成按时间解锁 + 难度权重**：权重与解锁时间进 `DifficultyProfile`，Spawner 只做查表加权；
   到同屏上限时**回收视野外最远敌人并立即补位**，不静默停刷；远处回收 3s → **1.5s**。
   上限按 D1 为 **简单 14 / 普通 30 / 困难 50**（C5 写入资产；C1–C4 仍 14/20/20）。
3. **计分按 M6 §5/D4 重写**：0.1s 手感的分段存活分（10/12/15/20 每秒）+ 剩余血量分 + 难度倍率 + 胜利奖励 200；
   **不含擦身、不含击杀**；HUD 显示整数，结算显示最终分与明细。
4. **协议保持 v3**：只做**增量字段**（`breakdown.health` / `breakdown.multiplier` / `payload.hitCount`），
   H5 无需改代码；`score` 仍是 H5 记最高分的唯一字段。若要做“同难度最高分/新纪录”上屏，属于另开票，不在本计划。
5. **版本按新构建 1.3.0 处理**：本地构建 + 证据，更新 `web/unity` 与 `?v=`，`check-version` 通过；
   **不部署线上、不前移 main、不发 Release**。
6. 先做 **数据模型 + AI 公共管线抽取（无行为变化）**，再依次加冲锋 / 绕行 / 生成与上限 / 计分；
   每个可运行节点一次 WebGL 构建 + 回归，失败可单步回滚。

## 1. 范围 / 非目标 / 硬边界

### 1.1 范围

- 敌人：Charger、Flanker 的行为、参数、前摇表现、伤害、边界处理、回收兼容。
- 生成：`DifficultyProfile` 解锁时间与权重；Spawner 加权选择；到顶回收补位；回收时长 1.5s。
- 计分：`ScoreService` 重写（分段存活分 + 血量分 + 倍率 + 奖励）；HUD / 结算 / 桥接字段同步。
- 测试：计分自检、敌人类型探针、E2E 断言更新、30/40/50 混合压测、6 视口、17 条回归。
- 文档：实现总览 / 架构 / 协议 / 验收 / README / 遗留问题（程序 owner）；M6 ticket 与参考拆解由策划改。
- 构建：本地 1.3.0 构建入 `web/unity`，`web/index.html` 5 处 `?v=` 同步；不部署。

### 1.2 非目标（本票不做）

- 玩家攻击 / 武器 / 技能 / 击杀 / 道具 / 商店 / 技能树；敌人远程 / 弹幕 / 召唤 / 支援 / 精英 / Boss。
- 排行榜 / 账号 / 云存档 / 跨刷新存档；大量新的美术/音效素材（仅 C4.1 新增 Charger/Flanker 两枚敌人 sprite）；URP；第三方包；新 Input System。
- 修改 v1.2 菜单/暂停/结算/3-2-1/协议 v3 的既有语义；H5 新增局内控件。
- 人工 60 秒胜利、5 局平衡基线、Honor 90 Pro / iPhone 真机、国内托管、GitHub Release、Netlify 部署。
- 把默认同屏上限抬到 30～50 之外的值：D1 已放行 **14/30/50**，C5 生效；50 只不达标或手感差时停报，不静默降回 20。

### 1.3 硬边界（M6 §0，逐条遵守）

- 玩家没有任何攻击手段；敌人没有远程手段；伤害只来自**接触**与**冲锋冲刺**。
- 计分、难度、敌人都不得引入“击杀”概念；`kills` 字段保留但恒为 0、不参与公式。
- 若以后要加攻击，先改 `项目规范` 与 `bridge-protocol`，另开票，不在 M6 内夹带。

## 2. 敌人行为与参数

### 2.1 数据模型（只加数据，不写 `if` 难度分支）

**新增 `Assets/Scripts/Enemy/EnemyKind.cs`**：

```csharp
public enum EnemyKind { Chaser = 0, Charger = 1, Flanker = 2 }
```

**扩展 `EnemyDefinition`**（所有新字段进资产，代码只读）：

| 字段 | 默认 | 用途 |
|---|---|---|
| `_kind` | Chaser | 选择行为分支；Chaser 资产保持 0，兼容旧资产 |
| `_tint` | 白 | SpriteRenderer.color；颜色已烤进各自 PNG，tint 保持白色避免二次相乘 |
| `_visualScale` | 1.0 | 仅视觉缩放；**碰撞半径/停距不变** |
| `_moveSpeed` | 1.8 / 1.5 / 1.9 | 追击 / 冲锋接近 / 绕行 |
| `_contactDamage` | 12 | 普通接触伤害（三档都乘难度伤害倍率） |
| `_colliderRadius` | 0.38 | 三档保持一致，避免停距/拥挤手感变化 |
| `_chargeTriggerDistance` | **7.0**（C5.3） | Charger 进入前摇的距离（4.2 仍太近，玩家来不及读前摇；7.0 = 冲刺距离，起手即满射程） |
| `_chargeWindupSeconds` | 0.45 | 前摇；≥0.4s，可读 |
| `_chargeDistance` | 7.0 | 冲刺距离；时长 = 距离 / 实际冲刺速度（含难度倍率） |
| `_chargeSpeed` | 4.5 | 冲刺速度 fallback；难度档案 `_chargeSpeed`（4.0/4.5/5.0）优先，**不再乘任何倍率**、不受 `EnemySpeedMax=2.2` 限制 |
| `_chargeRecoverSeconds` | 0.8 | 冲刺后恢复 |
| `_chargeCooldownSeconds` | 1.5 | 恢复后冷却 |
| `_chargeDamage` | 15 | 冲刺伤害；每次冲刺最多结算一次 |
| `_flankRadius` | **3.0**（C5.2） | 绕行目标点半径（2.4 时玩家看不出与普通敌人的区别） |
| `_flankAngleDegrees` | 135 | 目标点相对玩家朝向的侧后方角度 |
| `_flankPounceDistance` | **1.6**（C5.2） | 距玩家 ≤ 该值进入扑击 |
| `_flankPounceSpeed` | **4.0**（C5.2） | 扑击短突进的绝对速度（不乘倍率，上限显式放开） |
| `_flankPounceSeconds` | **0.3**（C5.2） | 一次突进时长；0.3×4.0 ≈ 1.2u |
| `_flankPounceCooldown` | **1.2**（C5.2） | 两次扑击最短间隔，避免贴身连扑 |
| `_flankDisengageDistance` | **2.5**（C5.2） | 追离距离：玩家拉开超过它并持续一段时间 |
| `_flankDisengageSeconds` | **0.4**（C5.2） | 追离持续时间；到点回 Flank 重绕 |
| `_flankTimeoutSeconds` | 6 | 侧绕超时转直追 |

> 资产由 `SceneSetup` 生成：`def_Enemy_Chaser.asset`（已有）、新增 `def_Enemy_Charger.asset`、
> `def_Enemy_Flanker.asset`。**已存在的资产不覆盖**调好的值；新资产只在不存在时创建。
> 新字段用 `-1` 哨兵 + `SceneSetup` 一次性写入，避免旧 `dif_*.asset` 缺字段时静默吃到 0。

**扩展 `DifficultyProfile`**：

| 字段 | 简单 | 普通 | 困难 |
|---|---|---|---|
| `_chargerUnlockSeconds` | 25 | 15 | 10 |
| `_flankerUnlockSeconds` | 45 | 35 | 25 |
| `_earlyChaserWeight` | 1.00 | 1.00 | 1.00 |
| `_midChaserWeight` / `_midChargerWeight` | 0.85 / 0.15 | 0.75 / 0.25 | 0.65 / 0.35 |
| `_lateChaserWeight` / `_lateChargerWeight` / `_lateFlankerWeight` | 0.70 / 0.20 / 0.10 | 0.55 / 0.30 / 0.15 | 0.45 / 0.35 / 0.20 |

- **C5.2 起自然生成不再用早/中/晚权重**（字段保留、不再参与生成），改为 5-tick 袋：
  **2 普通 + 2 绕行 + 1 冲锋**（每轮洗牌；未解锁类型用普通补位），全解锁后约 40/40/20，
  满足“绕行者数量与普通敌人相当”。解锁时间仍是表中 25/45/15/35/10/25 秒。
- `_enemyLimit`：C1–C4 保持 14/20/20；C5 写 14/30/50（D1）。

### 2.2 Chaser（追击，保持不变）

- 现有 `EnemyAI` 行为、1.8 速度、12 伤害、软墙/让位/防重合全部保留。
- 本计划对 Chaser 的唯一改动是**公共管线抽取**（§8 C2），抽取后必须 40 只回归无行为差异。

### 2.3 Charger（冲锋）

**状态机**（`EnemyAI` 内部枚举 `ChargerState`，无新 MonoBehaviour）：

| 状态 | 进入 | 时长 / 速度 | 行为 | 退出 |
|---|---|---|---|---|
| Approach | 初始 / 冷却结束 | 1.5 × 难度速度倍率 | seek 玩家；走普通软墙/让位 | 距离 ≤ **7.0**（C5.3） → Windup |
| Windup | Approach 触发 | 0.45s；速度 0 | 停止；颜色渐变到橙红、缩放 1.0→1.2 | 计时结束 → Dash |
| Dash | Windup 结束 | 距离 7.0u；绝对速度 4.0 / 4.5 / 5.0（时长 1.75 / 1.56 / 1.40s） | **方向在 Dash 开始时锁定**，直线冲刺；不转向、不受 `EnemySpeedMax` 限制 | 距离走完 / 撞地图边界 → Recover |
| Recover | Dash 结束 | 0.8s；速度 0 | 停止；颜色/缩放恢复 | 计时结束 → Cooldown |
| Cooldown | Recover 结束 | 1.5s；1.5 × 难度速度倍率 | seek 玩家 | 计时结束 → Approach |

**伤害**：

- 普通接触（Approach / Windup / Recover / Cooldown）= `ContactDamage` 12 × 难度伤害倍率，四舍五入、最小 1。
- Dash 接触 = `ChargeDamage` 15 × 难度伤害倍率，四舍五入、最小 1；**同一次冲刺最多结算一次**。
- 实现：Dash 开始时 `_chargeHitDone = false`；`OnTriggerStay2D` 在 Dash 期间首次接触就置 true（即使玩家处于无敌帧，
  本次冲刺也不再尝试第二次）；其余状态走普通接触伤害。无敌帧仍由 `PlayerHealth` 统一处理。
- 冲刺速度不加随机扰动、不吃压力曲线：前摇/路径/速度必须稳定可读；用难度档案的绝对速度（4.0/4.5/5.0），不再乘速度倍率。

**边界与拥挤**：

- 冲刺撞地图边界提前结束：记录冲刺起点是否在地图内；**从内向外**越界时结束并钳回边界；
  从地图外生成的 Charger 允许先走进来。
- Dash 期间**跳过软墙与切向让位**（避免被推挤打断冲刺方向）；但为满足用户 2026-11-01 拥挤手感口径
  （禁止完全重合），保留 `<0.5` 防重合兜底的**横向分量**：只沿冲刺方向的垂线修正、限速，不改变冲刺方向和前进速度。
  详见 §7 决策点 D4。
- 前摇期间仍参与软墙，避免停住时和邻居完全重叠。

**可读性 / 公平性**：

- 前摇 0.45s ≥ 0.4s；颜色 + 缩放双通道提示；冲刺方向在 Dash 开始才锁定（前摇中不锁）。
- 冲刺绝对速度 4.0 / 4.5 / 5.0（三档都快于玩家 3.2）；距离 7.0u（时长 1.75 / 1.56 / 1.40s），
  路径固定、有边界终点；横向闪避可躲。前摇不可读就回退该敌人（M6 §8）。
- 同圆形轮廓的独立 PNG：基图红、前摇换黄色专用图、冲刺红 + 1.15 缩放，视觉缩放 1.05。

### 2.4 Flanker（绕行）

**状态机**：

| 状态 | 进入 | 行为 | 退出 |
|---|---|---|---|
| Flank | 初始 / Chase 追离后重回 | 目标点 = 玩家侧后方 **3.0**；seek 目标点，走软墙；到目标点 0.6 内后改为 seek 玩家（仍算 Flank） | 距玩家 ≤**1.6** → Pounce；在交战距离内持续 6s → Direct |
| Pounce | 距玩家 ≤1.6 且冷却已过 | **短突进**：进入时锁定朝玩家方向，**0.3s × 4.0u/s ≈ 1.2u**，之后回 Chase；伤害仍是普通接触 12 | 计时结束 → Chase |
| Chase | Pounce 结束 | 正常 seek 玩家（普通追击）；冷却 1.2s 内不再扑 | 距离 ≤1.6 且冷却结束 → Pounce；离玩家 >2.5 持续 0.4s → Flank（重置 6s 与目标点标记） |
| Direct | Flank 超时 6s | 与 Chaser 相同（不再回 Flank） | 持续 |

**目标点几何**：

- `playerVel = _playerBody.linearVelocity`；`|playerVel| > 0.3` 时 `forward = playerVel.normalized`，
  否则用**出生时的固定参考方位**（`_flankReferenceBearing`）；若用每帧实时方位，目标点会跟着自己转，
  实际只是轻微内旋、近似正面直冲。
- `side = rotate(forward, ±135°)`，符号取生成时随机的 `_sideSign`；`target = playerPos + side * 3.0`。
- 目标点钳制在地图边界内；目标点与玩家重合等退化情况直接转 Direct。
- 玩家移动时每帧更新目标点；静止时目标点固定在出生参考方位上，避免退化成正面直冲；6s 超时防止无限绕圈。

**伤害**：始终普通接触 12 × 难度倍率；扑击只是位移，没有独立的冲刺伤害/远程。
**可读性（C5.2）**：用户明确不要拖尾/半透明等新提示（Charger 有 `trial` 水印的构建除外）；
“绕到侧后 → 扑 → 再绕”靠行为本身可读，探针记录 `Flank→Pounce→Chase→Flank` 循环次数。

**可读性**：同圆形轮廓的独立紫色 PNG（颜色烤进图、tint 白）、视觉缩放 0.95；碰撞半径 0.38 不变。

### 2.5 与现有拥挤手感的兼容

- 三种敌人共用同一套软墙（R=0.90 → Dsoft=0.70）、切向让位、玩家侧闪、`<0.5` 防重合兜底；
  不新增两两位置修正，唯一位置写入仍是 `pos += velocity * dt`。
- Charger 只在 Dash 期间跳过软墙/让位；其余状态与 Chaser 相同。
- Flanker 的 `_flankPointReached` 切换不能用每帧 `Vector2.Distance` 以外的新分配；目标点每帧重算即可。
- 验收以用户人工手感为准（录屏/实玩）；探针的 `minPairDist` 只作回归指标，不替代手感。

## 3. 生成与上限

### 3.1 解锁与独立节拍（C5.2）

- 保留原 tick：1.5s 起、每 10s −0.15、下限 0.5s、×难度倍率；**每个 tick 只出 1 只**。
- 类型用 **5-tick 袋**：2 普通 + 2 绕行 + 1 冲锋，每轮 Fisher–Yates 洗牌；未解锁的类型用普通补位。
  全解锁后约 **40/40/20**，绕行者数量 ≈ 普通敌人（用户要求）。
- 解锁时间：Charger 25/15/10s；**Flanker 三档均为 0s（C5.3）**，从开局就和普通敌人一起出。
  所以开局袋是 2 普通 + 2 绕行 + 0 冲锋（Charger 位用普通补位），Charger 解锁后为 2/2/1。
- 早/中/晚权重字段保留在资产里，但**不再参与自然生成**。
- `PerfProbe`：`enemyKind=charger|flanker|chaser` 强制单类型；`enemyMix=1` 从 0s 起按同一袋强制三类参与
  （仅压测/证据用，正常游玩不生效）。

### 3.2 选型实现（无分配）

```text
if (bagIndex >= 5) refill();      // 解锁判定 + 补位 + 原地洗牌
kind = bag[bagIndex++];
return definition(kind) ?? chaser;
```

- 袋是 `EnemyKind[5]` 字段，洗牌用原地交换；不建数组/闭包/字符串。

### 3.3 同屏上限与回收补位（M6 §3.4 口径）

- 上限按 D1：简单 14 / 普通 30 / 困难 50（`DifficultyProfile.EnemyLimit`；C5 写入资产，C1–C4 仍为 14/20/20；压测覆盖优先）。
- **回收时长 3s → 1.5s**：`GameConfig.EnemyRecycleSeconds = 1.5f`；仍要求“进过视野后才计时”，避免误杀刚生成的敌人。
- **到顶不静默停刷**：Spawner 在 `_enemies.Count >= EnemyLimit` 时，先尝试
  `TryRecycleFarthestOffscreen()`：
  - 候选必须 `HasBeenSeen == true` 且当前在相机视野外（±0.05 margin），取离玩家最远的一只；
  - `Unregister` 后同一帧生成新敌人；生成点仍必须在视野外，玩家看不到“补位”；
  - 没有合格候选才跳过本次生成，并累加 `CapBlockedCount` 供探针读取。
- 新增只读探针：`CapBlockedCount`、`CapRecycledCount`；`PerfProbe` 汇总进 `[PERF-SUMMARY]`。
- 生成间隔递减、`SpawnIntervalMin=0.5s`、出生点 1.15×相机半对角线/视野外/与现有敌人 ≥0.45 全部不变。

### 3.4 默认上限（D1 已拍板：简单 14 / 普通 30 / 困难 50）

- **C5 起生效**：`dif_Easy.asset=14`、`dif_Normal.asset=30`、`dif_Hard.asset=50`；C1–C4 保持现状 14/20/20，
  先验证三种敌人的行为与 40 只混合，不把“上限”和“敌人行为”混在同一步。
- 普通 30 是 30～40 区间的下限；5 局基线后如需要，只改 `dif_Normal.asset` 到 40，代码不变。
- C5 必须跑 **30 / 40 / 50 混合压测**；50 只（困难）若不达标或手感差，**停报**，不静默降回 20。
- 到顶回收补位、回收 1.5s 仍按 §3.3；上限越高，回收补位越重要。

## 4. 计分公式（M6 §5 / D4）

### 4.1 公式

```text
存活分（按生存时间分段累计，每段内按秒速率连续累计）：
  0–15s    10 分/秒   → 满段 150
  15–30s   12 分/秒   → 满段 180
  30–45s   15 分/秒   → 满段 225
  45–60s   20 分/秒   → 满段 300
  满 60 秒合计 855

血量分 = 结算剩余 HP × 1（0～100）
基础分 = (存活分 + 血量分) × 难度倍率（1.0 / 1.3 / 1.6）
最终分 = round(基础分) + 胜利奖励 200（固定，不乘倍率）
```

- **0.1 秒粒度**：内部用 float 按 `elapsed` 纯函数重算存活分；HUD 显示整数，整数变化约每 0.05～0.1s 一次，
  不出现小数、不做“每秒 +10”的整秒跳字。
- 分段边界按 `elapsed` 精确切分（纯函数逐段取 `min(remaining, segmentLength)`），不依赖帧率累积。
- **不含擦身分**（M6 已决定）；**不含击杀分**：`kills` 保留但恒 0、不参与公式、不显示。
- 胜利时 `elapsed` 固定为 `RoundDuration`，存活分取满段 855；失败时按实际 `elapsed`。
- HUD 建议只显示存活分（含倍率），避免受击时分数倒扣；血量分与奖励在结算时加上（决策点 D2）。

### 4.2 配置项（全部进 `GameConfig`，代码不写字面量）

| 字段 | 值 | 说明 |
|---|---|---|
| `_survivalSegmentSeconds` | 15 | 每段秒数 |
| `_survivalRatesPerSecond` | 10 / 12 / 15 / 20 | 每段每秒分；序列化数组，只读 |
| `_scorePerHealthPoint` | 1 | 剩余 HP 换算 |
| `_winBonus` | 200 | 胜利奖励，不乘倍率 |

- 删除 `_scorePerSurvivedSecond` 与 `_scorePerKill` 的公式用途；`ScorePerKill` 不再参与计算。
- `DifficultyProfile.ScoreMultiplier` 继续用现有 1.0 / 1.3 / 1.6；局内锁定。

### 4.3 `ScoreService` 实现要点

- `SurvivalRawFor(float seconds)`：纯函数，逐段累计，`seconds` 钳到 `[0, RoundDuration]`；无分配。
- `Advance(float elapsed)`：每帧 `_survivalRaw = SurvivalRawFor(elapsed)`；不做浮点累积，避免漂移。
- `Finish(float elapsed, int remainingHealth, bool won)`：重算存活分、记录血量/胜负；胜利时 `elapsed` 取回合时长。
- 只读属性：
  - `DisplayScore = Mathf.FloorToInt(_survivalRaw * Multiplier)`（HUD，整数、单调）
  - `SurvivalScore = Mathf.RoundToInt(_survivalRaw * Multiplier)`（结算/协议明细）
  - `HealthScore = Mathf.RoundToInt(_remainingHealth * Multiplier)`
  - `FinalScore = Mathf.RoundToInt((_survivalRaw + _remainingHealth) * Multiplier) + WinBonus`
  - `Kills => 0`、`KillScore => 0`（协议字段保留）
- `OnScoreChanged` 只在 `DisplayScore` 变化时触发；`Reset()` 清零全部字段。
- `GameManager.Update` 只在 `Playing` 调用 `Score.Advance(_elapsed)`；`FinishRound` 调 `Score.Finish(...)`；
  `ResetRound` 调 `Score.Reset()`。暂停/倒计时/结算不累计（`Time.deltaTime=0` 已保证）。
- `PlayerHealth` 增加 `HitCount`（真正扣血时 +1，`ResetHealth` 清零），供结算“受击”与桥接字段。

### 4.4 参考分数表（满血胜利 / 常见失败）

| 结果 | 简单 | 普通 | 困难 |
|---|---|---|---|
| 2s 胜利、满血 | 320 | 356 | 392 |
| 5s 胜利、满血 | 350 | 395 | 440 |
| 15s 胜利、满血 | 450 | 525 | 600 |
| 30s 胜利、满血 | 630 | 759 | 888 |
| 60s 胜利、满血 | **1155** | **1442** | **1728** |
| 15s 阵亡、剩 70 HP | 220 | 286 | 352 |
| 30s 阵亡、剩 50 HP | 380 | 494 | 608 |
| 60s 阵亡、0 HP（未胜利） | 855 | 1112 | 1368 |

> 上表用于 E2E/自检断言；E2E 涉及分数的短局必须加 `invincible=1`，否则受击会改变剩余 HP，断言不稳定。

### 4.5 与旧分数 / 最高分的关系

- 新公式比旧公式高（普通 60s 满血：980 → 1442；困难 1160 → 1728；简单 800 → 1155）。
- H5 仍用单一共享最高分 `survival.bestScore`（v1.2 口径）；**不迁移、不清空**：旧最高分是历史高水位，
  新局更容易超过它，属于预期变化。若策划要求“清零重来”，需要单独确认（会丢用户数据，不推荐）。
- **不恢复** M6 §5 的“同难度最高分 / 新纪录上屏”：v1.2 已把最高分归 H5 共享；恢复会改协议/UI，
  属于回退 v1.2，另开票（见 §5.4 与 D3）。

## 5. UI 与协议影响

### 5.1 Unity 内 UI

- HUD：`分数` 显示 `DisplayScore`（存活分 × 倍率，整数）；剩余时间显示不变（仍按秒倒计时）。
- 结算面板：`本局得分 FinalScore`；已有信息行保留“难度 / 存活 / 倍率”；新增一行小字
  **“存活分 A · 血量分 B · 受击 N”**（`UiStrings` 新增 `SurvivalScoreLabel=存活分`、
  `HealthScoreLabel=血量分`、`HitLabel=受击`）。
- 320×568 等小视口：新增行允许换行/缩小字号；6 视口重跑，溢出就改成两行或去掉“受击”（见 D3）。
- 菜单难度卡、暂停菜单、状态机、3-2-1 全部不动。

### 5.2 字体（硬前置）

- 新增汉字：`血`、`量`、`受`、`击`（若加“次数”还会多 `次`、`数`；本计划先不加）。
- 步骤：`UiStrings.AllCharacters` → 同步 `Assets/Fonts/ui-chars.txt` → 用 `tools/font-subset.py`
  从完整 Noto Sans SC 重裁 `NotoSansSC-UI-Subset.otf` → 构建时 `FontAssetBuilder` 重烤图集。
- 漏掉任何字符，`FontAssetBuilder` 会以缺字列表中止构建；不允许用 `LegacyRuntime.ttf` 兜底。

### 5.3 协议（保持 v3，只做增量字段）

`onGameOver.payload` 新增/扩展（H5 可忽略未知字段）：

```json
{
  "protocol": 3,
  "event": "gameOver",
  "payload": {
    "result": "win",
    "score": 1442,
    "survivedSeconds": 60.0,
    "kills": 0,
    "nickname": "阿宝",
    "difficulty": "normal",
    "hitCount": 2,
    "breakdown": {
      "survival": 1112,
      "health": 130,
      "kills": 0,
      "winBonus": 200,
      "multiplier": 1.3
    }
  }
}
```

- `score` 仍是唯一权威最终分；H5 只用它记最高分/最近一局。
- `breakdown.survival` / `breakdown.health` 是**乘倍率后的整数分量**；由于取整，两项相加可能与 `score` 差 1，
  以 `score` 为准（H5 不展示明细）。
- `protocol` 仍为 3；H5 的 `protocol !== 3` 检查不变。**不新增协议版本**，避免旧 H5 被判版本不匹配。
- `onStateChange`、`onReady`、`EnterGame`、`onDifficultyChanged` 全部不变。

### 5.4 UI/协议冲突（必须由策划裁决）

M6 §5 的结算卡要求“同难度最高分 / 新纪录”，但 v1.2 已把最高分改为 **H5 单一共享最高分**，
Unity 结算不显示最高分；`onGameOver` 只单向传分。恢复“同难度最高分/新纪录”需要：

- H5 把最高分回传给 Unity（新消息/新字段），或 Unity 自己持久化（违反 v1.2 职责边界）；
- 分档最高分键 `survival.bestScore.{easy,normal,hard}` 回退（v1.2 已迁移到单一键）。

**本计划默认不做**：保持 v1.2 共享最高分，H5 的“新纪录”提示继续由 `score > best` 判断。
若策划坚持 M6 §5 原文，先停、另开票改协议与 H5，不在本票夹带。

## 6. 性能与测试

### 6.1 性能目标

- 桌面 Chrome（165Hz 屏）：40 只**混合**（`enemyMix=1`）稳态
  **≥160 FPS、p95 ≤ 7.5ms、alloc ≤ 35KB/s**，不劣于 1.2.1 基线（165 FPS / p95 7ms / 29.4～30.0KB/s）。
- C5 另外跑 **30 / 40 / 50** 三档混合；50 只（困难）若 <160 FPS / p95 >7.5ms 或手感差，停报，
  不静默降回 20（D1）。
- 无每帧托管分配：`EnemyAI.Update` / `EnemySpawner.Update` 不新增 LINQ/数组/闭包/字符串拼接；
  Charger/Flanker 的状态切换不加 `GetComponent`/`Find`。
- 包体：仅新增三枚 32×32 PNG（<3KB）；相对 1.2.1（9,651,481 B）增长 ≤0.3MB。
- 中端手机 ≥30 FPS：仍待 Honor 90 Pro 实测，本计划只记录、不宣称。
- 探针命令（沿用现有 `PerfProbe`）：
  `?perf=1&autostart=1&enemyLimit=40&enemyMix=1&roundDuration=600&perfSeconds=90&invincible=1`
- `[PERF-SUMMARY]` 新增：`chaser` / `charger` / `flanker` / `charging` / `flanking` / `capBlocked` / `capRecycled`；
  `perf.mjs` 收集并断言三种类型都出现过、`minPairDist` 不长期 <0.6。

### 6.2 测试分层

| 层 | 内容 | 工具 |
|---|---|---|
| 纯逻辑 | 计分公式、分段边界、血量分、倍率、胜利奖励、Reset | 新增 `Assets/Editor/ScoreSelfCheck.cs`（Editor-only，菜单/批处理运行，不加测试包） |
| 集成 | 三种敌人行为、前摇/冲刺/冷却、绕行超时、边界、伤害一次结算 | Editor Play + `?perf=1&enemyKind=...` 探针；截图/日志 |
| E2E | 菜单/暂停/重开/结算/最高分；计分断言；难度；6 视口 | Playwright `tools/e2e/` |
| 性能 | 30/40/50 混合、cap 补位、回收 1.5s、GC | `perf.mjs` + `[PERF-SUMMARY]` |
| 人工 | 60 秒、5 局基线、真机手感 | 用户；本计划不代替 |

### 6.3 需要更新的现有断言 / 用例

- `tools/e2e/standalone-menu.mjs`：hard 2s 胜利 **232 → 392**；分数断言 URL 加 `invincible=1`。
- `tools/e2e/h5-shell.mjs`：best / `#data-last` **232 → 392**；注入低分 100 仍不覆盖。
- `tools/e2e/smoke.mjs`：只断言格式与 best>0，不写死分数；确认 5s 普通局新值（395）后更新注释/期望。
- `tools/e2e/perf.mjs`：加 `enemyMix=1`；跑 **30/40/50** 三档；新增 per-kind / cap 字段；阈值按 §6.1；
  50 只（困难）不达标或手感差时停报，不静默降回 20。
- 新增 `tools/e2e/score.mjs`（或并入 standalone）：`roundDuration=2/15/30/45/60` + `invincible=1`，
  验证 §4.4 的普通/困难分值；分段边界用 15/30/45 精确回合。
- 6 视口：`standalone-menu` viewports + `h5-shell` viewports；新增结算明细行后重拍。
- 17 条 / 浏览器矩阵：沿用现有 smoke/h5-shell/standalone/focus；新增敌人类型不改变加载/暂停/结算链路。

### 6.4 不宣称的项

- 人工 60 秒胜利、5 局/档平衡、Honor 90 Pro / iPhone 真机、DPR>1、全屏真机语义、国内托管。
- 这些在内容实装后仍按 `acceptance.md` §4 保留为“待测”，不因本票变绿。

## 7. 风险 / 停点 / 需策划拍板

### 7.1 风险

| 风险 | 处理 |
|---|---|
| 冲锋不公平（前摇看不清 / 躲不掉） | 前摇 ≥0.45s、颜色+缩放、Dash 开始才锁向、真机手测；不可读就把 `_chargeWindupSeconds` 调到 0.55～0.6（只改资产） |
| Dash 穿过人群造成完全重合 | 只跳过软墙/让位，保留 `<0.5` 的**横向**防重合；40 只混合 + 用户人工看片；必要时缩短 Dash 或提高横向限速 |
| AI 公共管线抽取引入拥挤回归 | 抽取单独 checkpoint，行为不变；先跑 40 只 + 原拥挤探针，再做 Charger/Flanker |
| 三种敌人 + 生成权重导致 30/40/50 只性能退化 | 每步压测；不达标先优化状态判断/缓存；50 只不达标或手感差**停报**，不静默降回 20；不引第三方 |
| 计分改动影响 H5 最高分 | 不迁移、不清空；旧分是历史高水位；若策划要清零，单独确认 |
| 新增中文字符缺字导致构建失败 | 先改 `UiStrings` → `ui-chars.txt` → 重裁 OTF；构建前自检 |
| 旧 `dif_*.asset` 缺新字段 | `-1` 哨兵 + SceneSetup 一次性写默认；启动日志校验（chargerUnlock < flankerUnlock） |
| 到顶回收补位误杀玩家看得见的敌人 | 只回收 `HasBeenSeen` 且当前视野外的敌人；补位生成点仍在视野外；探针记录 |
| 权重 RNG 让短局测试不稳定 | 分数/性能测试用 `enemyKind`/`enemyMix` 覆盖；自然权重用 60s 长局观察 |
| 本票拖长交付线 | 每步可停；1.2.1 线上不动；失败回滚到 `198f1a6e` |

### 7.2 决策留痕（2026-11-02 策划放行）

| # | 决策 | 结论 |
|---|---|---|
| D1 | 同屏上限 | **简单 14 / 普通 30 / 困难 50**；C5 起生效；C5 跑 30/40/50 混合；50 只不达标或手感差**停报**，不静默降回 20；5 局后可只改资产把普通调到 40 |
| D2 | HUD 分数 | **只显示存活分×倍率**；血量分结算时加 |
| D3 | 结算明细 / 最高分 | **加“存活分·血量分·受击”一行**；最高分继续 H5 单一共享，不恢复分档最高分/新纪录 |
| D4 | Charger Dash 分离 | **跳过软墙/切向让位，保留 `<0.5` 横向防重合** |
| D5 | 版本 / 构建入库 | **1.3.0；只有 C7 最终构建入 `web/unity` + `?v=1.3.0`**；C1–C6 构建跑完 `jj restore web/unity`，不提交中间 9.6MB 构建 |

其他放行口径：

- **绕行来源不阻塞**：C4 按现有 Brotato/20MTD 来源推导先做；策划后续补 `参考拆解` 侧翼来源。
- M6 ticket 的“分档最高分/新纪录”、`项目规范` §6.1 击杀分 ×20 由策划改；程序按 **v1.2 共享最高分、无击杀** 实现，不代改这两份。
- **C3/C4 完成先给用户看片/截图**（Charger 前摇→Dash、Flanker 侧绕→扑）；手感不对先调资产参数再进 C5。
- 计分/协议增量/字体按本文；`protocol` 仍 3；H5 不改。
- 其余按本文默认值执行；实现中遇到与 v1.2/规范冲突，先停报，不擅自改。

## 8. 实施顺序与 checkpoint（放行后执行）

> 原则：**每个可运行节点一次 WebGL 构建 + 回归**；每个 checkpoint 单独 `jj describe` + `jj new`，
> 只推 `wip`；`main` 保持 `198f1a6e`，验收后由策划决定是否前移。失败两次就停、报告、不叠加。
>
> **构建策略（D5）**：C1–C6 的构建只用于本地验证；构建后把产物复制到仓库外 `D:/unityFile/_builds/` 作证据，
> 再 `jj restore web/unity`，不把中间 9.6MB 构建提交进仓库。C1–C6 不 bump `BundleVersion`（保持 1.2.1，
> 避免破坏 `check-version`）；只有 C7 把 `BundleVersion` 与 `?v=` 统一升到 1.3.0，并把最终构建入 `web/unity`。

| # | Checkpoint | 主要文件 | 构建 / 验证 |
|---|---|---|---|
| C0 | 本计划落盘 | `docs/proposals/proposal-M6-内容-实施计划.md` | 不构建；推 wip 等放行 |
| C1 | 数据模型 + 资产（无行为变化） | `EnemyKind.cs`、`EnemyDefinition.cs`、`DifficultyProfile.cs`、`SceneSetup.cs`、`Assets/Configs/*` | 构建 #1：三定义挂上、仍只生成 Chaser；standalone 短局 + smoke |
| C2 | `EnemyAI` 公共管线抽取（行为不变） | `EnemyAI.cs` | 构建 #2：cap40 压测对比 1.2.1；smoke；拥挤探针 |
| C3 | Charger 行为 + 前摇 + 伤害 + 边界 | `EnemyAI.cs`、`EnemyDefinition.cs`（资产值） | 构建 #3：`enemyKind=charger` 20s 探针；**前摇→Dash 截图/录屏先给用户看**；伤害日志；40 只 charger 压测 |
| C4 | Flanker 行为 + 绕行超时 | `EnemyAI.cs`、`EnemyDefinition.cs` | 构建 #4：`enemyKind=flanker` 探针；**侧绕→扑截图/录屏先给用户看**；40 只 flanker 压测 |
| C5 | 生成权重/解锁 + 上限补位 + 回收 1.5s | `EnemySpawner.cs`、`DifficultyProfile.cs`、`GameConfig.cs`、`PerfProbe.cs`、`Assets/Configs/dif_*.asset` | 构建 #5（临时）：写入 14/30/50；60s 自然混合；**30/40/50 混合压测**；capBlocked/capRecycled；1.5s 回收；50 不达标停报 |
| C5.3 | 触发距离 + 绕行解锁微调（用户 2026-11-05 口径） | `dif_*.asset`、`def_Enemy_Charger.asset`、`EnemyDefinition.cs`、`SceneSetup.cs` | 与 C6 同一个构建：开局袋 2 普通 + 2 绕行、Charger 0；解锁后 2/2/1；Charger 7.0 起手 |
| C6 | 计分重写 + HUD/结算/协议 + 字体 | `ScoreService.cs`、`GameManager.cs`、`PlayerHealth.cs`、`HudView.cs`、`ResultPanel.cs`、`UiStrings.cs`、`WebGLBridge.cs`、`SceneSetup.cs`、`ScoreSelfCheck.cs`、字体资产 | **已完成**：构建 `2026-11-07T08:41:44Z`（仓库外 `_builds/`）；ScoreSelfCheck PASS；`score.mjs` 7 局（含 45s=1052 / 60s 1442/1728）全绿；standalone/h5-shell/smoke/6 视口 PASS；证据 `docs/evidence/m6-content/c6/c6-notes.md` |
| C7 | 全量回归 + 文档 + 1.3.0 最终构建 | E2E、`docs/实现总览.md`、`architecture.md`、`bridge-protocol.md`、`acceptance.md`、`遗留问题.md`、`README.md`、`docs/M6-内容扩展-执行记录.md`、`web/index.html`、`web/unity/**` | **已完成**：构建 `2026-11-07T13:46:43Z`（1.3.0，唯一入 `web/unity`）；ScoreSelfCheck + score + standalone/h5/smoke/focus + 6 视口 + 30/40/50 全 PASS；`check-version` PASS；C7.5c 用户验收 |

### 8.1 每步的提交与证据

- 提交前 `jj status`，只提交本步文件；不整块 describe 混合改动。
- 证据：E2E summary / 截图 / `[PERF-SUMMARY]` / ScoreSelfCheck 日志，放 `docs/evidence/m6-content/`。
- 需要用户看的截图先放仓库根 `review/`（按用户偏好），确认后再移入 `docs/evidence/`。
- 构建日志 `Logs/build-webgl.log`；每次构建记录耗时/大小。

## 9. 验收清单（最终）

### 9.1 玩法

- [ ] 三种敌人同屏可分辨（颜色/尺寸/前摇）；Charger 前摇可读、玩家能侧步躲开；Flanker 不长期贴脸。
- [ ] Charger：7.0 触发、0.45s 前摇、7.0u 冲刺（4.0/4.5/5.0）、0.8s 恢复、1.5s 冷却；Dash 锁向、撞边界提前结束；
      一次冲刺最多一次伤害；普通接触 12、冲刺 15（均乘难度倍率）。
- [ ] Flanker：侧后方 3.0、距 ≤1.6 直接扑（0.3s × 4.0）、6s 超时转直追；地图内目标点；玩家静止时仍会侧绕。
- [ ] Chaser 行为与 v1.2 一致（抽取前后对比）。

### 9.2 生成 / 性能

- [ ] 三档解锁/权重生效；上限 14/30/50（C5 起）；到顶回收视野外最远敌人并补位，不静默停刷；回收 1.5s。
- [ ] 30/40/50 混合：桌面 ≥160 FPS / p95 ≤7.5ms / alloc ≤35KB/s；无每帧 GC；包体增长 ≤0.3MB；
      50 只（困难）不达标或手感差时停报，不静默降回 20。
- [ ] 三种类型都出现在 `[PERF-SUMMARY]`；`minPairDist` 不长期 <0.6；用户人工手感通过。

### 9.3 计分 / UI / 协议

- [ ] ScoreSelfCheck：0/15/30/45/60 分段正确；2/15/30/60 三档最终分与 §4.4 一致；失败血量分正确；Reset 干净。
- [ ] 60 秒满血普通 = 1442、困难 = 1728、简单 = 1155；胜利奖励只加一次；最高分按 H5 共享逻辑更新。
- [ ] HUD 整数、无小数、无“每秒 +10”跳字；结算显示最终分 + 明细行；320 宽不溢出。
- [ ] `onGameOver` 增量字段存在且 H5 不报错；protocol 仍为 3；`score` 与 H5 best 一致。
- [ ] 字体无 tofu；`check-version` PASS。

### 9.4 回归

- [ ] 17 条原有链路全绿（1–16 自动化；第 17 条交付物为本地 1.3.0，未部署）。
- [ ] standalone flow/mobile/viewports/focus；h5-shell core/viewports；smoke；6 视口；30/40/50 混合。
- [ ] 暂停/失焦/3-2-1/重开/返回菜单/结算/刷新不受影响；console 0 未预期 error。
- [ ] 人工 60 秒、5 局基线、真机仍标“待测”，不宣称收口。

## 10. 回滚 / 版本

- 基线：`main = wip = 198f1a6e`；每个 checkpoint 单独 commit；失败用 `jj undo` / `jj restore`，
  不 force push、不改写已推历史。
- 回滚点：`198f1a6e`（v1.2.1 线上）；C1–C7 任一步失败，`jj bookmark set wip -r <上一 checkpoint>` 后重来。
- 版本：1.3.0；C1–C6 **不 bump** `BundleVersion`（保持 1.2.1，中间构建不入库）；C7 统一改为
  `BuildScript.BundleVersion = "1.3.0"`、`web/index.html` 5 处 `?v=1.3.0`，`tools/check-version.mjs` PASS。
- **不部署** Netlify、**不发** GitHub Release、**不动** `main`；内容构建只进 wip，等策划验收后决定。

## 11. 参考 / 文档责任

- 参考来源：`docs/参考拆解.md` §2.6（Brotato Enemies / Danger Levels、20MTD Darkness、b.mphilley Enemy Designs、
  From The Downtown、Vampire Survivors Results、擦身案例仅参考未采用）。
- **绕行来源不阻塞（已放行）**：§2.6 的 Brotato/20MTD 敌人分工与“前摇可躲”原则够用；C4 按现有推导先做，
  策划后续在 §2.6 补一条侧翼/包抄来源。`docs/参考拆解.md` 是策划 owner，程序不直接编辑。
- 文档 owner：
  - 程序改：`实现总览`、`architecture`、`bridge-protocol`、`acceptance`、`遗留问题`、`README`、
    `docs/M6-内容扩展-执行记录.md`；
  - 策划改：`docs/tickets/M6-内容扩展.md`（计分/上限/击杀分作废）、`docs/参考拆解.md`（绕行来源）、
    `docs/项目规范.md` §6.1（击杀分 ×20 作废）、`docs/文档地图.md`（新增本计划/执行记录索引）。
- 本计划只落 `docs/proposals/`，不改上述 owner 文件。
