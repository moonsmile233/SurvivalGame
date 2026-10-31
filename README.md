# SurvivalGame

2D 生存挑战：Unity WebGL 构建，由 H5 页面承载。玩家存活 60 秒获胜，H5 显示并保存最高分。

## 当前状态

- **M0（打通链路）已完成**（2026-10-29）：空场景 → WebGL 构建 → H5 加载 → 双向一条消息
- **M1（核心玩法）已完成**（2026-10-29）：移动、生成、追踪、掉血、计时、胜负、重开；Editor + 浏览器（Playwright）已验证
- **M2（表现层）已完成**（2026-10-30）：中文 HUD（生命/时间/分数）、暂停面板、结算面板、重新开始、受击红闪、胜负冻结；TMP + 内置 CJK 子集字体
- **M3（H5 闭环）已完成**（2026-10-30）：昵称 → 开始 → 结算 → 最高分（localStorage）→ 刷新仍在；标签页隐藏自动暂停；协议见 `docs/bridge-protocol.md`
- **M4（移动端 / 大地图 / UI / 敌人手感）基本完成**（2026-10-31）：
  全屏锚点拖动触控、手机 HUD/面板放大（DPR 上限 2）、结算只保留 H5 一套、操作提示、
  失焦保持暂停；点「继续」/按 Esc/P 主动恢复后 3-2-1 再开始、大地图 36×20 + 相机跟随、
  敌人站位环/位置修正/速度扰动/压力曲线/视野外回收、结算卡片首屏完整可见、E2E smoke（含自动走位 60 秒）。
  真机（iOS/Android）、人工平衡数据与正式美术仍未做，见 `docs/M4-执行记录.md` 第 6 节。
- **线上地址**：<https://survivalgame-demo.netlify.app>（M4.6 构建，Unity `2026-10-31T10:02Z`，H5/CSS `0.1.0-m4.5`，
  deploy `6ae5bd3b…`；Netlify 国内直连较慢，国内主链接按 M5 换 CloudBase）
- 验收证据与偏差记录：`docs/M0-执行记录.md`、`docs/M1-执行记录.md`、`docs/M2-执行记录.md`、`docs/M3-执行记录.md`、`docs/M4-执行记录.md`

**构建**（编辑器打开时点菜单；关闭时用 batchmode，详见下文「构建」）：

```text
Survival/Setup Scene        # 重建 Assets/Scenes/Game.unity（缺字体时自动生成）
Survival/Rebuild Font Asset # 重烤 TMP 中文字体图集（构建时会自动执行，一般不用手动点）
Survival/Build WebGL        # 构建到 web/unity
```

> `Survival/Build WebGL` 会先重烤字体、再用 `SceneSetup` 重建一次场景，然后才出包：
> 改文案、改场景结构都不用担心「忘了同步」。

**预览**：

```bash
cd "D:/unityFile/SurvivalGame/web" && python -m http.server 8000
# 打开 http://localhost:8000
```

## 目录

```text
SurvivalGame/
├── Assets/
│   ├── Scenes/Game.unity          # 由 SceneSetup 一键生成，不要手改
│   ├── Fonts/                     # NotoSansSC-UI-Subset.otf + ui-chars.txt + 生成的 TMP SDF 图集
│   ├── Configs/                   # cfg_GameConfig.asset + def_Enemy_Chaser.asset
│   ├── Scripts/                   # Core / Player / Enemy / UI / Bridge
│   ├── TextMesh Pro/              # TMP Essentials（一次性导入）
│   └── Editor/SceneSetup.cs + BuildScript.cs + FontAssetBuilder.cs
├── web/                           # H5 外壳（静态站点根目录）
│   ├── index.html
│   ├── css/style.css
│   ├── js/bridge.js  js/app.js  js/storage.js
│   └── unity/                     # WebGL 构建产物 + build-info.json
├── tools/e2e/                     # Playwright E2E smoke（不进入 web/，不参与发布）
└── docs/                          # 规范、ticket、执行记录、evidence
```

## 本地运行

```bash
cd "D:/unityFile/SurvivalGame/web"
python -m http.server 8000
```

打开 <http://localhost:8000>。必须用 HTTP 服务，直接双击 `index.html` 会显示「请用本地服务器打开」的提示（`file://` 无法加载 Unity WebGL）。

页面流程（M3）：

```text
加载 Unity → onReady → 输入昵称 → 开始游戏 → 玩 → 结算（本次得分 / 历史最高分 / 新纪录）
                                                  └─ 重新开始 → 新一局
刷新页面 → 最高分与上次昵称从 localStorage 读回
```

- 桌面：WASD / 方向键移动；也可以按住鼠标拖动。
- 手机：在游戏画面上**全屏拖动**移动（锚点相对，不画虚拟摇杆），`touch-action: none` 不会带着页面滚动。
- 地图 36×20，比一屏大；相机平滑跟随玩家并停在地图边缘。
- 昵称空 → 「玩家」；最多 16 个字符；渲染一律 `textContent`，不会执行 HTML/脚本。
- 最高分只在 `score > best` 时更新，相等不算新纪录。
- 窗口失焦 / 标签页隐藏会自动暂停；切回**不会瞬时恢复**，暂停面板显示 3-2-1 倒计时，结束才继续。
  手动暂停不会被自动恢复。
