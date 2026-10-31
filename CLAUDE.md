# SurvivalGame — 2D 生存挑战（Unity WebGL + H5）

> 文档版本：2026-10-31T20:30+08 · 作者：策划 · 改前必须从磁盘重读；上下文旧副本作废。
> 协作与存档规则见 `docs/协作协议.md`（单一写者、写前重读、每步 checkpoint）。

## 一句话
玩家在 2D 场景中移动、躲避追踪敌人，存活 60 秒获胜。Unity 构建 WebGL，由 H5 页面承载，H5 显示并保存最高分。

## 文档索引
- **文档地图（先看这页）：`docs/文档地图.md`** — 权威顺序、各文档 owner/状态
- **实现总览（当前事实）：`docs/实现总览.md`** — 规则/状态/输入/敌人/UI/构建/验收一页式说明
- 完整设计规范：`docs/项目规范.md`（原始设计 + 背景；部分内容已被实现总览取代）
- 当前任务：**M5 交付（`docs/tickets/M5.md`）**；M0~M4 主体已完成，M4 执行记录见 `docs/M4-执行记录.md`
- 通信协议：`docs/bridge-protocol.md`（实现契约，改代码必须同步）
- **执行前先看：`docs/遗留问题.md`**（输入后端、构建产物、字体与测试环境注意事项）
- **玩法增强（M4 P1/P2）：`docs/玩法增强.md`**
- **托管与离线：`docs/部署与托管.md`**（WebGL 不能 `file://` 双击；国内托管选项）
- **协作协议：`docs/协作协议.md`**（单一写者、写前重读、每步 checkpoint）
- **参考拆解：`docs/参考拆解.md`**（入口/主菜单/难度/全屏/暂停恢复；新 UI 前先拆 3～5 个同类）
- **体验审查：`docs/体验审查.md`**（对照同类主动审，收口前跑一遍；发现先记录再决定进不进 ticket）
- **入口/主菜单/全屏（可选）：`docs/tickets/M4.5-入口主菜单与全屏.md`**
- **触控摇杆可视化（P1）：`docs/tickets/M4.7-触控摇杆可视化.md`**
- **测试门槛：`docs/测试计划.md`；验收矩阵：`docs/acceptance.md`**

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
- 构建耗时：约 4 分钟（M3 实测 3:46~4:01）；产物约 9 MB（含 TMP 中文字体图集）
- 构建方式验证情况：**菜单 `Survival/Build WebGL` 已实测成功**；batchmode 命令路径已核对，但**尚未实测**（编辑器开着时无法跑，会撞工程锁）
- 发布：Netlify，线上 <https://survivalgame-demo.netlify.app>，部署命令 `bash scripts/deploy-netlify.sh`。
  新站点默认「需登录访问」，必须在后台 **Project configuration → General → Visitor access → Visibility** 改成 **Public**
- npm 默认缓存 `D:\nodejs\node_cache` 当前用户不可写，`npx` 需带 `npm_config_cache="D:/agent使用学习/浏览器相关/.npm-cache"`

## 当前里程碑：M5 交付（2026-10-31 用户决策：M5 交付优先）

M0（打通链路）、M1（核心玩法）、M2（表现层）、M3（H5 闭环）、M4 主体（移动端/大地图/相机/敌人手感/键盘暂停）
均已完成。M4 已落地：

- 移动端 P0：锚点相对触控、HUD 参考分辨率切换、结算只保留 H5 一套、失焦暂停 + 3-2-1、DPR 上限 2。
- **B0：大地图 36×20 + CameraFollow + 玩家边界改为地图 + 视野外生成/回收**。
- **B1：拥挤修复（站位环 + 两两位置修正）、速度扰动/压力曲线、离屏 3 秒回收、桌面 `Esc`/`P` 暂停**；
  B1 构建已部署并线上 smoke 通过；§1.7 键盘暂停构建本地 E2E 通过、尚未部署。
- `tools/e2e` Playwright smoke 已有（线上 Chrome/Edge/Firefox PASS）。

**当前主线 = `docs/tickets/M5.md`**：⓪ **M4.6 P0**（失焦保持暂停、玩家继续后 3-2-1 再恢复、结算卡片完整可见，
见 `docs/tickets/M4.6-暂停恢复与结算呈现.md`）+ **M4.7**（浮动摇杆可视化，只加视觉不改逻辑）→
① 推齐 origin + 部署（已做 §1.7 保险版本）；
② 国内托管（CloudBase 先试，用户决定放到最后）+ 手机真机；③ 最终构建入库 + Release zip + 干净克隆验证；
④ README/验收/交付记录；⑤ 人工 60 秒胜利 + 5 局基线。

