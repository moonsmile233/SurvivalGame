# SurvivalGame — 60 秒 2D 生存挑战

Unity WebGL 渲染、原生 H5 承载的网页小游戏。你在 36×20 的地图上移动，躲开三种敌人，**存活 60 秒获胜**。
没有攻击、没有装备，唯一的武器是走位。

- **直接在线玩**：<https://survivalgame-demo.netlify.app>
- **下载离线包**：<https://github.com/moonsmile233/SurvivalGame/releases/tag/v1.3.0>
- **源码**：<https://github.com/moonsmile233/SurvivalGame>

> 在线版是 **v1.3.0**。Netlify 是海外托管：海外/代理访问较快，**国内网络可能很慢或打不开**；
> 国内托管（腾讯云 CloudBase）尚未上线，届时会在本 README 更新链接。国内玩不了时，用下面的离线包或本地运行。

---

## 直接在线玩

| 方式 | 地址 | 说明 |
|---|---|---|
| 电脑 / 手机浏览器 | <https://survivalgame-demo.netlify.app> | 免安装，打开即玩；首次加载约 9 MB，页面有进度条 |
| 手机 | 同上 | 建议横屏；点过「开始游戏」后页面会出现「全屏」按钮 |

- 支持桌面 Chrome / Edge / Firefox，手机 Chrome / Safari（视口已验 320×568 ～ 1920×1080，**真机仍待测**）。
- 手机浏览器不进全屏也能玩；iPhone Safari 对网页全屏支持有限，会提示横屏或添加到主屏幕。

## 玩法

### 目标

- 单局 **60 秒**，活到最后即胜利；生命值归零则失败。
- 被敌人碰到掉血（受伤后有 1 秒无敌时间），**玩家不能攻击、不能击杀**，只能躲。
- 玩家可以穿过敌人，但穿过时会掉血——不要贴着人群走。

### 操作

| 平台 | 移动 | 暂停 / 恢复 | 全屏 |
|---|---|---|---|
| 电脑 | `WASD` / 方向键（也可按住鼠标拖动） | 游戏内「暂停」按钮，或 `Esc` / `P` | 进入游戏后点页面上的「全屏」；全屏内按 `Esc` 退出 |
| 手机 | 在画面上拖动（按下点就是摇杆中心，外圈只是视觉参考，不是满速边界） | 游戏内右上角「暂停」 | 进入游戏后点「全屏」；退出用系统手势 / 返回键；建议横屏 |

- 失焦 / 切到其他 App 会自动暂停；**切回来不会自动继续**，需要点「继续」或按 `P`。
- 继续、重新开始、再来一局、菜单开局都会先走 **3-2-1 倒计时**（给手机玩家把手移回屏幕的时间，刻意设计，不是卡顿）。
- 全屏内不显示任何网页端按钮，退出全屏用 `Esc`（桌面）或浏览器手势（手机）；画布内的 Unity 暂停按钮仍可用。

### 一局怎么开始

1. 打开页面，输入昵称（可留空，默认「玩家」），点 **「开始游戏」** —— 这一步是进入游戏。
2. 画布内进入 Unity 主菜单，选难度（简单 / 普通 / 困难），点 **「开始游戏」**。
3. 3-2-1 后开局；结算界面可以「再来一局」或「返回菜单」。
4. 最高分跨难度共享，保存在浏览器 localStorage，刷新仍在；昵称也会记住。

> 所以完整流程是**两次点击**：H5「开始游戏」进入 Unity，再在画布内选难度开局。
> 难度、暂停、重开、返回菜单、结算全部在 Unity 画布内，H5 只负责昵称 / 进入 / 成绩面板 / 全屏入口。

### 三种敌人

三种敌人的轮廓相同，靠颜色和行为区分：

