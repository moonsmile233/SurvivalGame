# 架构说明（v1.2 游戏优先）

> 对应规范第 5、6、7、8、9、11 节；v1.2 决策见 `docs/proposals/proposal-M6-重构-游戏优先-v1.2.md`。
> 代码为唯一事实来源，本文只做导航。通信协议细节见 `docs/bridge-protocol.md`。

## 1. 状态机

```text
Booting ──初始化──▶ Menu ──菜单开始──▶ Starting(3s) ──▶ Playing
Paused ──继续──▶ Starting(3s) ──▶ Playing（不重置本局）
Paused ──重新开始──▶ Starting(3s) ──▶ Playing（倒计时结束才 ResetRound）
Result ──再来一局──▶ Starting(3s) ──▶ Playing（同档，倒计时结束才 ResetRound）
Playing ⇄ Paused（暂停按钮 / Esc / P / 失焦）
Playing ──HP≤0 / 60s──▶ Result
Starting ──失焦取消──▶ 回来源态（Menu / Paused / Result）
Paused/Result ──返回菜单──▶ Menu
```

- 只有 `GameManager` 能改状态（`SetState` 私有）。
- `Starting` / `Paused` / `Result` 统一 `Time.timeScale = 0`；`ResetRound` 里先恢复 1。
  取消倒计时回来源态要复位：Menu=1、Paused/Result=0。
- 玩法逻辑全部用 `Time.deltaTime`，暂停/结算/倒计时时自然冻结；UI 用 uGUI，不受 timeScale 影响。
- 重开不走 `SceneManager.LoadScene`，而是 `GameManager.RestartGame()` → 倒计时结束 → 各系统 `Reset()`。
- **v1.2 起 Unity 拥有完整游戏流程**：默认 `Menu`，难度/开局/暂停/重开/返回/结算都在画布里；
  不依赖 H5 消息，Standalone 可直接玩。`_autoStart` 仍默认 false（自测可开）。
- **统一 3-2-1**：所有进入 `Playing` 的路径都经 `Starting`（`GameManager` 持有 unscaled 计时，
  `StartCountdownPanel` 只显示）。`Starting` 不发 `onStateChange`、不显示暂停/结算面板。
- **失焦保持暂停 + 玩家主动恢复才倒数**：`ApplicationFocusPause` 处理 `OnApplicationFocus/Pause`；
  旧 H5 的 `SetPausedByVisibility` 保留为兜底（幂等）。切回**保持 Paused**，不自动恢复。
  倒计时期间失焦 → 取消并回来源态，切回不自动继续。
- **结算只发一次**：`WebGLBridge._reported` 守卫，每次进入 `Playing` 清零。
- **结算只画一套**：v1.2 删除 `IsEmbedded`，`ResultPanel` 在 WebGL 里也显示；H5 只更新数据面板。

## 2. 模块职责