- 真机、人工 60 秒、5 局基线、国内托管仍未完成；**不要宣称 M4 收口**，以 M5 出口为准。
- M4.5 决策（2026-10-31）：§2 暂停菜单、§3 主菜单+难度**不阻塞交付**；§1 H5 全屏仅在 M5-1 通过后可选（2 小时盒）。
- 交付报告与 Release zip 属 M5-3/M5-4，按 `M5.md` 做，不要提前。

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

- **新玩法 / 新 UI / 新入口流程：先更新 `docs/参考拆解.md`（3～5 个同类作品），再写 ticket/代码**；
  不要只用"需求没写"当不做的理由。参考同类是默认动作，不是加分项。
- **按设计对象选择参考源**：玩法/数值参考同品类游戏；网页交互/暂停/全屏/触控/加载参考 Web/WebGL 游戏外壳；
  性能/包体参考引擎与浏览器官方文档；后端/部署/缓存参考行业技术资料。不要用一两个游戏覆盖所有决策，
  每个维度至少 2～3 个来源，并在文档里写明参考的是哪一类对象。

只有以下情况停下来等用户确认：

1. **网络 / 代理 / MCP / 凭据问题**：先按 `127.0.0.1:7897` 试一次；仍失败就停，报告卡在哪一步、完整报错、已尝试什么、建议选项。**禁止**擅自改直连、换端口、静默重试。
2. **需要用户在 Unity 里操作**：重启编辑器、装模块、导入 TMP Essentials、关闭工程锁、点编辑器菜单。直接说清楚点哪里，然后等。
3. **破坏性或不可逆操作**：删资源、覆盖用户文档、force push、reset。
4. **任务包与规范 / 实际环境冲突**，或验收标准不明确。
5. **改变架构 / 范围 / 交付物**：新增第三方包、改需要重启的 ProjectSettings、换技术方案。

停下时不要只问「可以吗」，要给：现象 + 报错 + 已尝试 + 2~3 个选项和你的建议。
MCP 连不上时优先走文件 + batchmode；仍不行就停，不要猜着改 ProjectSettings。

## 代码注释约定

注释写给"下一个改这段代码的人/Agent"，只写代码本身看不出来的信息：

- 写 **why / 不变量 / 约束 / 非显而易见的时序**；不要逐行复述代码。
- 类与公共方法用 XML `summary`；事件写清触发时机（如 `OnDamaged` 只在实际扣血时触发）。
- 必须注释的地方：WebGL 受限点、`SceneSetup` 的资产卸载顺序、`Time.timeScale` 冻结、暂停/重开时序、`Update` 无分配约束、`SendMessage` 单参数与 JSON 边界。
- 不留会过期的注释（如"M2 临时"）；TODO 写清条件，不留裸 TODO。
- 注释与代码同一次改动更新；**过期注释比没有注释更糟**。
- 不写注释掉的死代码；不为了"注释多"而加注释。

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

## 版本与发布（小步 checkpoint，不再攒到里程碑）

origin：`https://github.com/moonsmile233/SurvivalGame.git`（已确认）。
协作与存档的完整规则见 `docs/协作协议.md`；以下是执行摘要。

1. **每个逻辑步骤一个提交**（相机/B0、拥挤修复、压力曲线、素材、E2E 各自一个），
   不要 A + B0 + B1 塞一个。
2. 步骤完成、构建/自测通过后：

   ```bash
   jj describe -m "feat(m4-b1): ..."
   jj bookmark set wip -r @
   jj git push --bookmark wip
   jj bookmark set main -r @
   jj git push --bookmark main
   jj new
   ```

3. **`main` 只放已验证的检查点；`wip` 放最新工作状态**（允许未验收，用于 GitHub 备份）。
   未验证先只推 `wip`，不动 `main`；不要"改一大堆再存一个"。
4. 失败/半成品用 `jj undo` / `jj restore` 回退；不要 force push。
5. 推送前 `jj git fetch`；远端分叉就停，报告，禁止 `--force`。
6. 首次 push 会弹 Git Credential Manager 授权；之后凭据由系统记住，不需要重复登录。
7. M5 之前 `web/unity/` 保持 ignore；M5 最终构建单独一个 `build:` 提交，并同步 GitHub Release。

## 汇报格式（每个 checkpoint；里程碑另附完整验收清单）

1. 改了哪些文件（路径列表）+ 相关文档版本行
2. 构建结果（成功/失败 + 日志路径）
3. 本步自测证据（截图/日志/命令）
4. 遗留问题 / 需要人决策的点
5. `jj diff --stat`
