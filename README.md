# SurvivalGame

2D 生存挑战：Unity WebGL 构建，由 H5 页面承载。玩家存活 60 秒获胜，H5 显示并保存最高分。

**当前状态：M0（打通链路）已完成** —— 空场景 → WebGL 构建 → H5 加载 → 双向一条消息。
玩家、敌人、计分、HUD 美术、音效属于 M1/M2，尚未开始。

## 目录

```text
SurvivalGame/
├── Assets/
│   ├── Scenes/Game.unity          # 由 SceneSetup 一键生成，不要手改
│   ├── Scripts/Core/GameBootstrap.cs
│   ├── Scripts/Bridge/WebGLBridge.cs + WebGLBridge.jslib
│   └── Editor/SceneSetup.cs + BuildScript.cs
├── web/                           # H5 外壳（静态站点根目录）
│   ├── index.html
│   ├── css/style.css
│   ├── js/bridge.js  js/app.js
│   └── unity/                     # WebGL 构建产物 + build-info.json
└── docs/                          # 规范与 ticket
```

## 本地运行

```bash
cd "D:/unityFile/SurvivalGame/web"
python -m http.server 8000
```

打开 <http://localhost:8000>。必须用 HTTP 服务，直接双击 `index.html` 会因 `file://` 跨域失败。

页面按钮「测试通信」会调用 `SendMessage('WebGLBridge', 'Ping', 'hello-from-h5')`，
Unity 回显后页面显示 `Pong: hello-from-h5`。

## 构建

Unity 版本：`6000.3.22f1`，可执行文件：`D:\unity hub\Editor\Unity.exe`（本机实际路径）。

编辑器已打开本工程时，菜单：

```text
Survival/Setup Scene     # 重建 Assets/Scenes/Game.unity
Survival/Build WebGL     # 构建到 web/unity
```

编辑器关闭时（batchmode；**编辑器开着时 batchmode 会因工程锁失败**）：

```bash
"D:/unity hub/Editor/Unity.exe" -batchmode -quit \
  -projectPath "D:/unityFile/SurvivalGame" \
  -executeMethod BuildScript.BuildWebGL \
  -logFile "D:/unityFile/SurvivalGame/Logs/build-webgl.log"
```

菜单构建的日志在 `%LOCALAPPDATA%\Unity\Editor\Editor.log`；batchmode 用上面 `-logFile` 指定的路径。

### 构建产物命名（以实际产物为准）

本机 Unity 6 + Brotli + 开启 Decompression Fallback，产物是旧式 `.unityweb` 命名：

```text
web/unity/Build/unity.loader.js
web/unity/Build/unity.data.unityweb
web/unity/Build/unity.framework.js.unityweb
web/unity/Build/unity.wasm.unityweb
```

`web/unity/build-info.json` 由 `BuildScript` **扫描实际产物生成**，不要手改；
H5 读取它来决定加载哪些文件。若换 Unity 版本导致文件名变化，重跑构建即可。

> Decompression Fallback 开着，因此静态服务器不需要配置 `Content-Encoding: br`，
> 由 Unity 的 JS 解压兜底（首次加载略慢）。代价是体积比纯 brotli 直传略大。

## 验收（M0）

- [x] `Survival/Setup Scene` 生成 `Assets/Scenes/Game.unity`，重复执行不报错
- [x] Play 模式左上角显示 `Unity Ready`，Console 有 `[Bridge] ready`
- [x] WebGL 构建成功，`web/unity/build-info.json` 的四个 URL 都指向真实文件
- [x] 浏览器打开页面 → 显示 `Unity Ready`
- [x] 点「测试通信」→ 页面显示 `Pong: hello-from-h5`
- [x] 浏览器 Console 无 error（Playwright 采集到 `[]`；此前唯一的 404 是浏览器自动请求 `/favicon.ico`，已用空 icon 声明消除）
- [x] Playwright 能读到 `Unity Ready` 并点到 `Pong: hello-from-h5`（Chrome channel，截图存 `docs/evidence/m0-playwright.png`）

## 约定

- 游戏内 UI 只用英文 + 数字；中文文案放在 H5。
- 跨边界只传 JSON 字符串；`SendMessage` 每次只能传一个参数。
- 场景静态结构（相机、Canvas、玩家、生成器）放场景里，由 `SceneSetup` 生成；
  动态物体运行时生成。
- 通信协议详见 `docs/项目规范.md` 第 7 节（M1 时抽成 `docs/bridge-protocol.md`）。
