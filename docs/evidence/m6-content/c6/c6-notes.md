# C5.3 + C6 — 触发距离/绕行解锁微调 + 计分重写/HUD/结算/协议/字体

> 日期：2026-11-07 · 状态：**已实装、已构建（本地验证）、E2E 全绿；待用户验收**
> 构建（中间构建，**不入 `web/unity`**）：`builtAt 2026-11-07T08:41:44Z`，9 MB（字体 75 码位）；
> 留证 `D:/unityFile/_builds/SurvivalGame-1.2.1-c6-2026-11-07/`；
> 预览副本 `D:/unityFile/_preview/c6`，服务 `http://localhost:8000`（H5）、`http://localhost:8000/unity/index.html`（standalone）。
> `web/unity` 已 restore 回 1.2.1（`builtAt 2026-11-02T13:05:32Z`），`check-version` PASS。

## 1. C5.3 改动（与 C6 同一个构建）

| 文件 | 改动 |
|---|---|
| `Assets/Configs/dif_Easy/Normal/Hard.asset` | `_flankerUnlockSeconds` 45/35/25 → **0** |
| `Assets/Configs/def_Enemy_Charger.asset` + `EnemyDefinition.cs` 默认值 + `SceneSetup.cs` | `_chargeTriggerDistance` 4.2 → **7.0**（冲刺速度/距离/前摇/伤害/穿阵不变） |

**Editor 反射验证（真实 `RefillSpawnBag`，非重写逻辑）：**

```text
dif_Easy: chargerUnlock=25 flankerUnlock=0 limit=14
dif_Normal: chargerUnlock=15 flankerUnlock=0 limit=30
dif_Hard: chargerUnlock=10 flankerUnlock=0 limit=50
charger trigger=7 windup=0.45 dist=7 speed(default)=4.5

easy   t=0  bag: chaser=3 flanker=2 charger=0    easy   t=25 bag: chaser=2 flanker=2 charger=1
normal t=0  bag: chaser=3 flanker=2 charger=0    normal t=15 bag: chaser=2 flanker=2 charger=1
hard   t=0  bag: chaser=3 flanker=2 charger=0    hard   t=10 bag: chaser=2 flanker=2 charger=1
```

- 5-tick 袋仍是 **2 普通 + 2 绕行 + 1 冲锋**；Charger 未解锁时它的袋位用普通补位，
  所以开局每 5 tick 是 **3 普通 / 2 绕行 / 0 冲锋**，Charger 解锁后 **2/2/1**。
  （策划口径里的“开局普通/绕行各 2”按“Charger 位补普通”展开后是 3/2；如要严格 2/2/0 需改成 4-tick 袋或丢弃该位，属于设计变更。）

## 2. C6 改动

| 文件 | 改动 |
|---|---|
| `Core/GameConfig.cs` | 新增 `_survivalSegmentSeconds=15`、`_survivalRatesPerSecond=[10,12,15,20]`、`_scorePerHealthPoint=1`；删除 `_scorePerSurvivedSecond/_scorePerKill` 的公式用途 |
| `Core/ScoreService.cs` | 重写：`SurvivalRawFor(elapsed)` 纯函数、`Advance/Finish`、`DisplayScore/SurvivalScore/HealthScore/FinalScore`、`Kills=0` |
| `Core/GameManager.cs` | `Playing` 每帧 `Score.Advance(_elapsed)`；`FinishRound` 调 `Score.Finish(elapsed, hp, won)`；胜利 elapsed 固定回合时长；暴露 `Health` |
| `Player/PlayerHealth.cs` | 新增 `HitCount`（真正扣血 +1，`ResetHealth` 清零） |
| `UI/HudView.cs` | 分数显示 `DisplayScore`（存活分×倍率，整数） |
| `UI/ResultPanel.cs` + `Editor/SceneSetup.cs` | 最终分 + 明细行「存活分 · 血量分 · 受击」；新增 `BreakdownText` 与 `_playerHealth` 引用 |
| `UI/UiStrings.cs` | 新增 `存活分 / 血量分 / 受击` 并加入 `AllCharacters` |
| `Bridge/WebGLBridge.cs` | `payload.hitCount` + `breakdown.{health,multiplier}`（协议仍 3） |
| `Editor/ScoreSelfCheck.cs` | 新增：分段/参考分/粒度/Reset/血量钳制自检（菜单 + batchmode） |
| `Assets/Fonts/ui-chars.txt` + `NotoSansSC-UI-Subset.otf` | 71 → 75 码位（新增 血/量/受/击），从完整 Noto Sans SC 重裁 |
| `Assets/Configs/cfg_GameConfig.asset` | 写入新计分字段 |
| `tools/e2e/standalone-menu.mjs` / `h5-shell.mjs` | hard 2s 胜利 232 → **392**，URL 加 `invincible=1` |
| `tools/e2e/smoke.mjs` | 注释补充 5s 普通满血理论值 395（仍只断言格式与正数） |
| `tools/e2e/score.mjs` | 新增：easy 60 / normal 2/15/30/45/60 / hard 60 七局分值断言 |

