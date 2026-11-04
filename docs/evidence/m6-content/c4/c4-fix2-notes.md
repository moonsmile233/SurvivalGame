# C4.2 — 同轮廓独立 Sprite + Charger 绝对冲刺速度

> 日期：2026-11-04 · 状态：**已构建/探针/40 只回归验证**
> 触发：用户第二轮试玩——三种敌人形状应一致、只换颜色，且要用不同 Sprite；Charger 冲刺要
> 简单 4.0 / 普通 4.5 / 困难 5.0。
> 构建：`builtAt 2026-11-04T12:10:19Z`（内部 1.2.1，未入库/未部署）；留证
> `D:/unityFile/_builds/SurvivalGame-1.2.1-c4-fix2-2026-11-04/`。
> 用户看片截图：`review/m6-c4-fix2/`；技术证据：本目录 `fix2/`。

## 1. 外观：同圆形轮廓，颜色烤进 PNG

| 文件 | 颜色 | 用途 |
|---|---|---|
| `spr_enemy.png`（未改） | 粉 | Chaser 基图 |
| `spr_enemy_charger.png` | 红 `(217,58,58)` + 浅红内芯 | Charger 基图 / 冲刺 |
| `spr_enemy_charger_windup.png` | 黄 `(230,194,46)` + 浅黄内芯 | Charger 前摇专用 |
| `spr_enemy_flanker.png` | 紫 `(138,74,217)` + 浅紫内芯 | Flanker 基图 |

- 四张图与 `spr_enemy.png` **同一圆形轮廓**（半径 13.5 / 内芯 6.5），只有颜色不同。
- `_tint` 全部回到白色（Chaser/Charger/Flanker），颜色不再靠 SpriteRenderer.color 相乘。
- `EnemyDefinition` 新增 `_chargeWindupSprite`；Windup 期间 `EnemyAI` 换黄色图（无该图时回退旧 tint 方案），
  Dash 用红色基图 + 1.15 缩放，其余状态恢复基图。
- 三份 def 指向不同 GUID，`_chargeWindupSprite` 只在 Charger 上挂图。
- 截图：`fix2/probe/charger-normal/charger-windup-00.png`（黄圆）、`charger-dash-01.png`（红圆）、
  `fix2/probe/flanker/flanker-flank-side-01.png`（紫圆）。

## 2. Charger：难度档案绝对速度

- `DifficultyProfile` 新增 `_chargeSpeed`（绝对速度）：Easy 4.0 / Normal 4.5 / Hard 5.0；
  **不再乘 `EnemySpeedMultiplier`**，`EnemyDefinition._chargeSpeed`（4.5）只作 fallback。
- 距离保持 7.0u，时长 = 距离 / 实际速度：**1.75 / 1.56 / 1.40s**。
- 其他不变：0.45s 前摇、锁向、一次冲刺一次伤害、撞边界结束。

### 实测（headless 单 Charger）

| 难度 | speed | duration | 实际距离 |
|---|---|---|---|
| Easy | 4.00 | 1.75s | 7.01 |
| Normal | 4.50 | 1.56s | 7.00 / 7.01 |
| Hard | 5.00 | 1.40s | 7.01 |

- 证据：`fix2/probe/charger-{easy,normal,hard}/charger-console.log`。

## 3. 40 只回归（默认 Chaser，headed 离屏 90s）

| FPS | p95 | p99 | alloc KB/s | 到顶 s |
|---|---|---|---|---|
| 165 | 7 | 7 | 41.2 | 49.45 |

- 与 C3/C4/C4.1 一致，无回退；alloc 仍 ~41KB/s 待 C5 归因。
- 证据：`fix2/perf/perf-local-chrome-desktop-cap40-c4-fix2-40.json`。

## 4. 收口

- 提交为 C4.2 新提交（父 `c03ea196`），只推 wip。
- 构建临时放 `web/unity` 供用户验收；验收后 restore 回 1.2.1。
- C5 等用户接受后才放行。
