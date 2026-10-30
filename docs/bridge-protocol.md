# H5 ↔ Unity 通信协议（protocol 1）

> 代码与文档同步维护：`Assets/Scripts/Bridge/WebGLBridge.cs` + `WebGLBridge.jslib`、`web/js/bridge.js`、`web/js/app.js`。
> 设计依据：`docs/项目规范.md` 第 7 节；本文件是 M3 的实现契约。

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
| `StartGame` | JSON `{"nickname":"..."}` | Ready → Playing | 昵称只保存，不进入 Unity UI；JSON 解析失败或传纯字符串时按空/纯昵称处理 |
| `PauseGame` | `""` | Playing → Paused | H5 手动暂停；会清除「因隐藏而暂停」标记 |
| `ResumeGame` | `""` | Paused → Playing | H5 手动继续；会清除标记 |
| `RestartGame` | `""` | Won/Lost → Playing | 全量重置（血量、计时、分数、敌人、玩家位置），昵称沿用 |
| `SetPausedByVisibility` | `"true"` / `"false"` | Playing ↔ Paused（条件） | `true`：仅在 Playing 时暂停并记标记；`false`：仅当标记为真且状态为 Paused 时恢复 |

状态守卫（由 `GameManager` 保证，桥接层只转发）：

- `StartGame` 只接受 `Ready`；其他状态直接忽略并打 warning。
- `RestartGame` 只接受 `Won` / `Lost`。
- 手动暂停后切标签页再回来：不会自动恢复（只有自动暂停才自动恢复）。

## 3. Unity → H5

`.jslib` 通过 `[DllImport("__Internal")]` 调用，统一入口 `window.SurvivalGameBridge`：

| 事件 | 载荷 | 时机 |
|---|---|---|
| `onReady` | `{"protocol":1}` | 首帧渲染完成后一次（`GameBootstrap` 发，桥接层 `_readySent` 兜底） |
| `onStateChange` | `{"protocol":1,"state":"Playing"}` | 每次状态变化；`state` ∈ `Ready / Playing / Paused / Won / Lost` |
| `onGameOver` | 见下方 schema | Won/Lost，每局仅一次（`_reported` 守卫） |

`onGameOver` 载荷：

```json
{
  "protocol": 1,
  "event": "gameOver",
  "payload": {
    "result": "win",
    "score": 800,
    "survivedSeconds": 60.0,
    "kills": 0,
    "nickname": "阿宝",
    "breakdown": {
      "survival": 600,
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

## 4. 时序

```text
页面加载
  └─ 注册 window.SurvivalGameBridge（含 onReady/onStateChange/onGameOver）
  └─ fetch build-info.json → 加载 loader → createUnityInstance
Unity 首帧
  └─ onReady({"protocol":1}) → H5 隐藏遮罩、启用「开始游戏」
点击开始
  └─ SendMessage StartGame({"nickname"}) → Playing
     └─ onStateChange(Playing) → H5 隐藏开始按钮、禁用昵称输入
标签页隐藏
  └─ SendMessage SetPausedByVisibility("true") → Paused（若在 Playing）
标签页恢复
  └─ SendMessage SetPausedByVisibility("false") → 仅恢复自动暂停的那次
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
6. **协议版本**：`protocol: 1`。新增字段兼容；修改字段语义必须升版本。
7. **缓存**：`build-info.json` 与 `/unity/Build/*` 必须 no-cache（`netlify.toml` 已配置），否则浏览器会跑旧包；H5 的 js/css 同样 no-cache，并带 `?v=` 版本串。

## 6. 版本历史

| 版本 | 日期 | 变更 |
|---|---|---|
| 1 | 2026-10-30 | M3：移除 M0 的 Ping/Pong 测试接口；新增 StartGame / PauseGame / ResumeGame / RestartGame / SetPausedByVisibility 与 onReady / onStateChange / onGameOver |
