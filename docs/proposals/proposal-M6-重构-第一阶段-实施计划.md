# 重构 v1.2 第一阶段实施计划：Unity 菜单/状态机 + 协议 v3 骨架

> 文档版本：2026-11-01T23:05+08 · 作者：程序 · 状态：**待策划放行；落盘时尚未写实现代码**
> 依据：`docs/proposals/proposal-M6-重构-游戏优先-v1.2.md` §4–§7、§11–§13；
> `docs/交接提示词-2026-11-01-重构.md` A.4；策划 2026-11-01 审前 10 条约束。
> 基线：`main = 476bf3d1`（v1.0.0 线上不动）；`wip = 85741a3b`（重构方案 + 交接）。本计划只进 wip。

## 0. 摘要

1. **Unity 变成完整游戏**：默认进菜单，选难度开局；暂停 = 继续/重新开始/返回菜单；结算 = 得分/难度/存活/倍率 +
   再来一局/返回菜单。Standalone（不经过 H5）可完整玩一局。
2. **状态机**：业务态 `Menu / Starting / Playing / Paused / Result`（`Starting` 为统一 3-2-1 倒计时态，
   由审前约束 2 引入；方案原四态 + 该约束需要策划确认，见 §12-1）。内部枚举建议同步改名
   （`Ready→Menu`、`Won/Lost→Result`），并给出全部调用点清单与备选方案（§3.1）。
3. **3-2-1 统一归属 Unity**：菜单开始、暂停继续、暂停重开、结算再来一局，全部进 `Playing` 前走 3 秒
   unscaled 倒计时；倒计时中失焦 → 取消并回到来源状态（Menu/Paused/Result），不自动继续。
4. **协议 v3（本阶段只改 Unity 侧）**：H5→Unity `EnterGame`；Unity→H5 `onReady`（protocol=3 + 能力位）、
   `onStateChange`（`menu/playing/paused/result`）、`onGameOver`（字段沿用 v2）。旧 `StartGame` 当 `EnterGame`。
5. **结算归 Unity**：删除 `IsEmbedded` 抑制，WebGL 里也显示 ResultPanel；H5 只做数据面板。
6. **字体是硬前置**：现有子集 OTF 只有 25 个汉字，新文案缺 38 字左右；`×`（U+00D7）**不在现有子集**、
   但在完整 Noto Sans SC 里。用本机完整字体（OFL）重裁同名 `NotoSansSC-UI-Subset.otf`，精简 ASCII 以适配
   1024 TMP 图集；不新增字体文件，但会替换同名 OTF（需 §12-4 放行）。
7. **版本统一 1.2.0**：本阶段构建 `productVersion=1.2.0`，`web/index.html` 的 `?v=` 同步 1.2.0（只改版本串，
   不改 H5 行为）；不部署、v1.0.0 线上不动。
8. **1 次构建**（编辑器开着走菜单 `Survival/Build WebGL`），修复最多再 1 次。

## 1. 与审前 10 条的对齐 / 分歧（先看）

| # | 审前约束 | 本计划处理 |
|---|---|---|
| 1 | H5 全屏只在 EnterGame 之后显示 | **接受，属于下一阶段 H5 薄壳**；本阶段不改 H5，契约写进 §11.2，mock shell 图需小改由策划处理 |
| 2 | 所有进 Playing 的路径统一 3-2-1 | **接受**；引入 `Starting` 状态统一倒计时（§3.3），并同步改 PausePanel/ResultPanel 入口 |
| 3 | 失焦自动暂停 = Unity `OnApplicationFocus` 兜底 + 保留 H5 信号（幂等） | **接受**；§4.5 写明两者职责与真实失焦验证方法 |
| 4 | 建议不加 Menu 枚举，保留 Ready/Playing/Paused/Won/Lost 线格式 | **部分异议**：方案 §7 与交接明确线格式 `menu/playing/paused/result`，与本条要求的旧线格式冲突。计划默认按方案改名（选项 A），同时给“只改线格式、内部保留旧名”的选项 B 和全部调用点清单（§3.1）；请策划裁决 |
| 5 | 字体列全、确认 `×`、截图证明无 tofu | **接受**；已核对：`×` 不在现有子集、完整字体有；重裁后加入（§5） |
| 6 | Unity 按钮 Playwright 点击/断言方式、旧脚本影响 | **接受**；§9.2 给出归一化坐标换算、`window.SurvivalGameBridge` 事件捕获、旧脚本清单 |
| 7 | productVersion 与 H5 `?v=` 统一 1.2.0 | **接受**；`BuildScript.BundleVersion=1.2.0` + `web/index.html` 四处 `?v=1.2.0`（仅版本串） |
| 8 | H5 单一最高分 key / 最近一局 / 废弃 difficulty | **接受，属于下一阶段**；契约写进 §11.2，本阶段不改 `web/js/**` |
| 9 | 本阶段不做排行榜/退出外壳/内置全屏/昵称进 Unity/玩法计分 | **接受**；§11.1 |
| 10 | Standalone/H5/协议/全屏零按钮/6 视口/40 只全部给证据路径 | **接受，但全屏零按钮依赖 H5 改版**：旧 H5 未改，本阶段只能验“旧页过渡 + 画布菜单可操作”；v1.2 零按钮全屏的正式证据在下一阶段（§9.5 标注） |

