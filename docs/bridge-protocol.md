# H5 ↔ Unity 通信协议（protocol 2）

> 代码与文档同步维护：`Assets/Scripts/Bridge/WebGLBridge.cs` + `WebGLBridge.jslib`、`web/js/bridge.js`、`web/js/app.js`。
> 设计依据：`docs/项目规范.md` 第 7 节；本文件是 M3 起的实现契约，v2 见 §2.2 与 §6。

## 1. 加载方式

- 不使用 iframe：`createUnityInstance(canvas, config, onProgress)` 直接把 Unity 挂到页面 canvas 上。
- `window.SurvivalGameBridge` 必须在加载 `unity.loader.js` **之前**注册（`web/js/bridge.js` 顺序保证）。
- 配置文件：`web/unity/build-info.json`（由 `BuildScript` 扫描构建产物生成，不要手改）。

## 2. H5 → Unity

`unityInstance.SendMessage('WebGLBridge', 方法名, 单个 string 参数)`。
目标 GameObject 固定名 **`WebGLBridge`**，必须处于激活状态。
`SendMessage` 只支持 1 个参数，因此所有结构化数据都先序列化成 JSON 字符串。

| 方法 | 参数 | 状态迁移 | 说明 |
|---|---|---|---|
| `StartGame` | JSON `{"nickname":"...","difficulty":"easy\|normal\|hard"}` | Ready → Playing | 昵称只保存，不进入 Unity UI；`difficulty` 非法/缺省、或载荷是纯昵称字符串（v1 写法）→ `normal`（打一次 warning）；难度**只在这里设置一次**，局内锁定（见 2.2）；同时进入「H5 嵌入模式」（见 2.1） |
| `PauseGame` | `""` | Playing → Paused | H5 手动暂停；会清除「因隐藏而暂停」标记 |
| `ResumeGame` | `""` | Paused → 3-2-1 → Playing | H5 手动继续；M4.6 起只请求倒计时（`RequestCountdownResume`），不直接恢复；会清除标记。当前 H5 无人调用 |
| `RestartGame` | `""` | Won/Lost → Playing | 全量重置（血量、计时、分数、敌人、玩家位置），昵称**与难度**沿用本局快照（不重新读 H5） |
| `SetPausedByVisibility` | `"true"` / `"false"` | Playing ↔ Paused（条件） | `true`：Playing → 暂停并记标记；只要处于 Paused 就**取消倒计时**并保持暂停。`false`：**只清标记**，保持 Paused，不自动倒数、不自动恢复（M4.6） |

状态守卫（由 `GameManager` 保证，桥接层只转发）：

- `StartGame` 只接受 `Ready`；其他状态直接忽略并打 warning。
- `RestartGame` 只接受 `Won` / `Lost`。
- 失焦自动暂停后切回：**保持 Paused**，不自动恢复；恢复必须由玩家主动触发
  （暂停面板「继续」/ Esc / P）→ 3-2-1 → Playing。手动暂停同理（M4.6）。

### 2.1 结算归属（M4）

- WebGL 构建（`#if UNITY_WEBGL && !UNITY_EDITOR`）里 `WebGLBridge.IsEmbedded = true`，`StartGame` 时再置一次。
- 嵌入模式下 Unity **不显示** ResultPanel（成绩、明细、重开都由 H5 卡片负责）；PausePanel 仍保留在游戏内。
- Editor 里 `IsEmbedded = false`，Unity 结算面板保留，供自测。
- 因此 `onGameOver` 是 H5 结算的唯一数据源；Unity 面板上的「重新开始」只用于 Editor 自测。

### 2.2 难度（v2，M6 §7.2）

- `difficulty ∈ easy | normal | hard`（小写匹配）；其它任何值（空、数字、中文、大小写变体）→ `normal`。
  纯昵称字符串载荷（v1 写法）也只取昵称，难度按 `normal`。
