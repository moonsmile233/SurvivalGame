# 开发与实现说明

> 玩家 / 试玩请看 **[README.md](README.md)**。本文面向开发、构建、验收与维护。
> 设计规则与当前实现事实以 [`docs/实现总览.md`](docs/实现总览.md) 为准；协议见
> [`docs/bridge-protocol.md`](docs/bridge-protocol.md)；验收记录见 [`docs/acceptance.md`](docs/acceptance.md)。

## 1. 技术栈

- **Unity 6（`6000.3.22f1`）**，Built-in Render Pipeline，WebGL2 + IL2CPP，
  Brotli + Decompression Fallback（静态服务器未配 header 也能跑）。
- **H5 外壳**：原生 HTML / CSS / JS，无框架、无构建步骤。
- **嵌入方式**：`createUnityInstance` 直接把 canvas 嵌进页面（非 iframe）。
- **桥接**：H5 → Unity 用 `SendMessage('WebGLBridge', method, jsonString)`；
  Unity → H5 通过 `.jslib` 调 `window.SurvivalGameBridge.*`；协议版本 **3**。
  JSON 统一由 `JsonUtility.ToJson` 生成，禁止字符串拼接。
- **版本控制**：Jujutsu（jj），仓库同时保留 `.git` 兼容 GitHub。

## 2. 架构

- 单场景 + `GameManager` 显式状态机：`Menu → Starting(3-2-1) → Playing ⇄ Paused → Result`；
  重开调用 `ResetRound()`，不重载场景。
- Unity 负责完整游戏本体：主菜单、三档难度、开局倒计时、暂停/重开/返回、结算。
- H5 是薄壳：昵称、进入游戏（`EnterGame`）、成绩面板（共享最高分 / 最近一局）、
  localStorage、响应式布局、全屏入口。
- 计分：纯函数 `ScoreService`，每帧按 elapsed 重算，不做浮点累加；
  分段 10/12/15/20 分每秒 + 剩余 HP + 难度倍率 + 胜利 200。
- 敌人 AI：追击（含拦截者）/ 冲锋 / 绕行；软墙分离 + 均匀网格（30Hz 刷新）+
  60Hz 积分 + 速度平滑；唯一 position 写入是 `pos += v*dt`，无每帧分配。
- 字体：TMP + `NotoSansSC-UI-Subset.otf` 子集，构建前按 `UiStrings.AllCharacters`
  自动重烤（75 码位）；禁止 legacy `Text` 画中文。
- 场景由 `Assets/Editor/SceneSetup.cs` 一键重建，不靠手工拖拽。

## 3. 目录

```text
Assets/            Unity 工程（Scripts / Editor / Configs / Fonts / Sprites / Scenes）
web/               H5 静态站点根目录（index.html、css、js、unity 构建产物、start-local.*）
tools/e2e/         Playwright 端到端测试（默认无头；HEADED=1 才开屏幕外窗口）
docs/              文档地图、实现总览、协议、ticket、验收记录、evidence
scripts/           部署脚本（Netlify）
```

## 4. 构建

- Unity `6000.3.22f1`，WebGL Build Support 已安装。
- 编辑器打开时：菜单 **`Survival/Build WebGL`**（本工程 batchmode 会撞工程锁，不保证可用）。

  ```bash
  "D:/unity hub/Editor/Unity.exe" -batchmode -quit \
    -projectPath "D:/unityFile/SurvivalGame" \
    -executeMethod BuildScript.BuildWebGL \
    -logFile "D:/unityFile/SurvivalGame/Logs/build-webgl.log"
  ```

- 产物写入 `web/unity/`：`unity.loader.js` + `*.unityweb`；
  `web/unity/build-info.json` 由 `BuildScript` 扫描实际产物生成，**不要手改**。
- 构建前会自动重烤 TMP 中文字体图集并重建场景；构建后检查画面右下角
  **没有 Unity `trial version` 水印**（许可证失效会出水印，不能发布）。