| 敌人 | 颜色 | 行为 | 怎么躲 |
|---|---|---|---|
| 追击者 Chaser | 粉色 | 直线追你；其中约一半是**拦截者**，会绕到你前进方向的前方堵路，再回头贴上来 | 变向、别一直直线跑 |
| 冲锋者 Charger | 红色 | 进入 7 单位内会**前摇 0.45 秒（变黄）**，然后锁定方向直线冲刺 7 单位；困难档冲刺速度 5.0，比玩家快 | 看到变黄就横向侧步，冲刺中不会转向 |
| 绕行者 Flanker | 紫色 | 绕到你的侧后方 3 单位，靠近后短距离扑击；扑空会重新绕 | 别被围住，及时换方向 |

- 敌人只做近身判定，没有任何远程弹幕。
- 同屏数量有上限：简单 14 / 普通 30 / 困难 50；到上限会回收视野外最远的敌人并补位，不会出现“突然不刷怪”。

### 难度

难度在 **Unity 主菜单**选，局中不能换（换档需要暂停 →「返回菜单」）。三档胜利条件都是存活 60 秒。

| 档位 | 敌人 | 同屏上限 | 计分倍率 | 60 秒满血胜利分数 |
|---|---|---|---|---|
| 简单 | 更慢、生成更稀，伤害更低 | 14 | ×1.0 | 1155 |
| 普通（默认） | 标准 | 30 | ×1.3 | 1442 |
| 困难 | 更快、生成更密，伤害更高，冲锋更快 | 50 | ×1.6 | 1728 |

### 计分

```
存活分：按生存时间分段累计（0.1 秒粒度，不是整秒跳字）
  0–15 秒   10 分/秒 → 满段 150
  15–30 秒  12 分/秒 → 满段 180
  30–45 秒  15 分/秒 → 满段 225
  45–60 秒  20 分/秒 → 满段 300
  满 60 秒合计 855

血量分 = 结算时剩余 HP × 1（0～100）
最终分 = round((存活分 + 血量分) × 难度倍率) + 胜利奖励 200
```

- 游戏内 HUD 的「分数」只显示 `存活分 × 难度倍率`，约每 0.1 秒更新；结算界面显示最终分和明细「存活分 · 血量分 · 受击」。
- 计分不含击杀（玩家没有攻击手段）。最高分跨难度共享。

---

## 从 GitHub 下载游玩（离线包，推荐国内使用）

Release 里有打包好的静态站点，**不需要安装 Unity**，解压起一个本地静态服务器即可。

1. 打开 Release 页面：<https://github.com/moonsmile233/SurvivalGame/releases/tag/v1.3.0>
2. 下载 `SurvivalGame-web-1.3.0.zip`
   （9,464,620 字节，sha256 `b62bd1dbcd7cdea778854257360118cccaf824fd3685eac56d164d7472c61a9d`）。
3. 解压到任意目录。
4. 启动本地服务：
   - **Windows**：双击 `start-local.bat`
   - **macOS / Linux**：在解压目录执行 `sh start-local.sh`
   - 或者手动：`python serve.py 8000`（推荐，带 `Cache-Control: no-store`）
5. 浏览器打开 <http://localhost:8000>。
6. 手机想玩：手机和电脑连同一个 Wi-Fi，访问 `http://<电脑局域网 IP>:8000`

要求本机有 **Python 3 或 Node.js**（启动脚本会自动挑一个可用方式）。

> **不要直接双击 `index.html`**：Unity WebGL 不能通过 `file://` 加载，会白屏或报错。
> 也可以从仓库页 `Code → Download ZIP` 下载源码包，解压后同样在 `web/` 目录里启动。

## 从 GitHub 克隆运行（仓库已含构建产物）

```bash
git clone https://github.com/moonsmile233/SurvivalGame.git
cd SurvivalGame/web
# Windows：双击 start-local.bat；macOS / Linux：sh start-local.sh
# 或手动：
python serve.py 8000
```

打开 <http://localhost:8000> 即可。仓库里的 `web/unity/` 就是 v1.3.0 的完整 WebGL 构建，
克隆后**不需要 Unity 也能直接玩**。