- Unity 侧活动难度是 `GameConfig` 的**运行时字段**（`private`、不加 `[SerializeField]`，与 `_perfEnemyLimitOverride` 同写法），
  **不写回 SO 资产**：Editor 里试玩一局不能把 `Assets/Configs/cfg_GameConfig.asset` 弄脏。
- `StartGame` 时解析并快照；`RestartGame` 沿用快照；Playing/Paused/Won/Lost 期间收到 `StartGame`
  仍按状态守卫忽略——难度**局内不可改**。
- 档位中文名只在 H5 渲染（Unity 内不出新中文文案，避免动 TMP 字体图集）。
- 计分（v2 的中间态）：难度倍率**只作用于存活分**，胜利奖励固定 200；
  分段存活分 + 血量分的公式重做在 M6 第 3 步，届时 `breakdown` 形状会再变一次。

## 3. Unity → H5

`.jslib` 通过 `[DllImport("__Internal")]` 调用，统一入口 `window.SurvivalGameBridge`：

| 事件 | 载荷 | 时机 |
|---|---|---|
| `onReady` | `{"protocol":2}` | 首帧渲染完成后一次（`GameBootstrap` 发，桥接层 `_readySent` 兜底） |
| `onStateChange` | `{"protocol":2,"state":"Playing"}` | 每次状态变化；`state` ∈ `Ready / Playing / Paused / Won / Lost` |
| `onGameOver` | 见下方 schema | Won/Lost，每局仅一次（`_reported` 守卫） |

`onGameOver` 载荷：

```json
{
  "protocol": 2,
  "event": "gameOver",
  "payload": {
    "result": "win",
    "score": 1160,
    "survivedSeconds": 60.0,
    "kills": 0,
    "nickname": "阿宝",
    "difficulty": "hard",
    "breakdown": {
      "survival": 960,
      "kills": 0,
      "winBonus": 200
    }
  }
}
```

- `result`：`"win"` | `"lose"`。
- `survivedSeconds`：保留 1 位小数；胜利固定为 60。
- `kills`：当前无击杀手段，恒为 0（字段保留）。
- `nickname`：来自 `StartGame`，只回传不渲染到 Unity。
- `difficulty`：Unity **实际采用**的档位（非法值/缺省已被回落为 `normal`）。H5 必须用它做分档记分，
  不要用自己发送的值——两者在合法输入下相同，但以 Unity 的回传为准。
- `breakdown.survival` 已含难度倍率（v2 中间态：`600 × 1.6 = 960`），`winBonus` 不乘倍率。

## 4. 时序

```text
页面加载
  └─ 注册 window.SurvivalGameBridge（含 onReady/onStateChange/onGameOver）
  └─ fetch build-info.json → 加载 loader → createUnityInstance
Unity 首帧
  └─ onReady({"protocol":1}) → H5 隐藏遮罩、启用「开始游戏」
点击开始
  └─ SendMessage StartGame({"nickname","difficulty"}) → Playing（难度快照、局内锁定）
     └─ onStateChange(Playing) → H5 隐藏开始按钮、难度卡、禁用昵称输入
窗口失焦 / 标签页隐藏（blur、visibilitychange）
  └─ SendMessage SetPausedByVisibility("true") → Paused（若在 Playing），暂停面板出现
窗口聚焦 / 标签页恢复
  └─ SendMessage SetPausedByVisibility("false") → 只清标记，**保持 Paused**；
     不自动倒数、不自动恢复（M4.6）
玩家点「继续」/ 按 Esc/P（主动恢复）
  └─ Unity RequestCountdownResume() → 暂停面板 3-2-1（Time.unscaledDeltaTime）
     → ResumeGame() → onStateChange(Playing)
     ——3 秒留给手机把手指挪回触控区；倒计时中重复按 P 被忽略
倒计时期间再次失焦
  └─ SendMessage SetPausedByVisibility("true") → 取消倒计时（保持 Paused），
     切回后仍需玩家重新点继续；不会在玩家看不见的时候自动继续
HP ≤ 0 / 计时 ≥ 60s
  └─ onStateChange(Won|Lost) + onGameOver（一次）→ H5 显示结算
点击重新开始
  └─ SendMessage RestartGame("") → Playing
     └─ onStateChange(Playing) → H5 关闭结果面板（_reported 已清零）
```