## 2. 现状事实（代码）

| 位置 | 现状 |
|---|---|
| `GameState.cs` | `Booting / Ready / Playing / Paused / Won / Lost` |
| `GameManager.cs` | `StartGame` 只接受 `Ready`；`RestartGame` 只接受 `Won/Lost`；`ReturnToMenu` 回 `Ready`；`OnResumeCountdownRequested` 由 PausePanel 消费 |
| `PausePanel.cs` | 只有「继续」+ 3-2-1（自己持有 countdown 计时） |
| `ResultPanel.cs` | `Won/Lost` 显示；`IsEmbedded` 为真时隐藏 |
| `WebGLBridge.cs` | protocol 2；`StartGame/PauseGame/ResumeGame/RestartGame/ReturnToMenu/SetPausedByVisibility`；`IsEmbedded` |
| `SceneSetup.cs` | 代码生成 HUD/暂停/结算；无菜单面板 |
| `NotoSansSC-UI-Subset.otf` | 121 glyph = 95 ASCII + 25 汉字 + 空格；新字缺失 |
| `web/**`（v1.1） | H5 拥有难度/开始/3-2-1/暂停/结算；本阶段不改行为 |

## 3. 状态机

### 3.1 枚举与调用点

**选项 A（推荐，方案 §7 口径）**

```csharp
public enum GameState
{
    Booting,    // Awake 初始化瞬间
    Menu,       // 主菜单（原 Ready）
    Starting,   // 3-2-1 倒计时（新，来源为 Menu/Paused/Result）
    Playing,
    Paused,
    Result,     // 原 Won + Lost，胜负由 LastRoundWon 区分
}
```

需要改的状态判断/赋值点（全量扫描结果）：

| 文件 | 位置 | 改动 |
|---|---|---|
| `Core/GameManager.cs` | `Awake/StartGame/PauseGame/ResumeGame/ReturnToMenu/RestartGame/Win/HandlePlayerDied` | 全部迁移到新枚举 + `Starting` |
| `Core/PerfProbe.cs` | 自动开局判断 `State == Ready` | → `Menu` |
| `Core/PauseHotkey.cs` | `Playing ↔ Paused` | 不变（`Starting` 下忽略） |
| `Player/PlayerController.cs` | `State == Playing` | 不变 |
| `UI/PausePanel.cs` | `Paused` 判断 | 改为按钮组 + 请求倒计时 |
| `UI/ResultPanel.cs` | `Won/Lost`、`IsEmbedded` | → `Result`、始终显示 |
| `UI/HudView.cs` | 暂停按钮 `Playing` | 不变；新增 HUD 根显隐 |
| `UI/TouchJoystickView.cs` | `Playing` | 不变 |
| `Bridge/WebGLBridge.cs` | `Won/Lost/Ready/Paused` | 改为 `Result/Menu/Paused` + 线格式映射 |

**选项 B（低 churn 备选）**：内部保留 `Ready/Won/Lost`，只新增 `Starting`，`onStateChange` 仍映射为
`menu/playing/paused/result`。优点：少改 8 个文件；缺点：内部命名与方案/文档长期分叉。
**不建议**按审前第 4 条把线格式改回 `Ready/Playing/Paused/Won/Lost`：方案 §7 与交接都写明 v3 线格式是
`menu/playing/paused/result`，H5 下一阶段按新格式写。

### 3.2 迁移表（含 `Starting`）