- `BuildScript.BundleVersion` 与 `web/index.html` 的 `?v=` 必须一致，构建后跑
  `node tools/check-version.mjs` 校验。

## 5. 本地开发

```bash
cd web && python serve.py 8000
```

- `serve.py` 发 `Cache-Control: no-store`，避免改完 H5 仍跑旧脚本；监听 0.0.0.0，
  手机可用局域网 IP 访问。
- **不要双击 `index.html`**：WebGL 不能通过 `file://` 加载。
- 每次改 `web/` 下的 JS/CSS，必须同步 bump `index.html` 里的 `?v=`
  （见 [`docs/协作协议.md`](docs/协作协议.md) §6）。
- 版本自检：`node tools/check-version.mjs`。

## 6. 测试

E2E 默认无头（SwiftShader），不做帧率结论；性能只认有头/真机。

```bash
cd tools/e2e
node score.mjs                 # 计分 7 局
node standalone-menu.mjs       # 菜单/状态机流程
MODE=viewports node standalone-menu.mjs
node h5-shell.mjs              # H5 薄壳
MODE=viewports node h5-shell.mjs
node smoke.mjs                 # 端到端 smoke（9 项）

# 压测（有头，窗口移到屏幕外）
CAPS=1 EXTRA_QUERY="enemyMix=1&enemyLimit=40&walk=1" RUN_LABEL=c7-40 PERF_SECONDS=90 node perf.mjs
```

线上跑给 `BASE_URL` 与 `E2E_PROXY`：

```bash
BASE_URL=https://survivalgame-demo.netlify.app E2E_PROXY=http://127.0.0.1:7897 node smoke.mjs
```

## 7. 版本与交付（v1.3.0）

- `web/unity`：`productVersion 1.3.0`，`builtAt 2026-11-07T13:46:43Z`，总 **9,670,524 B**
  （wasm 6,991,330 / data 2,465,651 / framework 75,760 / loader 117,894）。
- 线上 Netlify：<https://survivalgame-demo.netlify.app>，production deploy
  **`6af049771a4cbd7448877fcc`**；线上 smoke 9/9、console 0。
- GitHub Release **v1.3.0**：<https://github.com/moonsmile233/SurvivalGame/releases/tag/v1.3.0>，
  附件 `SurvivalGame-web-1.3.0.zip` **9,464,620 B**，
  sha256 `b62bd1dbcd7cdea778854257360118cccaf824fd3685eac56d164d7472c61a9d`。
- 回滚点：上一 Netlify 部署 `6ae88c99…`（1.2.1）或 Release `v1.0.0`。
- 部署命令：`bash scripts/deploy-netlify.sh`（需要 Netlify 凭据；外网走 `127.0.0.1:7897` 代理）。
  新站点默认「需登录访问」，需在后台把 Visibility 改成 Public。

## 8. 作业需求对照（17 条）

状态：已实现 = 功能可用且有自测；已验收 = 有日期/设备/证据；未测 = 还没做。
逐条证据与设备矩阵见 [`docs/acceptance.md`](docs/acceptance.md)。