| 模块 | 文件 | 职责 | 不做什么 |
|---|---|---|---|
| 配置 | `Core/GameConfig.cs` | ScriptableObject，所有可调数值 | 不写逻辑 |
| 状态 | `Core/GameState.cs` | 状态枚举 | — |
| 状态机 | `Core/GameManager.cs` | 计时、胜负、暂停、重开、冻结、事件广播 | 不处理输入、不写分数算法、不碰 UI 文本 |
| 暂停快捷键 | `Core/PauseHotkey.cs` | 桌面 Esc/P：Playing → `PauseGame()`；Paused → `RequestResumeCountdown()`（3-2-1）；Starting 忽略 | 不改 timeScale、不碰 UI、不处理移动 |
| 失焦暂停 | `Core/ApplicationFocusPause.cs` | 失去焦点：Playing → Paused、Starting → 取消倒计时；Editor 内跳过 | 不自动恢复、不碰 UI |
| 计分 | `Core/ScoreService.cs` | 存活分 / 击杀分 / 胜利奖励，并提供只读明细 | 不碰 UI |
| 输入抽象 | `Player/PlayerInputSource.cs` | 抽象 `Vector2 Move`，PlayerController 只依赖它 | 不移动 |
| 键盘 | `Player/KeyboardInput.cs` | WASD / 方向键 → `Move` | 不移动 |
| 触控 | `Player/TouchInput.cs` | 全屏拖动（触摸优先，鼠标拖动兼容 Editor/桌面）→ `Move` | 不移动 |
| 输入路由 | `Player/PlayerInputRouter.cs` | 触控有输入就用触控，否则键盘；设备判断集中在这里 | 不做平台分支散布到其它类 |
| 移动 | `Player/PlayerController.cs` | 刚体移动、**地图边界**钳制（36×20，不再用相机视野） | 不改状态机 |
| 相机 | `World/CameraFollow.cs` | `LateUpdate` 指数平滑跟随玩家，钳制在地图内；重开距离 >8 直接 snap | 不读输入、不改状态 |
| 生命 | `Player/PlayerHealth.cs` | HP、受伤、无敌帧、`OnDamaged` / `OnDied` 事件 | 不直接调 UI |
| 表现 | `Player/PlayerView.cs` | 订阅 `OnDamaged` / `OnHealthChanged`，做红闪 + 10Hz 闪烁 | 不改 HP、不改状态 |
| 生成 | `Enemy/EnemySpawner.cs` | 按 `EnemyDefinition` 生成（玩家周围 1.15×相机半对角线、视野外）、间隔递减、档位上限、邻居网格 30Hz、`Register/Unregister/Forget` | 不追踪玩家、不做两两位置修正 |
| 敌人 | `Enemy/EnemyAI.cs` | 只 seek「玩家 + 停距 0.70」+ 软墙/切向让位 + 压力曲线 + 视野外回收计时、接触伤害、`OnDestroy` 兜底注销 | 不做分离力、不判断胜负、不钉住阵型 |
| 敌人定义 | `Enemy/EnemyDefinition.cs` | 单类型资产：sprite / 速度 / 伤害 / 碰撞半径（分离半径派生） | 不管全局生成节奏 |
| HUD | `UI/HudView.cs` | HP 条/数字、剩余时间、分数、暂停按钮显隐；Menu/Result 隐藏整个 HUD | 不改状态机、不写分数算法 |
| HUD 缩放 | `UI/HudCanvasScaler.cs` | `Screen.width`（设备像素）< 1000 时切 640×360 参考分辨率（手机 HUD/面板一起放大），`Update` 里比对 | 不改游戏状态 |
| 主菜单 | `UI/MenuPanel.cs` | Menu 状态显示；三档卡选择 + 开始；只发 `StartGame`，不自己改状态 | 不写最高分/昵称/全屏 |
| 暂停面板 | `UI/PausePanel.cs` | Paused 显示；继续 → `RequestResumeCountdown()`、重新开始 → `RestartGame()`、返回菜单 → `ReturnToMenu()` | 不计时（3-2-1 在 GameManager）、不碰 timeScale |
| 倒计时面板 | `UI/StartCountdownPanel.cs` | Starting 显示遮罩 + 数字，只读 `GameManager.CountdownRemaining/OnCountdownChanged` | 不自己计时、不改状态 |
| 结算面板 | `UI/ResultPanel.cs` | Result 显示；胜负/本局得分/难度/存活/倍率 + 再来一局/返回菜单；始终显示（不再有嵌入抑制） | 不写分数算法、不显示昵称/明细 |
| 文案 | `UI/UiStrings.cs` | 全部中文 UI 文案的 const + `AllCharacters` | 不写逻辑 |
| 桥接 | `Bridge/WebGLBridge.cs` + `.jslib` | protocol 3：EnterGame/昵称、状态（menu/playing/paused/result）、难度偏好、结算转发；旧指令兼容 shim | 不写玩法逻辑，不改状态 |
| H5 外壳（v1.1 旧） | `web/js/bridge.js` | 注册 `window.SurvivalGameBridge`、加载 Unity、转发事件 | 不渲染业务 UI；v1.2 薄壳下一阶段替换 |
| H5 页面（v1.1 旧） | `web/js/app.js` | 旧页面状态机、按钮/昵称/结算渲染、可见性事件 | 与 v1.2 只保证过渡兼容 |
| H5 存储 | `web/js/storage.js` | `survival.*` 读写封装、不可用回退内存 | 业务代码不得直接碰 `localStorage` |
| 场景生成 | `Editor/SceneSetup.cs` | 一键生成 `Game.unity`（含整套 UI） | — |
| 字体 | `Editor/FontAssetBuilder.cs` | 用脚本生成 TMP 字体图集 | 不写玩法 |
| 构建 | `Editor/BuildScript.cs` | 构建前重建字体 + 场景，产出 `web/unity/` 与 `build-info.json` | — |

## 3. 事件与数据流