| 迁移 | 触发 | 行为 |
|---|---|---|
| `Booting → Menu` | `Awake` | 初始化 Score、订阅死亡 |
| `Menu → Starting` | 菜单「开始游戏」→ `StartGame(diff)` | 记录难度；`Time.timeScale=0`；3s unscaled 倒计时 |
| `Paused → Starting` | 「继续」→ `RequestResumeCountdown()` | 不重置本局；倒计时后回 `Playing` |
| `Paused → Starting` | 「重新开始」→ `RestartGame()` | 倒计时结束后 `ResetRound()` 再 `Playing` |
| `Result → Starting` | 「再来一局」→ `RestartGame()` | 沿用难度；倒计时结束后 `ResetRound()` 再 `Playing` |
| `Starting → Playing` | 倒计时归零 | `timeScale=1`；需要重置的路径先 `ResetRound()`；`LastRoundWon=false` |
| `Starting → 来源态` | 失焦 / `CancelStartCountdown()` | `Menu` / `Paused` / `Result`；不重置、不结算 |
| `Playing → Paused` | 暂停按钮 / Esc / P / 失焦 | `timeScale=0` |
| `Playing/Paused → Result` | HP≤0 / 60s | `LastRoundWon`、停刷、`timeScale=0`、发一次 `onGameOver` |
| `Paused/Result → Menu` | 「返回菜单」→ `ReturnToMenu()` | 清场、重置、**不结算、不发 onGameOver** |
| `Starting` 下 Esc/P | — | 忽略（沿用 M4.6「倒计时中重复按 P 忽略」） |

- `ReturnToMenu` 在 `Menu` 幂等；`RestartGame` 在 `Menu/Playing/Starting` 忽略；`StartGame` 只在 `Menu` 接受。
- `Starting` 期间 `timeScale=0`：`EnemySpawner/EnemyAI/GameManager` 都用 `Time.deltaTime`，不会刷怪/移动
  （已核对 `EnemySpawner.Update` 有 `Time.deltaTime <= 0` 提前返回）。
- 结算数据在 `Starting` 期间保留：`Result→Starting` 不先 `ResetRound`，取消可原样回结算卡，避免分数被清零。

### 3.3 统一 3-2-1（审前约束 2）

- 计时器由 `GameManager` 持有（状态权威），`Time.unscaledDeltaTime`，单帧夹到 0.1s（防止切回时一帧走完）。
- 新增 `CountdownRemaining`、`OnCountdownChanged(int)`；`PausePanel` 删除自带倒计时，新增
  `Assets/Scripts/UI/StartCountdownPanel.cs` 统一显示遮罩 + 大数字。
- 来源态记录 `_countdownReturnState`；`CancelStartCountdown()` 回到来源态（失焦时调用）。
- 倒计时结束后：`Menu/Paused(重开)/Result` 路径先 `ResetRound()`，再 `timeScale=1`、`Playing`；
  `Paused(继续)` 路径不重置。
- 开局是否额外加 3-2-1：**按审前约束 2，加**（原计划“不加”作废）。

## 4. 场景与 UI（`SceneSetup` 代码生成）

### 4.1 主菜单 `MenuPanel`（新增）

- 子节点顺序：`HUD` → `PausePanel` → `ResultPanel` → `MenuPanel` → 摇杆（`SetAsFirstSibling`）。
- 面板铺满 + 不透明底色 `#0D0D15`；`Menu` 状态显示，其余隐藏。
- 布局（宽度全部用百分比锚点，兼顾 1280×720 与 640×360 参考分辨率）：

| 元素 | 锚点/位置 | 字号 | 文案 |
|---|---|---|---|
| 标题 | (0.5, 0.84)，宽 80% | 56 粗体 | 生存挑战 |
| 副标题 | (0.5, 0.74)，宽 80% | 24 | 存活 60 秒 |
| 难度区 | x 12%–88%，y 36%–62%，三列等分 | 32 / 20 / 20 | 简单 / 普通 / 困难 + 短标签 + 倍率 |
| 开始按钮 | (0.5, 0.20)，宽 46%、高 88 单位 | 32 粗体 | 开始游戏 |

- 卡片短标签：`更慢更少 / 标准节奏 / 更快更多`；倍率 `×1.0 / ×1.3 / ×1.6`（读 `DifficultyProfile.ScoreMultiplier`，只展示）。
- 选中态：紫色底色（如 `#6B4CFF`），未选中 `#212134`；点卡只改选择，不开局。
- 默认选中上次难度（§4.6）；**菜单不显示最高分/昵称/全屏按钮**（用户已定）。
- 菜单不提供「退出」；`Menu` 下 Esc/P 不做事。