- 版本自检：`node tools/check-version.mjs`（校验 `web/index.html` 的 `?v=` 与 `web/unity/build-info.json` 一致）。
- 改了 H5 的 JS/CSS 看不到变化：用无痕窗口或 `Ctrl+F5`；开发时用 `serve.py`（它发 `no-store`）。
  每次改 `web/` 下的资源，记得同步 bump `index.html` 里的 `?v=`。

## 自己构建（可选，需要 Unity）

- Unity **6000.3.22f1**，Built-in Render Pipeline，WebGL + IL2CPP，Brotli + Decompression Fallback。
- 编辑器打开时：菜单 **`Survival/Build WebGL`**（本工程 batchmode 会撞工程锁，不保证可用）。
- 构建前会自动重烤 TMP 中文字体图集，并用 `SceneSetup` 重建场景；产物写入 `web/unity/`。
- 构建后确认画面右下角**没有 Unity `trial version` 水印**（许可证失效时会出水印，不能发布）。

## 技术栈 / 版本

- Unity 6 WebGL（`6000.3.22f1`）+ 原生 HTML/CSS/JS，无框架、无构建步骤。
- 架构：单场景 + `GameManager` 显式状态机；Unity 负责游戏本体（菜单/难度/暂停/重开/结算），
  H5 是薄壳（昵称/进入/成绩/localStorage/全屏）。
- 桥接：`createUnityInstance` 直接嵌 canvas（非 iframe）；H5→Unity 用 `SendMessage` 传 JSON，
  Unity→H5 通过 `.jslib` 回调；协议版本 3。
- 当前版本：**1.3.0**，构建时间 `2026-11-07T13:46:43Z`，整包约 9.67 MB。
- 线上：Netlify（deploy `6af04977…`）；离线：GitHub Release `v1.3.0`。

## 目录

```text
Assets/            Unity 工程（Scripts / Editor / Configs / Fonts / Sprites / Scenes）
web/               H5 静态站点根目录（index.html、css、js、unity 构建产物、start-local.*）
tools/e2e/         Playwright 端到端测试（默认无头）
docs/              文档地图、实现总览、协议、验收记录、执行记录、evidence
```

## 作业需求对照（17 条）

状态：已实现 = 功能可用且有自测；已验收 = 有日期/设备/证据；未测 = 还没做。
逐条证据与设备矩阵见 `docs/acceptance.md`。

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
| 17 | 源码 + WebGL 构建文件 + README | 已验收：1.3.0 构建入库 `web/unity`；Release `v1.3.0`；干净克隆 main 本地可跑 | 2026-11-08 | `docs/acceptance.md` §3.9、<https://github.com/moonsmile233/SurvivalGame/releases/tag/v1.3.0> |

未测项：Honor 90 Pro / iPhone 真机（触控 / HUD / 安全区 / 帧率）；人工 60 秒与 5 局基线由用户确认完成，具体数据待补。

## 已知限制

- 单局 60 秒；无音效、无正式美术（圆点 + 网格占位）。
- **真机尚未系统验收**：目前只有浏览器视口与无头 E2E 证据，Honor 90 Pro / iPhone 的手感、安全区、帧率仍待实测。
- Netlify 在国内访问不稳定；国内托管未上线。
- 暂停恢复需要 3-2-1 是刻意设计（手机切回时要把手移回屏幕），不是 bug。
- 没有攻击 / 击杀手段：分数 = `round((分段存活分 + 剩余 HP) × 难度倍率) + 胜利 200`。

## 更多文档

先看 **`docs/文档地图.md`**（权威顺序与各文档 owner）。常用：

- `docs/实现总览.md` — 当前实现事实（规则 / 数值 / 状态机 / 输入 / 构建）
- `docs/acceptance.md` — 验收矩阵、部署与 Release 记录（§3.9）
- `docs/bridge-protocol.md` — H5 ↔ Unity 协议 v3
- `docs/部署与托管.md` — 托管与离线方案
- `docs/M6-内容扩展-执行记录.md` — v1.3 内容扩展的执行记录