```text
键盘 Input ─▶ KeyboardInput.Move ─┐
触摸/拖动 ─▶ TouchInput.Move ─────┼▶ PlayerInputRouter.Move
                                  │        │
                                  ▼        ▼
                       PlayerController.FixedUpdate ─▶ Rigidbody2D（地图 36×20 边界钳制）
                                                        │
                                                        ▼
                                     CameraFollow.LateUpdate（指数平滑，钳在地图内）

EnemySpawner.Update（Time.deltaTime）
   ├─ 定时在「玩家周围 1.15×相机半对角线、且相机视野外」生成 EnemyAI（按 EnemyDefinition 组装）
   ├─ 每隔 10s 间隔 −0.15s，下限 0.5s，上限取 DifficultyProfile（简单 14 / 普通·困难 20）
   └─ 邻居网格 30Hz 刷新（0.90 cell、最近 8 个）；移动仍 60Hz

EnemyAI.Update（Time.deltaTime）
   ├─ 只 seek「玩家 + 停距 0.70」，速度 16/s 平滑；对邻居做软墙（0.90→0.70 扣逼近速度，不外推）
   ├─ 被挡住/被顶住时切向让位；玩家穿人群时侧闪；<0.5 防重合兜底用连续加速
   ├─ 速度 = 生成时 ±12% × 压力曲线 1.0→档位终点，上限 2.2
   └─ 离相机视野（margin 0.05）持续 3s → Spawner.Unregister(this)（进入过视野后才计时）

EnemyAI.OnTriggerStay2D ─▶ PlayerHealth.TakeDamage(definition.ContactDamage)
PlayerHealth：无敌 1s 内忽略伤害；真正扣血时 OnDamaged → PlayerView 红闪/闪烁

PlayerHealth.OnDied ─▶ GameManager.HandlePlayerDied ─▶ FinishRound(false) ─▶ Result（冻结）
GameManager.Update ─▶ 计时 + ScoreService.SetSurvivedSeconds + OnTimeChanged
计时 ≥ 60 ─▶ FinishRound(true)（+200 奖励，冻结）

GameManager.OnStateChanged ─▶ HudView（HUD/暂停按钮显隐）
                            ├▶ MenuPanel（Menu 显示）
                            ├▶ PausePanel（Paused 显示）
                            ├▶ StartCountdownPanel（Starting 显示）
                            ├▶ ResultPanel（Result 显示）
                            └▶ WebGLBridge → jslib → onStateChange（Starting 不发）
                                                       └ Result + !_reported → onGameOver（一次）
GameManager.OnMenuDifficultyChanged ─▶ MenuPanel（高亮） + WebGLBridge → onDifficultyChanged
```

H5 ↔ Unity（v1.2，protocol 3）：

```text
H5 点击「开始游戏」
  └─ SendMessage EnterGame({"nickname","lastDifficulty"})
       └─ Bridge 保存昵称/难度偏好（不改状态）；画布内已是 Unity Menu
玩家在 Unity 菜单点开始
  └─ GameManager.StartGame → Starting(3-2-1) → Playing → onStateChange(playing)
玩家在 Unity 菜单点难度卡
  └─ GameManager.SetMenuDifficulty → onDifficultyChanged（H5 代存）
失焦 / 标签页隐藏
  └─ ApplicationFocusPause（或旧页 SetPausedByVisibility）→ Paused；切回保持 Paused
玩家点「继续」/ 按 Esc/P
  └─ RequestResumeCountdown → Starting(3-2-1) → Playing
Unity 结算
  └─ Result + onGameOver(JSON) → H5 更新最高分/最近一局；Unity 画布内显示结算
H5 旧指令 Pause/Resume/Restart/ReturnToMenu
  └─ no-op（一次性 warning）；局内操作全部在 Unity UI
```

事件清单（全部在 `Start` 订阅、`OnDestroy` 退订）：

```csharp
GameManager.OnStateChanged   // (GameState)
GameManager.OnTimeChanged    // (float remainingSeconds)
GameManager.OnCountdownChanged // (int seconds) 仅 Starting
GameManager.OnMenuDifficultyChanged // (Difficulty) 菜单选择变化
ScoreService.OnScoreChanged  // (int score)
PlayerHealth.OnHealthChanged // (int current, int max)
PlayerHealth.OnDamaged       // ()  仅真正扣血
PlayerHealth.OnDied          // ()
```

## 4. UI 层约定

- **文案唯一出处** `UI/UiStrings.cs`；游戏内不允许出现散落的字符串字面量。
- **字体**：TMP + `Assets/Fonts/NotoSansSC-UI-SDF.asset`（由 `ui-chars.txt` 裁的 75 字符，1024×1024 Static SDF，
  Sampling 64 / Padding 8 / SDFAA）。v1.2 新增菜单/结算文案后，用 `tools/font-subset.py` 从完整 Noto Sans SC（OFL）
  重裁同名子集 OTF；完整字体不入库。
  - 禁止 legacy `Text` + `LegacyRuntime.ttf`（WebGL 下中文字体的坑）。
  - 图集由 `Editor/FontAssetBuilder.cs` 生成：`Survival/Rebuild Font Asset` 手动；`Setup Scene` 缺资产时自动；`Build WebGL` 构建前无条件重烤。