### 4.2 暂停面板 `PausePanel`（改造）

- 标题「已暂停」+ 按钮组「继续 / 重新开始 / 返回菜单」；倒计时移到 `StartCountdownPanel`。
- 三按钮等宽 190 单位、间距 14，居中 y≈0.30；640 参考下 190×3+28=598 < 640。
- 「继续」→ `RequestResumeCountdown()`；「重新开始」→ `RestartGame()`；「返回菜单」→ `ReturnToMenu()`。
- 失焦自动暂停仍“切回保持暂停”，不自动恢复。

### 4.3 结算面板 `ResultPanel`（改造）

- 始终显示（删除 `IsEmbedded` 与 `_bridge` 引用），`Result` 时显示；`Starting` 时隐藏。
- 内容：标题（胜利/失败）、`本局得分 N`、`难度 普通 · 存活 60 秒 · 倍率 ×1.3`、
  明细（存活分 / 击杀（>0 才显示）/ 胜利奖励（仅胜利））、按钮「再来一局 / 返回菜单」。
- 不显示昵称；按钮宽度用百分比锚点。

### 4.4 倒计时面板 `StartCountdownPanel`（新增）

- 全屏半透明遮罩 + 大号数字（120），`Starting` 显示，其余隐藏。
- 数字来自 `GameManager.OnCountdownChanged`；不自己计时、不改状态。
- 暂停/继续/重开/再来一局共用这一套，避免两套倒计时逻辑。

### 4.5 HUD 与失焦

- `HudView` 增加 `_hudRoot`：`Menu` 隐藏；`Starting/Playing/Paused/Result` 显示；暂停按钮仍只在 `Playing`。
- 新增 `Assets/Scripts/Core/ApplicationFocusPause.cs`（挂 `GameManager` 物体）：
  - `OnApplicationFocus(false)` / `OnApplicationPause(true)`：
    `Playing → PauseGame()`；`Starting → CancelStartCountdown()`；其他状态不动。
  - 切回不自动恢复（M4.6）。
  - `if (Application.isEditor) return;`：Editor 内不启用，避免失焦停 Play 循环干扰自动化（`遗留问题.md` §4）。
- 旧 `SetPausedByVisibility` 保留为兼容入口，与原生失焦**幂等**：`PauseGame` 有状态守卫；
  `"false"` 只清标记、不恢复。H5 薄壳下一阶段可不再发送。
- 真实失焦验证：沿用 `tools/e2e/smoke.mjs` 的做法（CDP `Emulation.setFocusEmulationEnabled(false)` +
  第二个页面抢焦点），在 Standalone 页面重跑；无头下只验 Chrome/Edge（Firefox 无该开关）。

### 4.6 难度持久化

- `MenuPanel` 选中项写 `PlayerPrefs("survival.lastDifficulty")`；启动读取（非法/缺失 → `Normal`）。
- `GameConfig.ActiveDifficulty` 仍是运行时字段（不加 `[SerializeField]`、不写回资产），`RestartGame` 沿用。
- 若策划不要跨刷新记忆，可退化为会话内字段（§12-6）。

## 5. 文案与字体

### 5.1 `UiStrings` 新增文案（全量）

| 常量 | 文案 |
|---|---|
| `MenuTitle` | 生存挑战 |
| `MenuSubtitle` | 存活 60 秒 |
| `DifficultyEasy / Normal / Hard` | 简单 / 普通 / 困难 |
| `TagEasy / Normal / Hard` | 更慢更少 / 标准节奏 / 更快更多 |
| `StartGameButton` | 开始游戏 |
| `BackToMenuButton` | 返回菜单 |
| `PlayAgainButton` | 再来一局 |
| `RoundScoreLabel` | 本局得分 |
| `DifficultyLabel` | 难度 |
| `SurvivedLabel` | 存活 |
| `SecondsSuffix` | 秒 |
| `MultiplierLabel` | 倍率 |
| 已有 | 生命/时间/分数/暂停/已暂停/继续/胜利/失败/得分/存活分/击杀/胜利奖励/重新开始 |

倍率格式 `×{0:0.0}`。**字符核对结果**：`×`（U+00D7）**不在**现有 `NotoSansSC-UI-Subset.otf`，
但在完整 `NotoSansSC-Regular.otf` 中；重裁后加入。

### 5.2 缺字现状与重裁