| # | 需求 | 状态 | 日期 / 设备 | 证据 |
|---|---|---|---|---|
| 1 | 2D 场景（玩家/敌人/背景/UI） | 已验收 | 2026-10-30 Chromium | `docs/evidence/m2-browser-playing-1280.png` |
| 2 | 玩家移动（WASD/方向键，不出界） | 已验收 | 2026-10-30 Editor/Chromium | `docs/evidence/m3-move-before.png` |
| 3 | 敌人定时生成并追踪 | 已验收 | 2026-10-30 Chromium | `docs/evidence/m3-hud-enemies.png` |
| 4 | HP、受击掉血、无敌帧 | 已验收 | 2026-10-30 Editor | `docs/M1-执行记录.md` |
| 5 | 生存 60 秒胜利 | 已实现；**用户确认人工 60 秒已完成**（具体难度/胜负/设备待补） | 2026-11-07 用户实测（细节待补） | `docs/evidence/m6-content/c7/` |
| 6 | 失败 | 已验收 | 2026-10-30 线上 Chromium | `docs/evidence/m3-online-result.png` |
| 7 | 重新开始（全量重置） | 已验收 | 2026-10-30 Chromium | `docs/evidence/m3-restarted.png` |
| 8 | 暂停（按钮 / Esc / P / 失焦，恢复 3-2-1） | 已验收 | 2026-11-01 Chrome 桌面/移动视口 | `docs/evidence/e2e-local-chrome-desktop-focus-return-paused.png` |
| 9 | 分数与剩余时间 | 已验收 | 2026-10-30 Editor/线上 | `docs/evidence/m3-online-result.png` |
| 10 | WebGL 构建可在浏览器运行 | 已验收 | 2026-11-01 Chrome | `docs/evidence/e2e-local-chrome-desktop-playing.png` |
| 11 | 昵称输入，H5 显示 | 已验收 | 2026-10-30 线上 | `docs/evidence/m3-online-result.png` |
| 12 | 开始按钮（ready 前禁用） | 已验收 | 2026-10-30 Chromium | `docs/M3-执行记录.md` |
| 13 | 嵌入游戏（非 iframe） | 已验收 | 2026-10-30 Chromium | `web/js/bridge.js` |
| 14 | Unity → H5 传分 | 已验收 | 2026-10-30 线上 | `docs/evidence/m3-online-result.png` |
| 15 | 最高分 localStorage，刷新仍在 | 已验收 | 2026-10-30 线上 | `docs/evidence/e2e-local-chrome-desktop-summary.json` |
| 16 | 页面适配电脑和手机宽度 | 已实现（320～1920 视口无溢出）；**真机安全区：未测** | 2026-11-01 Chrome 视口 | `docs/evidence/resp-320x568-playing.png` |
| 17 | 源码 + WebGL 构建文件 + README | 已验收：1.3.0 构建入库 `web/unity`；Release `v1.3.0`；干净克隆 main 本地可跑 | 2026-11-08 | `docs/acceptance.md` §3.9 |

## 9. 已知技术边界

- **真机未系统验收**：Honor 90 Pro / iPhone 的触控、HUD、安全区、帧率、全屏语义仍待实测；
  现有结论全部来自浏览器视口与自动化测试。
- **性能口径**：30/40/50 只实测 163.4 / 131.6 / 130.9 FPS、p95 7/8/9 ms，受本机 rAF 上限（约 128）影响；
  同构建 40 只未限帧复测 2154 FPS / p95 1 ms，证明不是 CPU 掉帧。不能把 131 FPS 写成“160 已过”。
- alloc 33.8～41.4 KB/s（30/40/50，90s 口径）；低负载基线更高，属环境基线，非敌人代码新增。
- 非 Dash 敌人 minPair p50 约 0.758，到顶后仍有 0.1x 级瞬态；手感以用户试玩为准。
- 人工 60 秒 / 5 局基线：用户确认已完成，具体难度/胜负/设备待补，不代填。

## 10. 文档索引

- [`docs/文档地图.md`](docs/文档地图.md) — 权威顺序与 owner（先看这页）
- [`docs/实现总览.md`](docs/实现总览.md) — 当前实现事实（规则/数值/状态机/输入/构建）
- [`docs/bridge-protocol.md`](docs/bridge-protocol.md) — H5 ↔ Unity 协议 v3
- [`docs/acceptance.md`](docs/acceptance.md) — 验收矩阵、部署与 Release 记录
- [`docs/部署与托管.md`](docs/部署与托管.md) — 托管与离线方案
- [`docs/M6-内容扩展-执行记录.md`](docs/M6-内容扩展-执行记录.md) — v1.3 内容扩展执行记录
- [`docs/协作协议.md`](docs/协作协议.md) — 单一写者、写前重读、checkpoint 规则