- **显示规则**：剩余时间 `ceil(60 - elapsed)` → `时间 01:00 ~ 00:00`；分数 `分数 N`；
  结算卡 = 胜利/失败 + `本局得分 N` + `难度 X · 存活 N 秒 · 倍率 ×X.X`（按 mock，不显示分数明细与昵称）。
- **性能**：时间文本只在整秒变化时重建；分数 / HP 只在事件回调重建；`Update` 里不做字符串拼接、不 `GetComponent` / `Find`。
- **布局（M4）**：HUD 顶行 HP 左 / 时间中 / 暂停右上，分数在暂停按钮下方。
  `HudCanvasScaler` 在 `Screen.width`（设备像素，DPR 已由 H5 限制到 ≤2）< 1000 时把
  `CanvasScaler.referenceResolution` 从 1280×720 切到 640×360，手机竖屏 HUD 与面板一起放大；
  两套分辨率共用同一份锚点布局。判定放在 `Update` 里（等比例缩放不会触发 RectTransform 尺寸回调）。
- **操作提示**：在 H5 DOM 里（`.op-hint`），用 `pointer: coarse` 切「拖动屏幕」/「WASD」，不进 Unity HUD。

## 5. 关键决策

- **静态结构在场景、动态物体在代码**：玩家/相机/背景/生成器/管理器/整套 UI 由 `SceneSetup` 生成；敌人只由 `EnemySpawner` 运行时创建（CLAUDE.md 硬约束 1）。
- **场景可重建**：`Survival/Setup Scene` 一键生成；`BuildScript` 每次构建前也强制重建一次，保证「代码即场景」。
  - 注意：`EditorSceneManager.NewScene` 会卸载未被场景引用的资源，`GameConfig` / Sprite / TMP Font Asset 都必须在 `NewScene` 之后重新 `LoadAssetAtPath` 再赋值。
- **UI 用 TMP 而非 legacy Text**：中文字形在 WebGL 下 legacy Text 只有内置 Arial/LegacyRuntime，没有 CJK；TMP 走 SDF 图集，缩放到 4K 也清晰。
- **字体图集脚本化**：Font Asset Creator 是 GUI 操作、不可复现；改用 `TMP_FontAsset.CreateFontAsset` + `TryAddCharacters` 生成静态图集，并在构建前重烤，避免「改了文案忘了重烤 → tofu」。
- **敌人不做对象池**：上限 20，直接 Instantiate/Destroy，保持简单。
- **接触判定**：敌人是 Kinematic Rigidbody2D + Trigger Collider，玩家是 Dynamic Rigidbody2D，`OnTriggerStay2D` 触发；无敌帧由 `PlayerHealth` 内部处理。
- **输入后端**：`activeInputHandler = 2`（Both），规范 5.5 用内置 `Input`，同时保留新 Input System 可用性。
- **暂停/恢复触发来源（v1.2）**：暂停 = Unity HUD 按钮、`ApplicationFocusPause`（自动暂停）、桌面 `Esc`/`P`；
  **恢复只由玩家主动触发**（继续按钮 / Esc / P → 3-2-1），失焦切回不会自动继续。
  状态权威仍是 `GameManager`；`Esc` 在浏览器全屏时可能被用于退出全屏，所以 `P` 必须同时可用。
- **所有进 Playing 都走 3-2-1**：菜单开始、暂停继续、暂停重开、结算再来一局统一 `Starting`；
  只给压测/自动化的 `StartGameImmediate` 绕过（`?perf=1&autostart=1`）。
- **难度偏好由 H5 代存**：Unity 不用 `PlayerPrefs`；`EnterGame.lastDifficulty` 只作菜单初始选中，
  菜单选择变化发 `onDifficultyChanged`，H5 存 `survival.difficulty`（下一阶段实现存取）。
- **昵称只在 H5 渲染**：Unity 只保存并回传，不进入任何 TMP 文本（避免中文子集字体约束与 XSS 面）。
- **H5 不猜状态**：页面状态由 `onReady` / `onStateChange` / `onGameOver` 驱动，不轮询；
  v1.2 H5 只负责进入与数据面板，局内操作不回传 Unity。
- **存储集中封装**：所有读写走 `web/js/storage.js`（`survival.bestScore` / `survival.nickname` / `survival.difficulty`），
  不可用时回退内存并在页面提示；最高分只在 `score > best` 时更新。