- H5 **不轮询** Unity 状态，只处理事件；HUD 数字在 Unity 内绘制。
- `onReady` 之前「开始游戏」按钮保持 disabled。
- 点击开始后按钮立即禁用，防止连点开多局。
- **`SendMessage` 在本构建中是同步的**（2026-10-30 实测）：`onStateChange(Playing)` 可能在
  `sendMessage()` 返回**之前**就回调到页面。因此 H5 必须「先设置自身 UI 状态与兜底计时器，再发送指令」，
  由状态回调取消兜底；反过来会把刚到的状态覆盖掉（M3 收口门禁踩过，见 `docs/M3-执行记录.md` 第 9 节）。

## 5. 错误处理与安全

1. **异常隔离**：每个 `.jslib` 回调都包 `try/catch`；页面未注册对应回调时只 `console.warn`，绝不让 Unity 崩溃。
2. **非法 JSON**：H5 侧 `JSON.parse` 失败只 `console.warn` 并丢弃该事件，不抛到页面。
3. **昵称安全**：只跨边界传 JSON 字符串；H5 渲染昵称一律 `textContent`，禁止 `innerHTML`；空昵称 → 「玩家」，超过 16 个码点截断。
4. **幂等**：Unity 侧 `_reported` 保证每局一次 `onGameOver`；H5 收到重复事件直接忽略。`_reported` 在每次进入 `Playing` 时清零（StartGame / RestartGame / Unity 结算面板重开都会触发）。
5. **编辑器兼容**：`#if UNITY_WEBGL && !UNITY_EDITOR` 之外用 `Debug.Log` 模拟，Editor 可完整跑状态机。
6. **协议版本**：`protocol: 2`。新增字段兼容；修改字段语义必须升版本。H5 收到非 2 只 `console.warn`，不阻塞。
7. **缓存**：`build-info.json` 与 `/unity/Build/*` 必须 no-cache（`netlify.toml` 已配置），否则浏览器会跑旧包；H5 的 js/css 同样 no-cache，并带 `?v=` 版本串。

## 6. 版本历史

| 版本 | 日期 | 变更 |
|---|---|---|
| 1 | 2026-10-30 | M3：移除 M0 的 Ping/Pong 测试接口；新增 StartGame / PauseGame / ResumeGame / RestartGame / SetPausedByVisibility 与 onReady / onStateChange / onGameOver |
| 2 | 2026-11-01 | M6 §7.2：`StartGame` 增加 `difficulty`（非法/缺省 → `normal`）；`onGameOver.payload` 增加 `difficulty`（Unity 实际采用值）；`protocol` 1→2。**状态机不变**（难度是参数不是状态）。计分只加难度倍率（只作用于存活分，胜利奖励固定 200） |

> 2026-10-30（M4）：协议版本仍为 1。`SetPausedByVisibility("false")` 从「立即恢复」改为「暂停面板倒计时后恢复」，
> H5 调用方式不变；嵌入模式下 Unity 不再画结算面板（见 2.1）。
> 2026-10-31（M4.6）：协议版本仍为 1。`SetPausedByVisibility("false")` **不再自动倒数**，只清标记、保持 Paused；
> 3-2-1 改为玩家主动恢复（「继续」/ Esc / P → `RequestCountdownResume`）才触发，`ResumeGame` 也只请求倒计时。
> H5 调用方式与字段不变。
>
> 2026-11-01（M6 第 2 步，中间态版本号）：H5 静态资源的 `?v=` 串提到 `1.1.0`，但 `build-info.json` 的
> `productVersion` 仍是 `1.0.0`（Unity 工程版本未改）。这是**刻意保留的中间态**，最终构建（M6 第 7 步）
> 统一升到 `1.1.0` 并同步 README / acceptance。