### 2.1 浮点倍率修正

资产里的 `1.3f` 实际是 `1.29999995…`，直接相乘会把 `655 × 1.3` 算成 `851.4999` → 取整 851，
最终分比参考表少 1（45s 普通应 1052，实测 1051）。`ScoreService` 先把倍率规整成十进制
（`Math.Round((double)multiplier, 3)`）再算，并统一 `.5` 向上；之后 45s = 1052、60s = 1442。

## 3. 计分自检（Editor）

`Survival/Score Self Check` → **PASS**：分段 0/15/30/45/60 = 0/150/330/555/855；
三档 × 2/5/15/30/60 秒参考分全部命中；`Advance` 7.3s=73、7.35s 不触发、7.4s=74；
回退 Advance 不累加；Reset 清零；血量钳到 `PlayerMaxHealth`。

## 4. E2E（build `2026-11-07T08:41:44Z`）

### 4.1 `score.mjs`（7 局，全绿）

| 局 | score | breakdown.survival / health | multiplier |
|---|---|---|---|
| easy 60s | **1155** | 855 / 100 | 1.0 |
| normal 2s | **356** | 26 / 130 | 1.3 |
| normal 15s | **525** | 195 / 130 | 1.3 |
| normal 30s | **759** | 429 / 130 | 1.3 |
| normal 45s | **1052** | 722 / 130 | 1.3 |
| normal 60s | **1442** | 1112 / 130 | 1.3 |
| hard 60s | **1728** | 1368 / 160 | 1.6 |

每局 `result=win`、`kills=0`、`hitCount=0`（invincible）、`winBonus=200`。
证据：`score-e2e-summary.json`。

### 4.2 其他

- `standalone-menu.mjs`（flow）：hard 2s 胜利 **392**；菜单/暂停/重开/结算/再来一局/返回菜单全链路 PASS。
- `h5-shell.mjs`：core / protocol mismatch / migration / fullscreen 全 PASS；best 与 `#data-last` = **困难 392**；
  注入低分 100 不覆盖。
- `smoke.mjs`：9 项 PASS；最近一局「普通 395」（5s 满血理论值），刷新保留最高分。
- 6 视口：standalone 320/390/768/844/1280/1920 与 h5-shell 6 视口全 PASS，无横向溢出。
- 结算布局：320×568 与 1280×720 截图见 `standalone-result-*.png`；明细行在 320 宽不溢出。
- HUD 0.1s 粒度：`review/m6-c6/hud-3.3s-score-33.png`（分数 33）、`hud-later-score-37.png`（37），
  不是整数秒 ×10 跳字。

## 5. 已知问题 / 遗留

- **trial 水印仍在**：本构建截图右下角有 `trial version`。C7 必须先让用户确认 `Help > Manage License`
  为 Unity Personal 干净授权，再重建；仍带水印就停（沿用 C5 交接 §3）。
- H5 未改代码；`protocol` 仍 3；最高分仍单一共享键，不迁移、不清空。
- 中间构建、场景、TMP 字体资产都未入库；`docs/evidence/m6-refactor/*` 的 E2E 覆盖已 restore，
  C6 证据只保留本目录。
- 本构建只用于用户验收，1.2.1 线上不动；C7 才升 1.3.0 并入 `web/unity`。