- **缓存必须失效**：Unity 产物文件名跨构建不变，`netlify.toml` 对 `/unity/Build/*`、`build-info.json`、HTML/JS/CSS 全部 no-cache；H5 的 js/css 另带 `?v=` 版本串，避免部署后还跑旧脚本。
- **本地打开限制**：`file://` 下直接提示用本地服务器，不尝试加载 Unity（浏览器跨域限制）。

## 6. 数值一览（`GameConfig`）

| 参数 | 值 | 出处 |
|---|---|---|
| 地图尺寸 | 36 × 20（约 2×2 屏，相机跟随并钳制） | `GameConfig` |
| 玩家 HP / 速度 / 无敌 | 100 / 3.2 / 1.0s | `GameConfig` |
| 敌人速度 / 接触伤害 / 碰撞半径 | 1.8 / 12 / 0.38 | `Assets/Configs/def_Enemy_Chaser.asset` |
| 生成间隔 / 每 10s 递减 / 下限 / 上限 | 1.5s / 0.15s / 0.5s / 20 | `GameConfig` |
| 生成位置 | 玩家周围 1.15×相机半对角线、视野外 0.05 | `EnemySpawner`（常量） |
| 速度扰动 / 速度上限 | ±12% / 2.2 | `GameConfig` |
| 压力曲线 | 1.0 → 1.15（按局内时间，作用于已存在敌人） | `GameConfig` |
| 敌人间距 / 位置修正轮数 | 0.95 / 2（Spawner 两两修正） | `GameConfig` |
| 停距 / 站位槽位 | 0.7 / 12（+ 抖动） | `EnemyAI` |
| 回收 | 离开相机视野（±0.05）持续 3s | `GameConfig` |
| 胜利时间 | 60s | `GameConfig` |
| 存活分 / 击杀分 / 胜利奖励 | 10/s / 20/个 / 200 | `GameConfig` |

> 仍无击杀手段，击杀分接口保留给后续里程碑；结算面板因此不显示「击杀」行。

## 7. 敌人行为扩展接缝（M4 已落地第一阶段）

> 目标：以后加"不同速度/不同外观/近战挥砍"的敌人，只加数据和组件，不改 `GameManager` / `ScoreService` / `PlayerHealth` / `PlayerController`。详细参数与验收见 `docs/玩法增强.md`。

M4 已实现：

1. **`EnemyDefinition` ScriptableObject**：`sprite / moveSpeed / contactDamage / colliderRadius`
   （分离半径由碰撞半径派生）。资产 `Assets/Configs/def_Enemy_Chaser.asset` 由 `SceneSetup` 自动创建，
   构建时重指一次精灵。`GameConfig` 继续管全局：生成间隔、上限、速度扰动、分离权重、回收阈值。
2. **Spawner 按定义组装 + 可注销**：`EnemyDefinition[] _enemyDefinitions`（当前 1 个，随机选取但只有一个）；
   暴露只读 `ActiveEnemies` 给分离力使用；`Recycle(EnemyAI)` 从列表移除并销毁，释放 20 上限名额。
3. **追踪增强在 `EnemyAI` 内**：软分离（O(n²)、无分配）、速度扰动、包围站位、远距离回收。
   接触伤害仍在 `EnemyAI.OnTriggerStay2D`（伤害值来自定义）。

尚未拆分的部分（M5/P2 再做，不阻塞当前验收）：

- 攻击与移动仍未拆组件：`EnemyAI` 同时负责移动与接触伤害。
  `ContactAttack` / `MeleeAttack`（`Approach → Windup → Strike → Recover → Cooldown`）按
  `EnemyDefinition.attackType` 组装的设计见 `docs/玩法增强.md` 第 4 节。
- `EnemyDefinition` 还没有 `attackType / attackRange / windup / cooldown / scale / maxHp` 字段。

表现层接缝：`EnemyView` 订阅前摇/出手/受伤事件做变色、缩放或 Animator；逻辑不直接操作 `SpriteRenderer`。

新增一个近战敌人的改动面（验证接缝）：

1. 新建 `EnemyDefinition` 资产（Melee、range 1.2、windup 0.35、cooldown 1.2、damage 15）
2. Spawner 的定义数组加一项
3. 新增 `MeleeAttack.cs`
4. 复用/新增 `EnemyView`
5. **不改** `GameManager`、`ScoreService`、`PlayerHealth`、`PlayerController`

明确不做：行为树、ECS、`IDamageable`/`DamageInfo`、Prefab 工厂管线、对象池、A*/流场。20 只敌人的分离用 O(n²) 本地计算即可。
