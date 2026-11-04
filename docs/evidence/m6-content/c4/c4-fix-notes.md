# C4.1 — 敌人外观与 Charger 冲刺修正

> 日期：2026-11-04 · 状态：**已构建/探针/40 只回归验证**
> 触发：用户亲手试玩 C4 不通过——三种敌人像同一张图换色；Charger 冲刺太短、不够快。
> 构建：`builtAt 2026-11-04T11:40:50Z`（内部 1.2.1，未入库/未部署）；留证
> `D:/unityFile/_builds/SurvivalGame-1.2.1-c4-fix-2026-11-04/`（构建后另行复制）。
> 用户看片截图：`review/m6-c4-fix/`；技术证据：本目录 `fix/`。

## 1. 敌人外观

新增两枚 32×32、透明底、PPU 32、Point、Uncompressed、FullRect 的独立 sprite：

| 文件 | 形状 | 底色 | `_tint` |
|---|---|---|---|
| `Assets/Sprites/spr_enemy_charger.png` | 向上箭头（宽三角头 + 短柄 + 两侧小翼） | 白 + 浅灰内芯 | 红 `(1, 0.3, 0.3)` |
| `Assets/Sprites/spr_enemy_flanker.png` | 四角飞镖（菱形 + 内凹边） | 白 + 浅灰内芯 | 紫 `(0.72, 0.4, 1)` |

- `SceneSetup` 新增 `CreateChargerTexture` / `CreateFlankerTexture`，由 `Survival/Setup Scene` 生成并配置导入；
  `EnsureEnemyDefinitions` 现在按 kind 传各自 sprite。
- 三份 `def_Enemy_*.asset` 已真正指向不同 GUID：Chaser `234ce635…`、Charger `b89fc95c…`、Flanker `6ad94be4…`。
- sprite 保持白色主体，`SpriteRenderer.color` 仍可把 Charger 前摇染黄 `(1,0.85,0.2)`、冲刺染红 `(1,0.1,0.05)`；
  截图已验证两种状态都清晰可读。
- 碰撞半径 0.38、停距、视觉缩放逻辑不变。

## 2. Charger 冲刺

- `EnemyDefinition`：`_chargeDurationSeconds` 删除，改为 `_chargeDistance = 7.0`；`_chargeSpeed` 默认 5 → **6.0**。
- `EnemyAI`：`_chargeDurationSeconds = _chargeDistance / (ChargeSpeed × 难度速度倍率)`，不再写死 0.45s。
- 保留：锁向、撞边界提前结束、一次冲刺最多一次伤害、0.45s 前摇、0.8s 恢复、1.5s 冷却。
- 触发距离仍为 **2.8**（本轮未提到 3.5~4.0；实测 7u 冲刺从 2.8 起手能稳定穿过玩家）。
- 新增探针日志：`dash-start`（speed/duration/planned）与 `dash-end`（实际 dist/elapsed）。
- `PerfProbe` 新增 `?difficulty=easy|normal|hard`（perf-only），用于三档验证。

### 实测（headless 单 Charger，每档多次冲刺）

| 难度 | speed | duration | 实际冲刺距离 |
|---|---|---|---|
| Easy | 5.40 | 1.30s | 7.01 / 7.03 |
| Normal | 6.00 | 1.17s | 7.03 / 7.02 / 7.02 / 7.01 |
| Hard | 6.60 | 1.06s | 7.01 / 7.04 / 7.02 |

- 三档全部 **≥6.75**（配置 7.0），三档速度都 > 玩家 3.2。
- 证据：`fix/probe/charger-{easy,normal,hard}/charger-console.log` + 截图。

## 3. Flanker 外观

- 新紫色飞镖 sprite；侧绕/扑截图见 `fix/probe/flanker/` 与 `review/m6-c4-fix/flanker/`。
- 行为未改（沿用 C4 的固定参考方位与到点闩锁）。

## 4. 40 只回归（默认 Chaser，headed 离屏 90s）

| 场景 | FPS | p95 | p99 | alloc KB/s | 到顶 s |
|---|---|---|---|---|---|
| C4.1 cap40 | 165 | 7 | 7 | 41.4 | 49.46 |

- 与 C3/C4 一致，无回退；alloc 仍 ~41KB/s 待 C5 归因。
- 证据：`fix/perf/perf-local-chrome-desktop-cap40-c4-fix-40.json`。

## 5. 收口

- `web/unity` 已 restore 回 1.2.1（提交后临时放新构建供用户验收，见交接）。
- 未改 C4 提交 `83dde389`；C4.1 为新提交。
- 用户验收通过后才进 C5。
