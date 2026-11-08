# SurvivalGame

2D 生存挑战：Unity WebGL 渲染、原生 H5 承载，玩家在 36×20 地图上躲开三种敌人（追击 / 冲锋 / 绕行），
存活 60 秒获胜。**Unity 是完整游戏**：主菜单、三档难度、开局 3-2-1、暂停/重开/返回、结算都在画布内；
H5 只做昵称、进入游戏、数据面板（共享最高分/最近一局）、localStorage 与全屏入口。
仓库当前是 **v1.3 内容扩展**（`web/unity` 已入库 `productVersion 1.3.0`，
`builtAt 2026-11-07T13:46:43Z`），已部署 Netlify 并发布 GitHub Release `v1.3.0`；版本以
`web/unity/build-info.json` 为准。

## 试玩 / 下载

- **国内在线试玩：待定（未上线）**。国内托管（腾讯云 CloudBase）尚未执行，手机 4G 直连能进游戏的链接**还不存在**。
- **海外备份**：<https://survivalgame-demo.netlify.app>（Netlify；国内网络可能打不开或很慢。
  线上已是 **1.3.0** 构建 `2026-11-07T13:46:43Z`，deploy `6af049771a4cbd7448877fcc`；
  完整线上 smoke 9/9 PASS，回滚用上一部署 `6ae88c99…`（1.2.1）或 Release v1.0.0）。
- **离线包（v1.3.0）**：<https://github.com/moonsmile233/SurvivalGame/releases/tag/v1.3.0> 的
  `SurvivalGame-web-1.3.0.zip`（9,464,620 字节，sha256 `b62bd1dbcd7cdea778854257360118cccaf824fd3685eac56d164d7472c61a9d`）。
  v1.0.0 的 `SurvivalGame-web-1.0.0.zip` 仍保留作回滚/备份。
  解压后双击 `start-local.bat`（Windows）或运行 `sh start-local.sh`，打开 <http://localhost:8000>。
  **不要双击 `index.html`**：WebGL 不能通过 `file://` 加载。
- Release 只是离线备份/交付物，**不是游戏运行必需**；在线玩不需要下载。

## 操作

| 平台 | 移动 | 暂停 / 恢复 | 全屏 |
|---|---|---|---|
| 桌面 | `WASD` / 方向键（也可按住鼠标拖动，兼容路径，不显示摇杆） | 游戏内「暂停」或 `Esc`/`P`；失焦自动暂停，切回保持暂停 | 先点 H5「开始游戏」进入，再点「全屏」；全屏内按 `Esc` 退出 |
| 手机 | 在画面上拖动（浮动摇杆可视化：按下点即中心，外圈只是视觉参考，不是满速边界） | 游戏内右上「暂停」；继续先走 3 秒倒计时 | 进入后点「全屏」；全屏内用系统手势/返回退出；建议横屏 |

- **页面布局**：画布在上，昵称 / 开始 / 成绩卡在下（桌面与手机一致）。
- **H5 的「开始游戏」= 进入游戏**：画布切到 Unity 主菜单，难度与开局在画布内完成（两次点击）。
- 失焦 / 切到其他 App 自动暂停；切回**不会自动恢复**，需要点画布内「继续」或按 `P`，再等 3-2-1。
- 手机浏览器不进全屏也能玩；iPhone Safari 网页全屏受限，会提示横屏或添加到主屏幕。
- **全屏入口只在进入游戏后出现**；全屏内没有任何网页端按钮/控件（没有「退出全屏」），退出用 `Esc`（桌面）
  或浏览器手势/返回（手机）；画布内的 Unity 暂停按钮仍可用。
- 难度、开局、暂停、重开、返回菜单、结算全部在 Unity 画布内，H5 不提供这些控件。

## 难度

| 档位 | 敌人 | 同屏上限 | 计分倍率（乘存活分 + 血量分） | 60 秒满血胜利分数 |
|---|---|---|---|---|
| 简单 | 更慢、生成更稀 | 14 | ×1.0 | 1155 |
| 普通 | 标准（默认） | 30 | ×1.3 | 1442 |
| 困难 | 更快、生成更密、伤害更高 | 50 | ×1.6 | 1728 |

- 难度在 **Unity 主菜单**选；暂停重开、结算「再来一局」、返回菜单都沿用当前档，不弹选择。
- 开始、继续、重新开始、再来一局都先走 3 秒倒计时。
- 最高分**跨难度共享**（`survival.bestScore`，取历史最高）；旧版三档最高分在首次加载时取最大值迁移一次，
  旧键只读不删。
- 页面允许纵向滚动；滚轮在画布上也能滚页，拖动摇杆不会带动页面。

## 本地运行（仓库已含构建产物）