- 浏览器禁用 localStorage 时游戏仍可玩，页面会提示成绩无法保存。
- 结算：WebGL 页面里只显示 H5 卡片（Unity 内部结算面板在嵌入模式下隐藏）；Editor 里仍能看到 Unity 面板。

## E2E 测试（M4）

`tools/e2e/` 是独立的 Playwright smoke，不进入 `web/`、不参与发布：

```bash
cd tools/e2e
npm install
node smoke.mjs                                             # 本地起服务 + 系统 Chrome，3 局
BASE_URL=https://survivalgame-demo.netlify.app node smoke.mjs   # 打线上
MOBILE=1 node smoke.mjs                                    # 390×844 + 真实触摸拖动
```

覆盖：加载 → 昵称 → 开始 → **真实窗口失焦** → 已暂停 → 切回 3-2-1 恢复 → 结算 → 重开 → 刷新最高分，
并断言 console 无 error。详细环境变量见 `tools/e2e/README.md`。

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

## 发布（Netlify，本地部署）

**线上地址：<https://survivalgame-demo.netlify.app>**

采用「本地构建 + 本地部署」：构建产物**不进仓库**，每次构建后用 Netlify CLI 上传 `web/`。

站点信息：项目名 `survivalgame-demo`，Site ID `4d183772-2a92-4ff5-8cbc-b77450df46dd`，
团队 `moonsmile233`。

> 新站点创建后默认开了「需要登录访问」，必须到
> **Project configuration → General → Visitor access → Visibility** 改成 **Public** 才能被公开访问，
> 否则访客会看到 Netlify 登录页。该开关只能从网页后台改（公开 API 不提供写入）。

一次性准备：

```bash
npx netlify-cli login   # 浏览器授权，需要本人操作
npx netlify-cli link    # 选择或新建站点
```

之后每次发布：

```bash
# 1) 构建 WebGL（编辑器开着点菜单 Survival/Build WebGL；关着用 batchmode）
# 2) 部署
bash scripts/deploy-netlify.sh
```

`netlify.toml` 已固定 `publish = "web"`；`/unity/Build/*` 与 `build-info.json` 设为不缓存，
因为 Unity 产物文件名跨构建不变，长缓存会导致更新后浏览器仍加载旧包。

### 想改成「推 GitHub 就自动上线」

把 `.gitignore` 里的 `/web/unity/` 删掉、把构建产物提交，然后在 Netlify 里连接这个仓库即可。
代价是每次构建都会往历史里塞约 8 MB 二进制，等游戏定型再切也不迟。

### GitHub Pages（备选，未采用）

Pages 同理需要产物在仓库里；`web/` 作为站点根即可。当前方案不依赖它。

## 游戏内 UI 与 H5 外壳（M4）

```text
Unity HUD       生命 100/100（血条 + 数字，左上）｜时间 00:60（上中）｜暂停（右上）｜分数 0（暂停下方）
Unity 已暂停     遮罩 + 标题 + 继续；失焦切回时显示大号 3-2-1，倒计时结束自动继续
Unity 结算       遮罩 + 标题 + 得分 + 明细（存活分 / 击杀 / 胜利奖励）+ 重新开始（仅 Editor 显示；
                  WebGL 嵌入模式隐藏，由 H5 卡片统一展示）
H5 顶部         标题 + 昵称输入（最多 16 字）
H5 中部         16:9 Unity 画布 + 加载遮罩 / 进度 + 错误卡片（重试）
H5 底部         开始游戏 / 重新开始 + 状态提示（游戏中 / 已暂停）+ 操作提示
H5 结果卡片     本次得分 / 历史最高分 / 昵称 / 存活秒数 / 新纪录 + 当前设备操作提示
H5 页脚         版本号 · 构建时间（来自 web/unity/build-info.json）
```

- 手机窄屏（画布 CSS 宽度 < 640）时 HUD 自动切 640×360 参考分辨率，字号等效放大；暂停按钮放右下便于拇指。
- 操作提示是 H5 DOM 文本（`.op-hint`），用 `pointer: coarse` 在「WASD」和「拖动屏幕」之间切换，不占 Unity 字体图集。

- 文案全部在 `Assets/Scripts/UI/UiStrings.cs`，字体是 TMP + `Assets/Fonts/NotoSansSC-UI-SDF.asset`
  （从 `NotoSansSC-UI-Subset.otf` 按 `ui-chars.txt` 裁的 121 字符，1024×1024 Static SDF）。
- **改文案不用手动重烤字体**：`Survival/Build WebGL` 构建前会自动重烤图集；
  若新字不在子集里，构建会报错并列出缺字，需要换完整字体重新裁子集。
- H5 昵称仍只在网页侧渲染，Unity 不显示昵称。

## 约定

- 游戏内 UI 用中文 + 数字，必须走 TMP + 项目内置 CJK 子集字体（禁止 legacy `Text` / `LegacyRuntime.ttf`）。
- 跨边界只传 JSON 字符串；`SendMessage` 每次只能传一个参数。
- 场景静态结构（相机、Canvas、玩家、生成器、整套 UI）放场景里，由 `SceneSetup` 生成；
  动态物体运行时生成。
- 通信协议见 `docs/bridge-protocol.md`（设计依据为 `docs/项目规范.md` 第 7 节）。
- H5 存储键：`survival.bestScore`、`survival.nickname`；业务代码只能通过 `web/js/storage.js` 读写。