- 现有子集只有 25 个汉字：停击分利励命失奖始存已开得数新时暂杀活生继续胜败重间。
- 新文案需要新增约 38 个汉字（挑/战/秒/选/择/难/度/简/单/普/通/困/更/慢/少/标/准/节/奏/快/多/游/戏/返/回/菜/局/再/来/一/倍/率 等）。
- 路径（`遗留问题.md` §5 已写）：用完整 `NotoSansSC-Regular.otf`（OFL，本机
  `D:\agent使用学习\技术栈补充\agent制作游戏相关\assets\fonts\NotoSansSC-Regular.otf`，8.3MB）重裁，
  **覆盖同名** `Assets/Fonts/NotoSansSC-UI-Subset.otf`；完整字体不入库、不新增字体文件。
- 新增 `tools/font-subset.py`（source 路径参数化，字符集 = `UiStrings` 全量 + `ui-chars.txt` 清单），可复现。
- 新 `ui-chars.txt` 只保留 UI 实际用的 ASCII：`0-9`、空格、`:`、`/`、`.`、`×`、`%`、`-` + 全部汉字。
- TMP 图集容量：1024×1024 / Sampling 64 / Padding 8 → 每格 80px、12×12=**144 glyph**；
  精简后约 **75 glyph**（脚本核对），**图集与采样参数不变**；若仍不足才考虑 2048（需策划点头）。
- 重烤仍由 `FontAssetBuilder.TryRebuild` 在构建前自动完成，输出同名 `NotoSansSC-UI-SDF.asset`，不新增 TMP 资源。
- 证据：构建日志无“缺少字符/没有进图集”；菜单/暂停/结算截图逐字看，证明无 tofu（§9.6）。

## 6. 协议 v3（本阶段只改 Unity 侧）

### 6.1 消息表

| 方向 | 消息 | 载荷 | 说明 |
|---|---|---|---|
| H5→Unity | `EnterGame` | `{"nickname":"阿宝"}`（可空） | 进入游戏；昵称只记日志；`Menu` 下幂等；`Starting/Playing/...` 下忽略并 warning |
| Unity→H5 | `onReady` | `{"protocol":3,"capabilities":["enterGame","stateChange","gameOver"]}` | 首帧后一次 |
| Unity→H5 | `onStateChange` | `{"protocol":3,"state":"menu\|playing\|paused\|result"}` | 每次状态变化；`Starting` 不单独发（仍在来源态/playing 前的等待，H5 不需要） |
| Unity→H5 | `onGameOver` | §6.2 | 每局一次（`_reported` 守卫） |
| 旧兼容 | `StartGame` | v2 JSON / 纯昵称 | **当 `EnterGame`**，不自动开局；打一次 warning |
| 旧兼容 | `SetPausedByVisibility` | `"true"/"false"` | 保留；与原生失焦幂等 |
| 删除 | `PauseGame / ResumeGame / RestartGame / ReturnToMenu` | — | Unity 保留同名 no-op + 一次性 warning，避免旧页 SendMessage 报错；H5 v1.2 上线后移除 |

- `onStateChange` 不发 `Starting`：它不是一个可交互业务态，H5 数据栏无需求；若策划要求可窥，
  可发 `starting` 但 H5 不用（§12-3）。

### 6.2 `onGameOver`

```json
{
  "protocol": 3,
  "event": "gameOver",
  "payload": {
    "result": "win",
    "score": 980,
    "survivedSeconds": 60.0,
    "kills": 0,
    "nickname": "阿宝",
    "difficulty": "normal",
    "breakdown": { "survival": 780, "kills": 0, "winBonus": 200 }
  }
}
```

字段形状沿用 v2，只升 `protocol`；`difficulty` 是 Unity 实际采用值。

### 6.3 兼容矩阵

| 组合 | 结果 | 处理 |
|---|---|---|
| 新 Unity + 新 H5（下一阶段） | 目标 | `EnterGame` + 四态线格式 |
| 新 Unity + 旧 H5（v1.1 缓存） | `onReady` 协议 warning；`StartGame`→`EnterGame` 停在菜单；旧页仍显示开始；画布内可开局；`onGameOver` 旧页仍更新最高分 | 同版本部署 + `?v=`；README/协议写明过渡 |
| 新 H5 + 旧 Unity | 不支持 | 方案 §7：同版本部署 |

### 6.4 `IsEmbedded` 与失焦归属

