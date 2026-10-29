# 架构说明（M1）

> 对应规范第 5、6 节与 `docs/tickets/M1.md`。代码为唯一事实来源，本文只做导航。

## 1. 状态机

```text
Booting ──初始化完成──▶ Ready ──StartGame──▶ Playing
                                              │  ▲
                                  PauseGame   │  │  ResumeGame
                                              ▼  │
                                            Paused
Playing ──HP ≤ 0──▶ Lost ──RestartGame──▶ Playing（全量重置）
Playing ──计时 ≥ 60s──▶ Won ──RestartGame──▶ Playing（全量重置）
```

- 只有 `GameManager` 能改状态（`SetState` 私有）。
- 暂停统一 `Time.timeScale = 0`；恢复置 1。玩法逻辑全部用 `Time.deltaTime`，暂停时自然冻结。
- 重开不走 `SceneManager.LoadScene`，而是 `GameManager.RestartGame()` → 各系统 `Reset()`。
- M1 的 `GameManager._autoStart = true` 让 Play 后自动进入 Playing；M3 接 H5 时关闭，改由 `StartGame` 驱动。

## 2. 模块职责

| 模块 | 文件 | 职责 | 不做什么 |
|---|---|---|---|
| 配置 | `Core/GameConfig.cs` | ScriptableObject，所有可调数值 | 不写逻辑 |
| 状态 | `Core/GameState.cs` | 状态枚举 | — |
| 状态机 | `Core/GameManager.cs` | 计时、胜负、暂停、重开、事件广播 | 不处理输入、不写分数算法 |
| 计分 | `Core/ScoreService.cs` | 存活分 + 击杀分 + 胜利奖励 | 不碰 UI |
| 输入 | `Player/PlayerInputController.cs` | 键盘 → `Vector2 Move` | 不直接移动 |
| 移动 | `Player/PlayerController.cs` | 刚体移动、相机边界钳制 | 不改状态机 |
| 生命 | `Player/PlayerHealth.cs` | HP、受伤、无敌帧、死亡事件 | 不直接调 UI |
| 生成 | `Enemy/EnemySpawner.cs` | 定时生成、间隔递减、上限、清场 | 不追踪玩家 |
| 敌人 | `Enemy/EnemyAI.cs` | 追踪玩家、接触伤害 | 不判断胜负 |

## 3. 事件与数据流

```text
键盘 Input ─▶ PlayerInputController.Move
                    │
                    ▼
             PlayerController.FixedUpdate ─▶ Rigidbody2D（边界钳制）

EnemySpawner.Update（Time.deltaTime）
   ├─ 定时在相机视口 ±0.15 外生成 EnemyAI
   └─ 每隔 10s 间隔 −0.15s，下限 0.5s，上限 20 只

EnemyAI.Update ─▶ MoveTowards(player)
EnemyAI.OnTriggerStay2D ─▶ PlayerHealth.TakeDamage(12)
PlayerHealth：无敌 1s 内忽略伤害；HP ≤ 0 → OnDied

PlayerHealth.OnDied ─▶ GameManager.HandlePlayerDied ─▶ Lost
GameManager.Update ─▶ 计时 + ScoreService.SetSurvivedSeconds + OnTimeChanged
计时 ≥ 60 ─▶ Won（+200 奖励）
```

对外事件（M2/M3 订阅，不反向调用）：

```csharp
GameManager.OnStateChanged   // (GameState)
GameManager.OnTimeChanged    // (float remainingSeconds)
ScoreService.OnScoreChanged  // (int score)
PlayerHealth.OnHealthChanged // (int current, int max)
PlayerHealth.OnDied          // ()
```

## 4. 关键决策

- **静态结构在场景、动态物体在代码**：玩家/相机/背景/生成器/管理器由 `SceneSetup` 生成；敌人只由 `EnemySpawner` 运行时创建（CLAUDE.md 硬约束 1）。
- **场景可重建**：`Survival/Setup Scene` 一键生成 `Game.unity`，并补齐 `Assets/Configs/cfg_GameConfig.asset` 与 `Assets/Sprites/*.png` 占位图。
  - 注意：`EditorSceneManager.NewScene` 会卸载未被场景引用的资源，`GameConfig` 必须在 `NewScene` 之后重新 `LoadAssetAtPath` 再赋值，否则场景里是空引用。
- **敌人不做对象池**：上限 20，直接 Instantiate/Destroy，保持简单。
- **接触判定**：敌人是 Kinematic Rigidbody2D + Trigger Collider，玩家是 Dynamic Rigidbody2D，`OnTriggerStay2D` 触发；无敌帧由 `PlayerHealth` 内部处理。
- **输入后端**：模板默认 `activeInputHandler = 1`（仅新 Input System）。M1 按规范 5.5 用内置 `Input`，已改为 `2`（Both），同时保留模板的新 Input System 可用性。
- **暂停调试键**：M1 临时用 `Esc` 切换暂停，M2 起由暂停面板 / H5 接管，届时删除该快捷键。

## 5. 数值一览（`GameConfig`）

| 参数 | 值 |
|---|---|
| 玩家 HP / 速度 / 无敌 | 100 / 3.2 / 1.0s |
| 敌人速度 / 接触伤害 | 1.8 / 12 |
| 生成间隔 / 每 10s 递减 / 下限 / 上限 | 1.5s / 0.15s / 0.5s / 20 |
| 胜利时间 | 60s |
| 存活分 / 击杀分 / 胜利奖励 | 10/s / 20/个 / 200 |

> M1 无击杀手段，击杀分接口保留给后续里程碑；`ScoreService.AddKill()` 暂无人调用。