```bash
git clone https://github.com/moonsmile233/SurvivalGame.git
cd SurvivalGame/web
# Windows：双击 start-local.bat；macOS / Linux：sh start-local.sh
# 或者手动（推荐 serve.py：带 Cache-Control: no-store）：
python serve.py 8000
```

打开 <http://localhost:8000>（手机用本机局域网 IP，如 `http://192.168.1.100:8000/`）。
需要 Python 3 或 Node；**直接双击 `index.html` 会失败**（`file://` 无法加载 Unity WebGL）。
首次加载约 9 MB，页面有进度条。

> **v1.3 版本**：仓库内 `web/unity` 已是 1.3.0（protocol 3），本地按上面的方式启动即可直接玩；
> `node tools/check-version.mjs` 校验 H5 `?v=` 与构建版本一致。线上 Netlify 已是 1.3.0
> （deploy `6af04977…`），GitHub Release 为 `v1.3.0`；v1.0.0 保留作回滚。

> **改了 H5 看不到变化？** 浏览器可能缓存了旧的 `app.js`/`style.css`。
> 用无痕窗口或 `Ctrl+F5` 硬刷新；开发时请用 `serve.py`（`start-local` 默认）——它发 `no-store`。
> 每次改 `web/` 下的 JS/CSS，必须同步 bump `index.html` 里的 `?v=`（见 `docs/协作协议.md` §6）。

## 构建

Unity `6000.3.22f1`，Built-in Render Pipeline，WebGL + IL2CPP，Brotli + Decompression Fallback。

- **常规路径**：编辑器打开时菜单 `Survival/Build WebGL`（本工程 batchmode 会撞工程锁，不保证可用）。

  ```bash
  "D:/unity hub/Editor/Unity.exe" -batchmode -quit \
    -projectPath "D:/unityFile/SurvivalGame" \
    -executeMethod BuildScript.BuildWebGL \
    -logFile "D:/unityFile/SurvivalGame/Logs/build-webgl.log"
  ```

- 产物在 `web/unity/Build/`（`unity.loader.js` + `*.unityweb`）；`web/unity/build-info.json` 由 `BuildScript` 扫描生成，**不要手改**。
- 构建前会自动重烤 TMP 中文字体图集，并用 `SceneSetup` 重建 `Assets/Scenes/Game.unity`；场景与字体无需手拖。
- 构建后检查画面右下角没有 Unity `trial version` 水印（许可证失效时会出水印，不能发布）。

## 目录

```text
Assets/            Unity 工程（Scripts / Editor / Configs / Fonts / Scenes）
web/               H5 静态站点根目录（index.html、css、js、unity 构建产物、start-local.*）
tools/e2e/         Playwright E2E（默认无头，HEADED=1 才开屏幕外窗口）
docs/              文档地图、规范、ticket、执行记录、验收、evidence
```

## 需求对照（17 条）

状态：已实现 = 功能可用且有自测；已验收 = 有日期/设备/证据；未测 = 还没做。
下表证据属于**线上 1.0.0 时期**；v1.2（Unity 菜单/状态机 + H5 薄壳，`web/unity` 1.2.1）证据见
`docs/evidence/m6-refactor/`，已部署 Netlify、**未发 Release**。

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
| 16 | 页面适配电脑和手机宽度 | 已实现（320～1920 视口）；**真机安全区：未测** | 2026-11-01 Chrome 视口 | `docs/evidence/resp-320x568-playing.png` |
| 17 | 源码 + WebGL 构建文件 + README | 已实现：1.3.0 构建已入库；Release v1.3.0（zip sha256 已核对） | 2026-11-07 build `13:46:43Z` | `docs/M6-内容扩展-执行记录.md`、<https://github.com/moonsmile233/SurvivalGame/releases/tag/v1.3.0> |

未测项：Honor 90 Pro / iPhone 真机（触控/HUD/安全区/帧率）；人工 60 秒/5 局由用户确认完成，具体数据待补。
设备矩阵与逐条证据见 `docs/acceptance.md`。

## 已知限制

- 单局 60 秒；无音效、无正式美术（圆点 + 网格占位）。
- iPhone Safari 对网页全屏支持有限，会提示「横屏或添加到主屏幕」。
- Netlify 在国内访问不稳定；国内托管未上线。
- 暂停恢复需要 3-2-1 是刻意设计（手机切回时需要把手移回屏幕），不是 bug。
- 没有攻击/击杀手段：分数 = round((分段存活分 + 剩余 HP) × 难度倍率) + 胜利 200
  （60 秒满血胜利：简单 1155 / 普通 1442 / 困难 1728）。

## 更多文档

先看 **`docs/文档地图.md`**（权威顺序与各文档 owner），常用：
`docs/实现总览.md`（当前事实）、`docs/bridge-protocol.md`（H5↔Unity 协议）、
`docs/acceptance.md`（验收矩阵）、`docs/部署与托管.md`（托管方案）、`docs/M5-执行记录.md`（交付数据）。