- 删除 `IsEmbedded` 与 ResultPanel 抑制：结算归 Unity。
- 失焦：Unity 原生为主，H5 `SetPausedByVisibility` 为兼容兜底；两者幂等，切回保持 Paused。

## 7. 文件改动清单

**新增**：`Assets/Scripts/UI/MenuPanel.cs`、`Assets/Scripts/UI/StartCountdownPanel.cs`、
`Assets/Scripts/Core/ApplicationFocusPause.cs`、`tools/font-subset.py`、`tools/e2e/standalone-menu.mjs`、
`docs/M6-重构-执行记录.md`。

**修改**：`GameState.cs`、`GameManager.cs`、`PerfProbe.cs`、`UiStrings.cs`、`PausePanel.cs`、`ResultPanel.cs`、
`HudView.cs`、`WebGLBridge.cs`、`SceneSetup.cs`、`Assets/Fonts/ui-chars.txt`、
`Assets/Fonts/NotoSansSC-UI-Subset.otf`（重裁）、`NotoSansSC-UI-SDF.asset`（构建重烤）、
`Assets/Editor/BuildScript.cs`（BundleVersion 1.2.0）、`web/index.html`（仅 `?v=1.2.0`）、
`docs/bridge-protocol.md`、`docs/实现总览.md`、`docs/architecture.md`、`docs/acceptance.md`、`docs/遗留问题.md`。

**不碰**：`web/js/**`、`web/css/**`、`docs/tickets/**`、`docs/参考拆解.md`、`CLAUDE.md`、`ProjectSettings/**`、
`GameConfig` 数值、`EnemyAI/EnemySpawner`、`ScoreService` 公式。

## 8. 实施顺序、checkpoint、构建次数

| 步 | 内容 | 验证（不构建） | 提交 |
|---|---|---|---|
| 1 | 状态机：`GameState/GameManager`（含 `Starting` 与倒计时）/`PerfProbe`/`ApplicationFocusPause` | MCP 重编译 0 error；`execute_code` 走全套迁移/取消 | `feat(refactor-p1): 状态机 Menu/Starting/Playing/Paused/Result` |
| 2 | 文案 + 字体：`UiStrings`/`ui-chars.txt`/`font-subset.py`/重裁 OTF | FontAssetBuilder 自检无缺字 | `feat(refactor-p1): 菜单/结算文案 + 字体子集重裁` |
| 3 | UI：`MenuPanel`/`StartCountdownPanel`/`PausePanel`/`ResultPanel`/`HudView`/`SceneSetup` | 重编译；Play 截图菜单/倒计时/暂停/结算 | `feat(refactor-p1): Unity 主菜单 + 暂停/结算/倒计时 UI` |
| 4 | 协议 v3：`WebGLBridge` | Editor 日志检查 ready/state/gameOver JSON | `feat(refactor-p1): 协议 v3 骨架 + 旧 StartGame 兼容` |
| 5 | 构建 #1 + Standalone/Editor 验收 + 证据 + 文档 | §9；失败修复后构建 #2（最多） | `build(refactor-p1): 构建 #1（1.2.0）+ 验收证据` |

- 构建用 `execute_menu_item("Survival/Build WebGL")`（编辑器当前开着；batchmode 会撞锁）。
- 构建前自动重烤字体 + `SceneSetup.GenerateScene()`；每步只推 wip，`main` 等验收。

## 9. 验收证据

### 9.1 Editor（MCP）

- 重编译 0 error；Console 无新 error。
- `execute_code` 驱动并断言：`Menu` → `StartGame(Easy/Normal/Hard)` → `Starting`（倒计时 3s）→ `Playing`
  （校验 `ActiveDifficulty`/倍率）→ `PauseGame` → `RequestResumeCountdown` → `Starting` → `Playing`；
  `Paused` 下 `RestartGame` → `Starting` → `Playing`（计时归零）；反射 `FinishRound(true/false)` → `Result`
  （`LastRoundWon`）→ `RestartGame` / `ReturnToMenu`；`Starting` 中 `CancelStartCountdown` 回来源态。
- 截图：菜单（默认/三档选中）、倒计时、暂停、结算（胜/负）。

### 9.2 Standalone 直开（核心验收）

- 构建后静态服务，打开 `/unity/index.html`（Unity 生成，不入库）。
- 事件捕获：Playwright `page.addInitScript` 在 Unity 启动前注册
  `window.SurvivalGameBridge = { onReady/onStateChange/onGameOver }`，把 JSON push 到
  `window.__survivalEvents`；断言 `protocol===3`、能力位、状态序列、`onGameOver.payload.difficulty`。
  Unity 的 `Debug.Log` 同时进浏览器 Console 作为第二信源。
