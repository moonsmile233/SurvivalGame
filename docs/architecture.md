# 架构说明（M2）

> 对应规范第 5、6、7、11 节与 `docs/tickets/M2.md`。代码为唯一事实来源，本文只做导航。

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
- `Paused` / `Won` / `Lost` 三者都统一 `Time.timeScale = 0`；`ResetRound` 里先恢复 1。
- 玩法逻辑全部用 `Time.deltaTime`，暂停 / 结算时自然冻结；UI 用 uGUI，不受 timeScale 影响。
- 重开不走 `SceneManager.LoadScene`，而是 `GameManager.RestartGame()` → 各系统 `Reset()`。
- M2 仍保留 `GameManager._autoStart = true`（Play 即开局）方便自测；M3 接 H5 时关闭。

## 2. 模块职责

| 模块 | 文件 | 职责 | 不做什么 |
|---|---|---|---|
| 配置 | `Core/GameConfig.cs` | ScriptableObject，所有可调数值 | 不写逻辑 |
| 状态 | `Core/GameState.cs` | 状态枚举 | — |
| 状态机 | `Core/GameManager.cs` | 计时、胜负、暂停、重开、冻结、事件广播 | 不处理输入、不写分数算法、不碰 UI 文本 |
| 计分 | `Core/ScoreService.cs` | 存活分 / 击杀分 / 胜利奖励，并提供只读明细 | 不碰 UI |
| 输入 | `Player/PlayerInputController.cs` | 键盘 → `Vector2 Move` | 不直接移动 |
| 移动 | `Player/PlayerController.cs` | 刚体移动、相机边界钳制 | 不改状态机 |
| 生命 | `Player/PlayerHealth.cs` | HP、受伤、无敌帧、`OnDamaged` / `OnDied` 事件 | 不直接调 UI |
| 表现 | `Player/PlayerView.cs` | 订阅 `OnDamaged` / `OnHealthChanged`，做红闪 + 10Hz 闪烁 | 不改 HP、不改状态 |
| 生成 | `Enemy/EnemySpawner.cs` | 定时生成、间隔递减、上限、清场 | 不追踪玩家 |
| 敌人 | `Enemy/EnemyAI.cs` | 追踪玩家、接触伤害 | 不判断胜负 |
| HUD | `UI/HudView.cs` | HP 条/数字、剩余时间、分数、暂停按钮显隐 | 不改状态机、不写分数算法 |
| 暂停面板 | `UI/PausePanel.cs` | 按状态显隐，`继续` → `ResumeGame()` | 不直接改 `timeScale` |
| 结算面板 | `UI/ResultPanel.cs` | 按状态显隐，胜负/得分/明细，`重新开始` → `RestartGame()` | 不写分数算法 |
| 文案 | `UI/UiStrings.cs` | 全部中文 UI 文案的 const + `AllCharacters` | 不写逻辑 |
| 桥接 | `Bridge/WebGLBridge.cs` | H5 ↔ Unity 边界转换 | 不写玩法逻辑 |
| 场景生成 | `Editor/SceneSetup.cs` | 一键生成 `Game.unity`（含整套 UI） | — |
| 字体 | `Editor/FontAssetBuilder.cs` | 用脚本生成 TMP 字体图集 | 不写玩法 |
| 构建 | `Editor/BuildScript.cs` | 构建前重建字体 + 场景，产出 `web/unity/` 与 `build-info.json` | — |

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
PlayerHealth：无敌 1s 内忽略伤害；真正扣血时 OnDamaged → PlayerView 红闪/闪烁

PlayerHealth.OnDied ─▶ GameManager.HandlePlayerDied ─▶ Lost（冻结）
GameManager.Update ─▶ 计时 + ScoreService.SetSurvivedSeconds + OnTimeChanged
计时 ≥ 60 ─▶ Won（+200 奖励，冻结）

GameManager.OnStateChanged ─▶ HudView（暂停按钮显隐）
                            ├▶ PausePanel（Paused 时显示「已暂停」）
                            └▶ ResultPanel（Won/Lost 时显示「胜利 / 失败」+ 明细）
