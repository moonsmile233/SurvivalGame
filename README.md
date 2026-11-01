# SurvivalGame

2D 生存挑战：Unity WebGL 渲染、原生 H5 承载，玩家在 36×20 地图上躲开追踪敌人，存活 60 秒获胜。
支持**简单 / 普通 / 困难**三档难度（开局前选，局内可点难度卡中止本局重选）。
昵称、开始、结算与三档最高分都在 H5；分档最高分存浏览器 localStorage。
线上/Release 版本 **1.0.0**，构建时间 **2026-10-31T14:53:47Z**；仓库内 `web/unity` 可能是更新的 WIP 构建
（M6 三档难度，`2026-11-01T12:44:35Z`，尚未部署），**以 `web/unity/build-info.json` 为准**。

## 试玩 / 下载

- **国内在线试玩：待定（未上线）**。国内托管（腾讯云 CloudBase）尚未执行，手机 4G 直连能进游戏的链接**还不存在**。
- **海外备份**：<https://survivalgame-demo.netlify.app>（Netlify；国内网络可能打不开或很慢。
  线上已是 **1.0.0** 构建 `2026-10-31T14:53:47Z`，deploy `6ae6d3d1…`）。
- **离线包**：<https://github.com/moonsmile233/SurvivalGame/releases/tag/v1.0.0> 的
  `SurvivalGame-web-1.0.0.zip`（9,433,571 字节，sha256 `dccc7bf650f9e54508fbe03f93c90bebbf1398a0b61d0680bbe3635149a2e799`）。
  解压后双击 `start-local.bat`（Windows）或运行 `sh start-local.sh`，打开 <http://localhost:8000>。
  **不要双击 `index.html`**：WebGL 不能通过 `file://` 加载。
- Release 只是离线备份/交付物，**不是游戏运行必需**；在线玩不需要下载。

## 操作

| 平台 | 移动 | 暂停 / 恢复 | 全屏 |
|---|---|---|---|
| 桌面 | `WASD` / 方向键（也可按住鼠标拖动，兼容路径，不显示摇杆） | `Esc` 或 `P`；失焦自动暂停，切回保持暂停 | 主界面/局内都有「全屏」；全屏内按 `Esc` 退出 |
| 手机 | 在画面上拖动（浮动摇杆可视化：按下点即中心，外圈只是视觉参考，不是满速边界） | 游戏内右上「暂停」；恢复先走 3 秒倒计时 | 主界面/局内都有「全屏」；全屏内用系统手势/返回退出；建议横屏 |

- 失焦 / 切到其他 App 自动暂停；切回**不会自动恢复**，需要点「继续」或按 `P`，再等 3-2-1。
- 手机浏览器不进全屏也能玩；iPhone Safari 网页全屏受限，会提示横屏或添加到主屏幕。
- **主界面和局内都能进全屏**（结算不提供）；主界面全屏保留难度/最高分/开始。
- **游戏中（Playing/Paused）全屏里没有任何网页端按钮**（没有「退出全屏」），退出用 `Esc`（桌面）
  或浏览器手势/返回（手机）；画布内的 Unity 暂停按钮仍可用。

## 难度

| 档位 | 敌人 | 同屏上限 | 计分倍率（只乘存活分） | 60 秒胜利分数 |
|---|---|---|---|---|
| 简单 | 更慢、生成更稀 | 14 | ×1.0 | 800 |
| 普通 | 标准（默认） | 20 | ×1.3 | 980 |
| 困难 | 更快、生成更密、伤害 14 | 20 | ×1.6 | 1160 |

- 开始与重新开始都有 3 秒倒计时；倒计时中点难度卡会取消开始。
- 难度卡常驻：Ready 只切换；Playing/Paused/Result 点卡会**中止本局**（不计分、不写最高分）并回主菜单选新档。
- 最高分按难度分开保存（`survival.bestScore.easy|normal|hard`）；旧版单一最高分自动迁到普通档（只迁一次）。
- 全屏下（Playing/Paused/Result）难度卡隐藏，换档需先退出全屏。
- 页面允许纵向滚动（菜单常驻）；滚轮在画布上也能滚页，拖动摇杆不会带动页面。

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

> **改了 H5 看不到变化？** 浏览器可能缓存了旧的 `app.js`/`style.css`。
> 用无痕窗口或 `Ctrl+F5` 硬刷新；开发时请用 `serve.py`（`start-local` 默认）——它发 `no-store`。
> 每次改 `web/` 下的 JS/CSS，必须同步 bump `index.html` 里的 `?v=`（见 `docs/协作协议.md` §6）。

## 构建

Unity `6000.3.22f1`，Built-in Render Pipeline，WebGL + IL2CPP，Brotli + Decompression Fallback。

- 编辑器打开时：菜单 `Survival/Build WebGL`。
- 编辑器关闭时：

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

| # | 需求 | 状态 | 日期 / 设备 | 证据 |
|---|---|---|---|---|
| 1 | 2D 场景（玩家/敌人/背景/UI） | 已验收 | 2026-10-30 Chromium | `docs/evidence/m2-browser-playing-1280.png` |
| 2 | 玩家移动（WASD/方向键，不出界） | 已验收 | 2026-10-30 Editor/Chromium | `docs/evidence/m3-move-before.png` |
| 3 | 敌人定时生成并追踪 | 已验收 | 2026-10-30 Chromium | `docs/evidence/m3-hud-enemies.png` |
| 4 | HP、受击掉血、无敌帧 | 已验收 | 2026-10-30 Editor | `docs/M1-执行记录.md` |
| 5 | 生存 60 秒胜利 | 已实现（自动化 60.0s / 800 分）；**人工 60 秒：未测** | 2026-10-31 Chrome（脚本走位） | `docs/evidence/m4-win-automated-local.png` |
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
| 17 | 源码 + WebGL 构建文件 + README | 已验收 | 2026-11-01 build `f19fcb79` + Release `v1.0.0` + 干净克隆 | `docs/M5-执行记录.md` |

未测项：人工 60 秒胜利、5 局平衡基线、Honor 90 Pro / iPhone 真机（触控/HUD/安全区/帧率）。
设备矩阵与逐条证据见 `docs/acceptance.md`。

## 已知限制

- 单局 60 秒；无音效、无正式美术（圆点 + 网格占位）。
- iPhone Safari 对网页全屏支持有限，会提示「横屏或添加到主屏幕」。
- Netlify 在国内访问不稳定；国内托管未上线。
- 暂停恢复需要 3-2-1 是刻意设计（手机切回时需要把手移回屏幕），不是 bug。
- 没有攻击/击杀手段，分数 = 存活分 × 难度倍率 + 胜利奖励（60 秒无伤胜利：简单 800 / 普通 980 / 困难 1160）。

## 更多文档

先看 **`docs/文档地图.md`**（权威顺序与各文档 owner），常用：
`docs/实现总览.md`（当前事实）、`docs/bridge-protocol.md`（H5↔Unity 协议）、
`docs/acceptance.md`（验收矩阵）、`docs/部署与托管.md`（托管方案）、`docs/M5-执行记录.md`（交付数据）。