- 点击映射（Playwright 不能按 DOM 点 Unity）：
  - 视口固定 `1280×720`（DPR 1）→ `Screen.width=1280 ≥1000` → Canvas 参考 1280×720，锚点即比例；
  - 点击点 = `canvasRect.left + fx * width`, `top + fy * height`（归一化坐标，从上/左算）；
  - 关键坐标：开始按钮 `(0.50, 0.80)`；难度卡 `(0.247/0.50/0.753, 0.49)`；
    暂停按钮 `(0.933, 0.928)`；暂停三按钮 `(0.341/0.50/0.659, 0.70)`；结算两按钮 `(0.403/0.597, 0.80)`。
    若后续改布局，只改这份坐标表并在脚本里集中定义。
- 断言流程：开始 → `onStateChange(playing)`（3s 后）→ Esc → `paused` → 点继续 → 3s → `playing`；
  用 `?perf=1&roundDuration=5&autostart=1`（PerfProbe 现有参数）跑到 `result` + `onGameOver`（difficulty 校验）；
  点再来一局 → 倒计时 → `playing`；返回菜单 → `menu`。
- 证据：`docs/evidence/m6-refactor/standalone-*.png` + 事件 JSON 摘要。

### 9.3 旧 H5 过渡（只验不修）

- `web/index.html`（v1.1）+ 新构建：Console 有协议 3 warning；点开始 → Unity 菜单；画布内可玩；
  `onGameOver` 后旧页最高分仍更新；无白屏/崩溃。证据截图 + console 文本。

### 9.4 视口（6 组）

- Standalone `index.html`：`320×568 / 390×844 / 844×390 / 768×1024 / 1280×720 / 1920×1080`；
  菜单/倒计时/暂停/结算截图，检查无横向溢出、按钮可点、无 tofu。
- 证据：`docs/evidence/m6-refactor/viewport-*.png`。

### 9.5 全屏零按钮（依赖下一阶段）

- 本阶段 H5 未改，旧页全屏仍保留网页控件；**无法**给出 v1.2「全屏内 0 网页按钮」的正式证据。
- 本阶段只验：旧页全屏时画布内 Unity 菜单/暂停可点（过渡证据）。
- 正式证据在 H5 薄壳阶段补：`docs/evidence/m6-refactor/fullscreen-*.png`（入口只在 EnterGame 后、全屏 0 网页按钮）。

### 9.6 字体无 tofu

- 构建日志无缺字；菜单/难度卡/暂停/结算 4 处截图逐字核对；`×1.3` 确认不是方块。

### 9.7 性能（40 只）

- Standalone `?perf=1&enemyLimit=40&roundDuration=600&perfSeconds=60&autostart=1`，
  收集 `[PERF-SUMMARY]`：桌面基线 165 FPS / p95 7ms / alloc 26~35KB/s 不得明显劣化；
  新增 UI 只在状态/倒计时整数变化时刷新，不引入每帧 GC。
- 证据：`docs/evidence/m6-refactor/perf-40.json`。

### 9.8 回归与旧脚本影响

- 三档 60 秒分数不变（800/980/1160）：计分代码不动，用短局 `roundDuration` 验倍率公式。
- **旧 E2E 预期失效**，本阶段不作为门禁：`smoke.mjs`、`difficulty-e2e.mjs`、`menu-check.mjs`、
  `fullscreen-states.mjs`、`responsive-shot.mjs`、`joystick-shot.mjs`、`perf.mjs` 都依赖 H5 DOM 的
  开始/难度/暂停/全屏或直接 `SendMessage('WebGLBridge','StartGame',…)`；下一阶段随 H5 改版重写。
- 证据目录：`docs/evidence/m6-refactor/`（新）；旧证据不动。

## 10. 风险与停点

