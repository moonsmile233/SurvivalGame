# SurvivalGame — 2D 生存挑战（Unity WebGL + H5）

## 一句话
玩家在 2D 场景中移动、躲避追踪敌人，存活 60 秒获胜。Unity 构建 WebGL，由 H5 页面承载，H5 显示并保存最高分。

## 文档索引
- 完整设计规范：`docs/项目规范.md`（实现前先读第 5、6、7、11 节）
- 当前任务：M0 已完成（`docs/tickets/M0.md`、`docs/M0-执行记录.md`）；M1 ticket 待提供
- 通信协议：规范第 7 节（M0 通过后抽成 `docs/bridge-protocol.md`）

## 环境（已确认，不要猜）
- 工程根 = 仓库根：`D:\unityFile\SurvivalGame`
- Unity：`6000.3.22f1`，可执行文件在 `D:\unity hub\Editor\Unity.exe`（2026-10-29 核对：`metadata.hub.json` 的 revisionHash `1c726e1fb402` 与 `ProjectVersion.txt` 一致）
- 模板：2D (Built-In Render Pipeline)；平台已切换到 WebGL
- WebGL Build Support：已安装
- 系统：Windows 11；Shell：Git Bash；Python 3.13 / Node 24 可用
- 外网访问走代理 `http://127.0.0.1:7897`
- UnityMCP 已配置在 `http://127.0.0.1:8081/mcp`（8080 是别的项目）：**有就用，没有就走文件 + batchmode，M0 不依赖它**
- 构建产物实际文件名（Unity 6 + Brotli + Decompression Fallback，**是 `.unityweb` 后缀**，不是 `.data.br`）：
  - `web/unity/Build/unity.loader.js`
  - `web/unity/Build/unity.data.unityweb`
  - `web/unity/Build/unity.framework.js.unityweb`
  - `web/unity/Build/unity.wasm.unityweb`
  - `web/unity/build-info.json` 由 `BuildScript` **扫描实际产物生成**，不要手改；换 Unity 版本后重跑构建即可自适应
- 构建耗时：约 6 分钟；产物约 8 MB（M0 空场景）
- 构建方式验证情况：**菜单 `Survival/Build WebGL` 已实测成功**；batchmode 命令路径已核对，但**尚未实测**（编辑器开着时无法跑，会撞工程锁）
- 发布：Netlify，线上 <https://survivalgame-m0.netlify.app>，部署命令 `bash scripts/deploy-netlify.sh`。
  新站点默认「需登录访问」，必须在后台 **Project configuration → General → Visitor access → Visibility** 改成 **Public**
- npm 默认缓存 `D:\nodejs\node_cache` 当前用户不可写，`npx` 需带 `npm_config_cache="D:/agent使用学习/浏览器相关/.npm-cache"`

## 当前里程碑：M1（未开始）
M0（打通链路）已于 2026-10-29 完成并通过验收，执行记录见 `docs/M0-执行记录.md`。
M1 ticket 尚未提供，**不要自行开始 M1**。

## 硬约束
1. **一个状态一个作者**：静态结构（相机、Canvas、玩家、生成器）放在场景里；动态物体（敌人、特效、飘字）运行时生成。禁止两边各生成一份。
2. **场景可重建**：`Assets/Editor/SceneSetup.cs` 必须能一键生成 `Assets/Scenes/Game.unity`；禁止只靠手工拖拽。
3. **跨边界只传 JSON 字符串**；`SendMessage` 一次只能传一个 string 参数。
4. **游戏内 UI 只用英文 + 数字**（WebGL 中文字体不可靠）；中文文案放在 H5。
5. **不引入第三方包**，不用 URP，不改模板，不加新 Input System。
6. 每完成一个可运行节点就构建一次 WebGL，不要攒到最后。
7. 环境事实与本文件冲突时，以本文件为准。
8. 规范第 4 节目的目录结构里没有 `unity/` 这一层——**本项目根目录就是 Unity 工程根**。

## 构建与预览

编辑器关闭时（batchmode）：

```bash
"D:/unity hub/Editor/Unity.exe" -batchmode -quit \
  -projectPath "D:/unityFile/SurvivalGame" \
  -executeMethod BuildScript.BuildWebGL \
  -logFile "D:/unityFile/SurvivalGame/Logs/build-webgl.log"
```

编辑器打开时：菜单 `Survival/Build WebGL`。

> 注意：batchmode 构建时 Unity 不能打开这个工程（工程锁），否则直接失败。

预览：

```bash
cd "D:/unityFile/SurvivalGame/web" && python -m http.server 8000
```

打开 http://localhost:8000

## M0 完成后的目录形态

```text
SurvivalGame/
├── CLAUDE.md
├── Assets/
│   ├── Scenes/Game.unity
│   ├── Scripts/
│   │   ├── Core/GameBootstrap.cs
│   │   └── Bridge/WebGLBridge.cs + WebGLBridge.jslib
│   └── Editor/SceneSetup.cs + BuildScript.cs
├── web/
│   ├── index.html
│   ├── css/style.css
│   ├── js/bridge.js + app.js
│   └── unity/            ← 构建产物 + build-info.json
├── docs/
└── README.md
```

## 汇报格式（每个里程碑结束时）
1. 改了哪些文件（路径列表）
2. 构建结果（成功/失败 + 日志路径）
3. 验收清单逐条勾选结果
4. 遗留问题 / 需要人决策的点
