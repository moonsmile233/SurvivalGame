# SurvivalGame — 2D 生存挑战（Unity WebGL + H5）

## 一句话
玩家在 2D 场景中移动、躲避追踪敌人，存活 60 秒获胜。Unity 构建 WebGL，由 H5 页面承载，H5 显示并保存最高分。

## 文档索引
- 完整设计规范：`docs/项目规范.md`（实现前先读第 5、6、7、11 节）
- 当前任务：**M2（`docs/tickets/M2.md`）**；M0、M1 已完成（`docs/tickets/M0.md`、`docs/M0-执行记录.md`；`docs/tickets/M1.md`、`docs/M1-执行记录.md`）
- 通信协议：规范第 7 节（M0 通过后抽成 `docs/bridge-protocol.md`）
- **执行前先看：`docs/遗留问题.md`**（输入后端、autoStart、临时暂停键等）
- **玩法增强（排 M4，不得挤占 M3/M5）：`docs/玩法增强.md`**

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
- 构建耗时：约 5~6 分钟；产物约 9 MB（M2，含 TMP 中文字体图集）
- 构建方式验证情况：**菜单 `Survival/Build WebGL` 已实测成功**；batchmode 命令路径已核对，但**尚未实测**（编辑器开着时无法跑，会撞工程锁）
- 发布：Netlify，线上 <https://survivalgame-demo.netlify.app>，部署命令 `bash scripts/deploy-netlify.sh`。
  新站点默认「需登录访问」，必须在后台 **Project configuration → General → Visitor access → Visibility** 改成 **Public**
- npm 默认缓存 `D:\nodejs\node_cache` 当前用户不可写，`npx` 需带 `npm_config_cache="D:/agent使用学习/浏览器相关/.npm-cache"`

## 当前里程碑：M2 已完成（2026-10-30），下一步 M3

M0（打通链路）、M1（核心玩法）、M2（表现层）均已完成，执行记录见
`docs/M0-执行记录.md`、`docs/M1-执行记录.md`、`docs/M2-执行记录.md`。

- M2 内容：中文 HUD（生命/时间/分数）、暂停面板、结算面板、重新开始、受击红闪、胜负冻结；
  TMP + 内置 CJK 子集字体（`Assets/Fonts/`，图集由 `Editor/FontAssetBuilder.cs` 脚本生成，构建前自动重烤）。
- Editor + 浏览器（Playwright）均已验证；线上 <https://survivalgame-demo.netlify.app> **仍是 M1 构建**，M2 尚未部署。
- M3 之前不要做 H5 桥接、昵称、最高分、触控、音效。
- 下一步 M3（H5 闭环）是交付硬要求；敌人分离/速度扰动/回收/近战变体一律排 M4，见 `docs/玩法增强.md`，**不得挤占 M3/M5**。

## 硬约束
1. **一个状态一个作者**：静态结构（相机、Canvas、玩家、生成器）放在场景里；动态物体（敌人、特效、飘字）运行时生成。禁止两边各生成一份。
2. **场景可重建**：`Assets/Editor/SceneSetup.cs` 必须能一键生成 `Assets/Scenes/Game.unity`；禁止只靠手工拖拽。
3. **跨边界只传 JSON 字符串**；`SendMessage` 一次只能传一个 string 参数。
4. **游戏内 UI 用中文 + 数字**，必须走 TMP + 项目内置 CJK 子集字体（`Assets/Fonts/NotoSansSC-UI-Subset.otf` → TMP Font Asset）；禁止用 `LegacyRuntime.ttf` / `UnityEngine.UI.Text` 画中文。昵称仍只在 H5 渲染。
5. **不引入第三方包**，不用 URP，不改模板，不加新 Input System。
6. 每完成一个可运行节点就构建一次 WebGL，不要攒到最后。
7. 环境事实与本文件冲突时，以本文件为准。
8. 规范第 4 节目的目录结构里没有 `unity/` 这一层——**本项目根目录就是 Unity 工程根**。

## 工作方式（默认自主，何时停下）

默认：在任务包和规范范围内**自己拍板**，不要为每个实现细节征求确认；把关键选择和理由写进汇报。

只有以下情况停下来等用户确认：

1. **网络 / 代理 / MCP / 凭据问题**：先按 `127.0.0.1:7897` 试一次；仍失败就停，报告卡在哪一步、完整报错、已尝试什么、建议选项。**禁止**擅自改直连、换端口、静默重试。
2. **需要用户在 Unity 里操作**：重启编辑器、装模块、导入 TMP Essentials、关闭工程锁、点编辑器菜单。直接说清楚点哪里，然后等。
3. **破坏性或不可逆操作**：删资源、覆盖用户文档、force push、reset。
4. **任务包与规范 / 实际环境冲突**，或验收标准不明确。
5. **改变架构 / 范围 / 交付物**：新增第三方包、改需要重启的 ProjectSettings、换技术方案。

停下时不要只问「可以吗」，要给：现象 + 报错 + 已尝试 + 2~3 个选项和你的建议。
MCP 连不上时优先走文件 + batchmode；仍不行就停，不要猜着改 ProjectSettings。

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

## 版本与发布（每个里程碑收口固定执行）

origin：`https://github.com/moonsmile233/SurvivalGame.git`（远端初始为空，已确认）。

1. 构建 WebGL + 完成本里程碑自测；构建失败不发布。
2. `jj describe -m "feat(mX): ..."`（Conventional Commits）
3. `jj bookmark set main -r @`
4. `jj git push --bookmark main`
5. `jj new` 开下一个里程碑

- 首次 push 会弹 Git Credential Manager 浏览器授权；之后凭据由系统记住，不需要重复登录。
- 推送前先 `jj git fetch`；若远端出现与本仓库不一致的历史，**停止并报告**，禁止 `--force`。
- 不自动推送未验收的中间状态；只有里程碑通过才 push。
- M5 之前 `web/unity/` 保持 ignore；M5 最终构建单独一个 `build:` 提交，并同步 GitHub Release。

## 汇报格式（每个里程碑结束时）
1. 改了哪些文件（路径列表）
2. 构建结果（成功/失败 + 日志路径）
3. 验收清单逐条勾选结果
4. 遗留问题 / 需要人决策的点