| 风险 | 处理 |
|---|---|
| `Starting` 超纲（方案四态变五态） | §12-1 请策划确认；若否，退回“不加开局倒计时”，但违反审前约束 2 |
| 3-2-1 期间失焦/切回 | 取消倒计时回来源态；`unscaledDeltaTime` 单帧夹 0.1s；E2E 真实失焦验证 |
| 字体缺字/图集容量 | 完整字体已核对覆盖；约 75 glyph < 144；失败即停报缺失字符 |
| 字体二进制替换 | §12-4 放行；OFL 许可文件保留；完整字体不入库 |
| 状态改名漏改 | `grep GameState.Ready/Won/Lost` 清零后提交；MCP 每次重编译 |
| 旧 H5 过渡 | 同版本部署 + `?v=1.2.0`；协议 warning 不阻塞；README 写清两阶段 |
| WebGL 失焦事件不触发 | Standalone 真失焦验证；失败则保留 H5 信号为主路径并停报 |
| Editor 失焦停 Play | `ApplicationFocusPause` Editor 内 return；测试 `runInBackground=true` |
| 窄视口菜单溢出 | 百分比锚点 + 6 视口截图 |
| 全屏零按钮无法本阶段验收 | §9.5 明确顺延下一阶段，不伪造证据 |
| 版本串不一致 | `BuildScript.BundleVersion` + `web/index.html ?v=` 同改 1.2.0；构建后核对 `build-info.json` |
| 与 M6 第 3 步冲突 | 不碰计分/敌人；计分改造仍未放行 |

**停点**：字体重裁后仍缺字；需要偏离方案/审前约束且未获裁决；需要用户 Unity GUI 操作/重启；
构建连续两次失败；需要改 ProjectSettings/加第三方包；全屏或真机问题需要人工操作。

## 11. 边界

### 11.1 本阶段不做

- H5 薄壳（昵称/进入/数据面板/共享最高分/最近一局/localStorage/响应式）。
- 排行榜、退出到网页外壳、Unity 内置全屏、全屏状态/退出 UI。
- 昵称进 Unity、最高分在 Unity 展示/持久化。
- 玩法/数值/计分（含 M6 第 3 步）、敌人类型、美术/音效、地图/相机。
- 部署、v1.0.0 线上变更、v1.2 Release、main 前移。
- 旧 E2E 全套重写（下一阶段随 H5）。

### 11.2 下一阶段（H5 薄壳）已定契约（本阶段不实现）

- 全屏按钮**只在 `EnterGame` 后显示**；Shell 阶段只有昵称 + 开始；全屏内 0 网页按钮，画布内 Unity 菜单可点。
- 单一最高分 key `survival.bestScore`；首次迁移取 `bestScore.{easy,normal,hard}` 三者 max，
  旧键只读一次后不再读写；**最近一局只从 `onGameOver` 更新、不持久化**（刷新清空）。
- `survival.difficulty` 废弃（难度归 Unity）。
- H5 不再发局内指令；开始与全屏两个独立按钮；昵称进入后只读。

## 12. 待策划确认

1. **`Starting` 状态**：审前约束 2 要求所有进 `Playing` 路径走 3-2-1，方案 §6 只写四态；
   本计划加 `Starting`（推荐）。是否接受五态？若否，请明确哪条让路。
2. **枚举/线格式**：本计划按方案 §7 用 `menu/playing/paused/result`，内部建议同步改名（选项 A）；
   审前第 4 条建议保留旧线格式。请裁决 A（推荐）/ B（内部旧名、线格式新名）/ C（全旧，需改方案）。
3. **`Starting` 是否发 `onStateChange`**：本计划不发（H5 无需求）；若要，加 `starting`。
4. **字体二进制替换**：重裁同名 OTF + 精简 ASCII，是否算“不新增素材”的可接受范围。
5. **结算明细**：保留「存活分/击杀/胜利奖励」行（推荐），还是只留 mock 的得分/难度/存活/倍率。
6. **难度持久化**：`PlayerPrefs` 跨刷新（推荐）还是仅会话内。
7. **旧 H5 过渡范围**：只保证进菜单/可玩/回传分数；旧页难度卡与暂停按钮语义不保证——是否接受。
8. **参考拆解同步**：`docs/参考拆解.md` §2.6 旧结论“入口只保留 H5 一层”已被 v1.2 取代，
   该文件 owner 是策划，请补决策记录。
9. **版本串**：确认本阶段把 `web/index.html` 四处 `?v=` 提到 1.2.0（仅版本串，不改 H5 行为）。

## 13. 预估

- 实现 + 字体 + 文档：约 1~1.5 个工作日（不含评审）。
- 构建：1 次（约 4 分钟）+ 最多 1 次修复。
- 验收：Editor 状态机/截图 ~1 小时；Standalone/6 视口/失焦/40 只 ~2 小时（无头）。