```

事件清单（全部在 `Start` 订阅、`OnDestroy` 退订）：

```csharp
GameManager.OnStateChanged   // (GameState)
GameManager.OnTimeChanged    // (float remainingSeconds)
ScoreService.OnScoreChanged  // (int score)
PlayerHealth.OnHealthChanged // (int current, int max)
PlayerHealth.OnDamaged       // ()  仅真正扣血
PlayerHealth.OnDied          // ()
```

## 4. UI 层约定

- **文案唯一出处** `UI/UiStrings.cs`；游戏内不允许出现散落的字符串字面量。
- **字体**：TMP + `Assets/Fonts/NotoSansSC-UI-SDF.asset`（由 `ui-chars.txt` 裁的 121 字符，1024×1024 Static SDF，Sampling 64 / Padding 8 / SDFAA）。
  - 禁止 legacy `Text` + `LegacyRuntime.ttf`（WebGL 下中文字体的坑）。
  - 图集由 `Editor/FontAssetBuilder.cs` 生成：`Survival/Rebuild Font Asset` 手动；`Setup Scene` 缺资产时自动；`Build WebGL` 构建前无条件重烤。
- **显示规则**：剩余时间 `ceil(60 - elapsed)` → `时间 00:60 ~ 00:00`；分数 `分数 N`；结算 `得分 N` + `存活 X`，击杀为 0 时不显示「击杀」行，`胜利奖励` 仅胜利显示。
- **性能**：时间文本只在整秒变化时重建；分数 / HP 只在事件回调重建；`Update` 里不做字符串拼接、不 `GetComponent` / `Find`。

## 5. 关键决策

- **静态结构在场景、动态物体在代码**：玩家/相机/背景/生成器/管理器/整套 UI 由 `SceneSetup` 生成；敌人只由 `EnemySpawner` 运行时创建（CLAUDE.md 硬约束 1）。
- **场景可重建**：`Survival/Setup Scene` 一键生成；`BuildScript` 每次构建前也强制重建一次，保证「代码即场景」。
  - 注意：`EditorSceneManager.NewScene` 会卸载未被场景引用的资源，`GameConfig` / Sprite / TMP Font Asset 都必须在 `NewScene` 之后重新 `LoadAssetAtPath` 再赋值。
- **UI 用 TMP 而非 legacy Text**：中文字形在 WebGL 下 legacy Text 只有内置 Arial/LegacyRuntime，没有 CJK；TMP 走 SDF 图集，缩放到 4K 也清晰。
- **字体图集脚本化**：Font Asset Creator 是 GUI 操作、不可复现；改用 `TMP_FontAsset.CreateFontAsset` + `TryAddCharacters` 生成静态图集，并在构建前重烤，避免「改了文案忘了重烤 → tofu」。
- **敌人不做对象池**：上限 20，直接 Instantiate/Destroy，保持简单。
- **接触判定**：敌人是 Kinematic Rigidbody2D + Trigger Collider，玩家是 Dynamic Rigidbody2D，`OnTriggerStay2D` 触发；无敌帧由 `PlayerHealth` 内部处理。
- **输入后端**：`activeInputHandler = 2`（Both），规范 5.5 用内置 `Input`，同时保留新 Input System 可用性。
- **暂停只由 UI / H5 触发**：M1 的 `Esc` 调试键已在 M2 删除。

## 6. 数值一览（`GameConfig`）

| 参数 | 值 |
|---|---|
| 玩家 HP / 速度 / 无敌 | 100 / 3.2 / 1.0s |
| 敌人速度 / 接触伤害 | 1.8 / 12 |
| 生成间隔 / 每 10s 递减 / 下限 / 上限 | 1.5s / 0.15s / 0.5s / 20 |
| 胜利时间 | 60s |
| 存活分 / 击杀分 / 胜利奖励 | 10/s / 20/个 / 200 |

> 仍无击杀手段，击杀分接口保留给后续里程碑；结算面板因此不显示「击杀」行。
