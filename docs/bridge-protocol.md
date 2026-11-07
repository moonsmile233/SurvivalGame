# H5 ↔ Unity 通信协议（protocol 3）

> 代码与文档同步维护：`Assets/Scripts/Bridge/WebGLBridge.cs` + `WebGLBridge.jslib`、`web/js/bridge.js`、`web/js/app.js`。
> 设计依据：`docs/项目规范.md` 第 7 节；v1.2 游戏优先方案见
> `docs/proposals/proposal-M6-重构-游戏优先-v1.2.md` §7 与 `proposal-M6-重构-第一阶段-实施计划.md`。
> 本文件是**实现契约**：改代码必须同步。

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
| `EnterGame` | JSON `{"nickname":"...","lastDifficulty":"easy\|normal\|hard"}`（字段均可空；旧页可发 `difficulty` 别名） | 无（Unity 默认在 Menu） | 进入游戏；昵称只保存不渲染；难度只作菜单初始选中，**不自动开局**；解析顺序 `lastDifficulty ?? difficulty ?? normal`，非法/缺省 → `normal`；`Menu` 下幂等 |
| `SetPausedByVisibility` | `"true"` / `"false"` | `Playing → Paused`；`Starting → 来源态` | 失焦兜底（环境信号，不是局内操作）。`true` 暂停/取消倒计时；`false` **直接忽略**（Unity 没有标记；H5 自己按最后发送值去重），保持 Paused、不自动恢复（M4.6）。与 Unity 原生失焦幂等 |
| `StartGame`（v2 旧） | v2 JSON / 纯昵称 | 无 | **按 `EnterGame` 处理**，不自动开局；v2 的 `difficulty` 当 `lastDifficulty`（旧页选档仍生效）；打一次 warning。旧 H5 只算过渡 |
| `PauseGame` / `ResumeGame` / `RestartGame` / `ReturnToMenu`（v2 旧） | `""` | 无 | **no-op**，一次性 warning；v1.2 起局内操作全部在 Unity UI |

状态守卫（由 `GameManager` 保证，桥接层只转发）：

- `EnterGame` 不改状态；Unity 独立运行时没人调用也能从 Menu 正常游玩。
- `PauseGame` 只在 `Playing` 生效；`Resume` 由 Unity 内部按钮/快捷键触发。
- 暂停恢复、重开、再来一局、菜单开始**全部走 Unity 内部 3-2-1**（见 §4.1）。

### 2.1 难度偏好（v3，2026-11-01 用户确认）

- 难度**只在 Unity 菜单选**；`lastDifficulty` 由 H5 代存、`EnterGame` 时回传，仅作初始选中。
- Unity 菜单选择变化 → `onDifficultyChanged`；H5 存 `survival.difficulty`，**不提供选择 UI**。
- `EnterGame` 回填难度时 Unity **不回发** `onDifficultyChanged`（Bridge 抑制），避免循环。
- 没有 H5（standalone）时每次启动默认普通。

### 2.2 结算归属（v1.2）

- **结算归 Unity**：ResultPanel 在 WebGL 里也显示；H5 只更新数据面板（最高分/最近一局）。
- 删除 v1 的 `IsEmbedded` 抑制；`onGameOver` 仍是 H5 记分的唯一数据源。

## 3. Unity → H5

`.jslib` 通过 `[DllImport("__Internal")]` 调用，统一入口 `window.SurvivalGameBridge`：

| 事件 | 载荷 | 时机 |
|---|---|---|
| `onReady` | `{"protocol":3,"capabilities":["enterGame","stateChange","gameOver","difficultyChanged"]}` | 首帧渲染完成后一次 |
| `onStateChange` | `{"protocol":3,"state":"menu\|playing\|paused\|result"}` | 每次**业务态**变化；`Starting` 是内部过渡态，不发 |
| `onDifficultyChanged` | `{"protocol":3,"difficulty":"easy\|normal\|hard"}` | 菜单选择变化时一次（EnterGame 回填不回发） |
| `onGameOver` | 见下方 schema | `Result`（胜利/失败），每局仅一次（`_reported` 守卫） |

`onGameOver` 载荷（协议仍为 3；C6 只做**增量字段**，H5 可忽略未知字段）：

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

- `result`：`"win"` | `"lose"`。
- `score`：唯一权威最终分 = `round((存活分 + 血量分) × 难度倍率) + 200`（胜利奖励不乘倍率）。
- `survivedSeconds`：保留 1 位小数；胜利固定为回合时长（默认 60）。
- `kills`：无击杀手段，恒为 0（字段保留、不参与公式）。
- `nickname`：来自 `EnterGame`，只回传不渲染到 Unity。
- `difficulty`：Unity **实际采用**的档位，H5 以它记分。
- `hitCount`（C6 增量）：本局真正扣血的次数（无敌期忽略的不计）。
- `breakdown`（C6 扩展）：`survival` / `health` 是**乘倍率后取整**的分量；`multiplier` 是本局倍率；
  `winBonus` 仅胜利时 200。由于分别取整，`survival + health` 可能与 `score - winBonus` 差 1，**以 `score` 为准**。
  H5 不展示明细，只记 `score`。

### 3.1 状态字符串

| 内部枚举 | 线格式 | H5 用途 |
|---|---|---|
| `Menu` | `menu` | 数据面板文案（“可开始”） |
| `Starting` | **不发** | 内部 3-2-1 |
| `Playing` | `playing` | 数据面板文案 |
| `Paused` | `paused` | 数据面板文案 |
| `Result` | `result` | 数据面板文案；结算以 `onGameOver` 为准 |

## 4. 时序

### 4.1 Unity 内部（3-2-1 归属）

