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

## 版本管理

仓库使用 **Jujutsu (`jj`)**，colocate 模式（同时保留 `.git`，可直接推 GitHub）。

```bash
jj status                 # 看改动
jj log                    # 看历史
jj describe -m "说明"      # 给当前改动写提交说明
jj new                    # 完结当前改动，开下一个
jj bookmark set main -r @ # 把 main 指到当前改动
```

`.gitignore` 已排除 `Library/`、`Temp/`、`Obj/`、`Logs/`、`UserSettings/`、`*.csproj`、`*.sln`
等可重建内容；Unity 会自己重新生成它们，**不要提交**。

首次连接 GitHub：

```bash
jj git remote add origin <你的仓库地址>
jj git push --bookmark main
```

## 发布到 GitHub Pages（尚未配置）

`web/unity/`（构建产物）默认不入库，所以直接把仓库挂 Pages 是没内容的。两种做法：

1. **提交构建产物**（推荐，简单）：删掉 `.gitignore` 里的 `/web/unity/` 一行，
   重新 `jj status` / 提交；然后仓库 Settings → Pages 选择 `web/` 目录作为站点根。
2. **CI 构建**：用 GitHub Actions 在流水线里跑 `BuildScript.BuildWebGL`，
   需要配置 Unity License secret，配置成本明显更高。

无论哪种，H5 入口都是 `web/index.html`；`build-info.json` 由构建生成，不要手工改。

## 约定

- 游戏内 UI 只用英文 + 数字；中文文案放在 H5。
- 跨边界只传 JSON 字符串；`SendMessage` 每次只能传一个参数。
- 场景静态结构（相机、Canvas、玩家、生成器）放场景里，由 `SceneSetup` 生成；
  动态物体运行时生成。
- 通信协议详见 `docs/项目规范.md` 第 7 节（M1 时抽成 `docs/bridge-protocol.md`）。