```text
Menu ──点开始──▶ Starting(3s, unscaled, timeScale=0) ──▶ Playing
Paused ──继续──▶  Starting(3s) ──▶ Playing（不重置本局）
Paused ──重新开始──▶ Starting(3s) ──▶ Playing（倒计时结束才 ResetRound）
Result ──再来一局──▶ Starting(3s) ──▶ Playing（同档，倒计时结束才 ResetRound）
Starting ──失焦/取消──▶ 回来源态（Menu/Paused/Result），timeScale: Menu=1、其余=0
Playing ──HP≤0 / 60s──▶ Result（发 onStateChange(result) + onGameOver 一次）
Paused/Result ──返回菜单──▶ Menu（不结算、不发 onGameOver）
```

- 倒计时中再次失焦：取消并回来源态；切回**不自动恢复/开局**。
- 倒计时中 Esc/P：忽略（沿用 M4.6“重复按 P 不加速”）。
- `Starting` 期间 `Time.timeScale=0`，刷怪/敌人/计时全部冻结。

### 4.2 H5 侧

```text
页面加载
  └─ 注册 window.SurvivalGameBridge（onReady/onStateChange/onGameOver/onDifficultyChanged）
  └─ fetch build-info.json → 加载 loader → createUnityInstance
Unity 首帧
  └─ onReady({"protocol":3,"capabilities":[...]}) → H5 进 Shell（遮罩改为「游戏已就绪」并继续挡住画布）
点击 H5「开始游戏」
  └─ SendMessage EnterGame({"nickname","lastDifficulty"}) → 画布内 Unity 菜单
     └─ 玩家在画布内选难度 → onDifficultyChanged → H5 存 survival.difficulty
     └─ 玩家点画布内「开始游戏」→ Unity Starting(3-2-1) → onStateChange(playing)
窗口失焦 / 标签页隐藏
  └─ Unity OnApplicationFocus(false) 自动暂停（H5 也可发 SetPausedByVisibility("true") 兜底）
窗口恢复
  └─ 保持 Paused，不自动恢复；玩家点画布内「继续」→ 3-2-1 → playing
HP ≤ 0 / 60s
  └─ onStateChange(result) + onGameOver（一次）→ H5 更新最高分/最近一局
玩家点「返回菜单」
  └─ onStateChange(menu)
```

- H5 **不轮询** Unity 状态；不发送局内操作指令。
- `onReady` 之前 H5 入口保持 disabled。
- **`SendMessage` 是同步的**：状态回调可能在 `sendMessage()` 返回前发生；H5 要先设置自身状态再发送
  （M3 收口踩过，见 `docs/M3-执行记录.md` 第 9 节）。
- `Starting` 不产生状态事件，H5 的数据面板在开局前保持“menu”文案即可（下一阶段实现）。

## 5. 错误处理与安全

1. **异常隔离**：每个 `.jslib` 回调都包 `try/catch`；页面未注册对应回调时只 `console.warn`（`onDifficultyChanged`
   是可选项，未注册时静默跳过），绝不让 Unity 崩溃。
2. **非法 JSON**：H5 侧 `JSON.parse` 失败只 `console.warn` 并丢弃；Unity 侧解析失败回落空昵称/普通难度。
3. **昵称安全**：只跨边界传 JSON 字符串；H5 渲染昵称一律 `textContent`，禁止 `innerHTML`；空昵称 → 「玩家」，
   超过 16 个码点截断（Unity 侧再截 64）。
4. **幂等**：Unity 侧 `_reported` 保证每局一次 `onGameOver`；每次进 `Playing` 清零。
5. **编辑器兼容**：`#if UNITY_WEBGL && !UNITY_EDITOR` 之外用 `Debug.Log` 模拟，Editor 可完整跑状态机。
6. **协议版本**：`protocol: 3`。H5 收到非 3 **禁用开始并显示错误卡「版本不匹配，请刷新重试」**，不进入 Shell/Entered
   （新 H5 配旧 Unity 会进入一个没有菜单的壳，因此必须显式中止）；新增字段兼容。
7. **缓存**：`build-info.json` 与 `/unity/Build/*` 必须 no-cache（`netlify.toml` 已配置）；
   H5 的 js/css 同样 no-cache，并带 `?v=` 版本串（v1.2 起统一 `1.2.0`）。

## 6. 版本历史

| 版本 | 日期 | 变更 |
|---|---|---|
| 1 | 2026-10-30 | M3：移除 Ping/Pong；新增 StartGame / PauseGame / ResumeGame / RestartGame / SetPausedByVisibility 与 onReady / onStateChange / onGameOver |
| 2 | 2026-11-01 | M6 §7.2：`StartGame` 增加 `difficulty`；`onGameOver.payload` 增加 `difficulty`；新增 `ReturnToMenu`；`protocol` 1→2 |
| 3 | 2026-11-01 | **v1.2 游戏优先**：Unity 拥有菜单/难度/开局/暂停/重开/返回/结算；H5→Unity 只保留 `EnterGame`（+ 旧指令兼容）；`onReady` 加能力位；`onStateChange` 改为 `menu/playing/paused/result`（`Starting` 不发）；新增 `onDifficultyChanged` 与 `EnterGame.lastDifficulty`（H5 代存难度）；删除 `IsEmbedded`，结算归 Unity |

> 2026-10-30（M4）：协议版本仍为 1。`SetPausedByVisibility("false")` 从“立即恢复”改为“暂停面板倒计时后恢复”。
> 2026-10-31（M4.6）：协议版本仍为 1。`SetPausedByVisibility("false")` 不再自动倒数，只清标记、保持 Paused。
> 2026-11-01（v3）：所有进入 Playing 的路径统一由 Unity 走 3-2-1；H5 不再有局内操作。
