# 重构 v1.2 第二阶段实施计划：H5 薄壳（昵称 / 进入 / 数据面板 / 全屏）

> 文档版本：2026-11-02T14:10+08 · 作者：程序 · 状态：**待策划放行；落盘时未写实现代码**
> 依据：`proposal-M6-重构-游戏优先-v1.2.md` §4–§7、§11；`proposal-M6-重构-第一阶段-实施计划.md` §15
> （难度偏好补充）；策划 2026-11-02 第二阶段口径。
> 基线：`main = wip = b788de03`（v1.2 第一阶段已验收）；Unity 构建 `1.2.0`（`2026-11-02T05:12:58Z`，
> 仓库外 `D:/unityFile/_builds/SurvivalGame-1.2.0-wip-2026-11-02/`，`web/unity` 仍是 1.0.0）。

## 0. 摘要

1. **只改 H5 静态文件，不重建 Unity**：协议 v3 与菜单/状态机已在第一阶段完成；这一阶段把 H5 变成
   “身份 + 入口 + 数据展示”的薄壳，使用现有 1.2.0 构建。
2. **删除 H5 的局内职责**：难度卡、H5 自己的 3-2-1、暂停/继续/重开/返回、结算卡、分档最高分、
   Playing/Paused 全屏零控件的旧分支。
3. **保留作业七条**：昵称、开始按钮、嵌入、Unity→H5 传分、本次/最高分展示、localStorage、响应式。
4. **壳状态**：`Loading → Shell → Entered → GameOver`；全屏入口只在 `Entered` 之后出现，
   全屏内 0 网页按钮，退出靠 Esc/系统手势。
5. **存储**：单一 `survival.bestScore`（旧三档只读一次取 max）；`survival.nickname`；`survival.difficulty`
   由 H5 代存 Unity 难度偏好；最近一局只在内存，不持久化。
6. 版本串保持 **`?v=1.2.0`**（H5 文件本身首次以 1.2.0 发布；若评审期间再改，按协作协议 bump 到 1.2.1）。

## 1. 为什么不需要重建 Unity

- Unity 侧已实现：`EnterGame{nickname,lastDifficulty}`、`onReady(protocol=3)`、
  `onStateChange(menu/playing/paused/result)`、`onDifficultyChanged`、`onGameOver`、
  `SetPausedByVisibility` 兜底；菜单/难度/3-2-1/暂停/重开/结算全在画布内。
- 本阶段只改 `web/**` 的 HTML/CSS/JS；Unity 产物、`Assets/**`、`ProjectSettings/**` 不动。
- 本地验收：把仓库外 1.2.0 构建临时复制到 `web/unity/` 跑 E2E，结束后 `jj restore web/unity`
  （仓库里仍是 1.0.0 产物）。**不构建、不部署**。
- 若评审要求“Unity 也要为薄壳改动”（例如新增协议字段），先停报，不在本阶段扩协议。

## 2. 壳状态机

| 状态 | 触发 | 页面表现 | 画布 |
|---|---|---|---|
| `Loading` | 页面启动 | 加载进度；开始按钮 disabled | 加载遮罩 |
| `Shell` | `onReady` 且 `protocol===3` | 昵称可编辑、开始可用、「游戏已就绪」遮罩；全屏按钮**隐藏**；数据面板显示历史最高分/最近一局 | Unity Menu（不可见遮罩下） |
| `Error`（版本不匹配） | `onReady` 的 `protocol !== 3` | **禁用开始**、显示错误卡「版本不匹配，请刷新重试」，不进入 Entered | 不进入 |
| `Entered` | 点「开始游戏」→ 发 `EnterGame` | 遮罩消失、开始隐藏、昵称只读、**全屏按钮出现**；数据面板常驻 | Unity Menu → 玩家在画布内选难度/开始 |
| `GameOver` | `onGameOver` | 同在 `Entered` 布局；数据面板更新本次/最高分/最近一局；不弹 H5 结算卡 | Unity Result 面板 |
| `Error` | 加载/启动失败 | 错误卡 + 重试 | — |

- 没有“退出到外壳/返回 Shell”：用户已定，刷新页面即回 Shell（并保留昵称/最高分/难度偏好）。
- `GameOver` 不是独立布局态：仍属 Entered，只是数据面板内容更新；状态文案可显示“本局结束”。
- 全屏入口时机（策划补充）：**只在 `Entered` 之后显示**。Shell 阶段给全屏会陷入“全屏内 0 网页按钮
  就没有开始可点”的矛盾；进入后画布是 Unity 菜单，全屏内菜单可点、0 网页按钮。

## 3. 页面结构与布局

### 3.1 宽屏（≥900px）

```text
┌──────────────┬──────────────────────────────┐
│ 左侧数据栏     │  16:9 画布（Unity）           │
│ · 标题/副标题  │  · 加载/已就绪遮罩             │
│ · 昵称输入     │  · Unity 菜单/游戏/暂停/结算   │
│ · 开始 / 全屏  │                              │
│ · 最高分       │                              │
│ · 最近一局     │                              │
│ · 状态文案     │                              │
└──────────────┴──────────────────────────────┘
```

### 3.2 手机（<900px）

```text
标题
昵称 + 开始 / 全屏
数据面板（最高分 / 最近一局）
16:9 画布
页脚版本
```

- 页面允许纵向滚动，**无横向滚动**；`body { overflow-x: hidden }` 只兜底，根因用 `min-width:0` / `max-width:100%`。
- 画布 `aspect-ratio: 16/9`、`width:100%`、`touch-action:none`；画布上的滚轮继续由 H5 捕获后 `scrollBy`
  （保留现有 `onCanvasWheel` 逻辑，全屏与 Ctrl+滚轮放行）。
- 数据面板内容：`最高分 N`（单一，不区分难度）、`最近一局：普通 980`（难度 + 分数；无则「—」；
  胜负/存活放 `title`/aria）、`昵称：玩家`、状态文案（`游戏已就绪` / `游戏菜单` / `游戏中` / `已暂停` / `本局结束`）。
- **Shell 遮罩**：加载层/「游戏已就绪」层铺满画布并 `pointer-events:auto`，在 `Entered` 前挡住 Unity 菜单，
  避免玩家在 H5 还没进入时直接操作画布；`Entered` 后隐藏。加载失败仍复用同一层的错误卡。
- 昵称在 Shell 可编辑；Entered 后只读。昵称只在 H5 渲染（数据面板/页面标题），不进 Unity。
- 不显示难度选择 UI；可有一句提示「难度在游戏内选择」。
- 页脚：版本 + 构建时间（来自 `build-info.json`）。

### 3.3 全屏

- 入口：`Entered` 后显示「全屏」按钮（与开始分开，开始此时已隐藏）。
- 目标元素：`.game-frame`（只含画布与遮罩）；`:fullscreen` 时隐藏所有网页 UI（数据栏、按钮、页脚、提示），
  画布 letterbox 居中、背景纯黑。
- 全屏内 **0 网页按钮**；退出靠 Esc（桌面）/系统手势或返回（手机），`fullscreenchange` 恢复页面。
- `requestFullscreen` 被拒（iPhone Safari）→ 提示「当前浏览器不支持网页全屏，请横屏或添加到主屏幕」，
  保持普通布局；`screen.orientation.lock('landscape')` 为 best-effort。
- 全屏中失焦：Unity 自己暂停；H5 继续发 `SetPausedByVisibility` 兜底（见 §5）。
- 全屏中结算：Unity 画布内显示结算；退出全屏后数据面板已是最新。

## 4. 文件改动

| 文件 | 动作 | 要点 |
|---|---|---|
| `web/index.html` | **重写** | 左侧栏/顶栏 + 16:9 画布；删难度区、H5 倒计时、结算卡、重新开始；保留昵称/开始/全屏/数据面板/遮罩/错误卡/页脚；`?v=1.2.0` |
| `web/css/style.css` | **重写布局段** | 桌面双栏 / 手机单列、16:9、数据面板、全屏规则；删难度/结果/倒计时样式；保留按钮/输入/遮罩/错误/页脚/旋转提示 |
| `web/js/app.js` | **重写** | 壳状态机、EnterGame、事件处理、数据面板、迁移、全屏、可见性；删局内指令与 H5 3-2-1 |
| `web/js/bridge.js` | 小改 | 新增 `onDifficultyChanged` 转发；加载逻辑不变 |
| `web/js/storage.js` | 保持 | 通用 `survival.*` 封装；迁移逻辑放 app |
| `web/js/perf-overlay.js` | 保持 | `?perf=1&overlay=1` 调试叠层；若引用旧页面状态则改为只读 console |
| `tools/e2e/h5-shell.mjs`（新） | 新增 | 见 §8 |
| 旧 E2E | 重写/废弃 | 见 §8.3 |

**删除项（明确）**

- 难度卡与 `survival.bestScore.{easy,normal,hard}` 的日常读写（仅迁移时读一次）。
- H5 自己的开始/重开 3-2-1（`countdown`、`Starting` 页面态、`START_RESPONSE_TIMEOUT_MS` 兜底提示）。
- H5 暂停/继续/重开/返回按钮与结算卡（`#result`、`#btn-restart`）。
- Playing/Paused 全屏“保留/隐藏菜单”的旧分支（L1.1 口径作废；全屏内始终 0 网页控件）。
- 分档最高分展示、难度卡键盘交互、点卡中止本局等逻辑。

## 5. 协议调用

| 时机 | H5 动作 |
|---|---|
| `onReady` | **必须 `protocol===3`**：相等才启用开始并进 Shell；不相等 → 禁用开始 + 错误卡「版本不匹配，请刷新重试」，**不进 Entered**（新 H5 配旧 Unity 会没有 Unity 菜单，不能放行） |
| 点开始 | 先设 `Entered`（遮罩隐藏/昵称只读/开始隐藏/全屏出现），再 `SendMessage('WebGLBridge','EnterGame', JSON)`；`SendMessage` 同步，顺序不能反 |
| `EnterGame` 载荷 | `{"nickname":"玩家","lastDifficulty":"easy\|normal\|hard"}`；难度取 `survival.difficulty`，非法/缺失 → `normal` |
| `onStateChange` | 只更新状态文案（menu/playing/paused/result）；**不改变壳布局**、不显示/隐藏按钮 |
| `onDifficultyChanged` | 校验后写 `survival.difficulty`；菜单选择变化才发，EnterGame 回填不回发（Unity 已保证） |
| `onGameOver` | 更新 `survival.bestScore`（仅 `score > best`）、内存最近一局、数据面板；不显示 H5 结果卡 |
| 失焦/隐藏 | 发 `SetPausedByVisibility("true")`；切回发 `"false"`（环境信号，保留；Unity 原生焦点为主，幂等） |
| 局内指令 | **不再发** `StartGame/PauseGame/ResumeGame/RestartGame/ReturnToMenu` |

- `onGameOver` 字段沿用 v3：`result/score/survivedSeconds/kills/nickname/difficulty/breakdown`；
  H5 只用 `result/score/survivedSeconds/difficulty`，不展示明细。
- `onReady` 之前开始按钮 disabled；点击后立即禁用，防止连点。
- 旧 H5 的 `StartGame` 兼容路径不再使用（本阶段页面整体替换）。

## 6. 存储与迁移

| 键 | 读写 | 说明 |
|---|---|---|
| `survival.bestScore` | 读/写 | 单一最高分（跨难度共享）；只在 `score > best` 时写。**与 v1.0 旧单键同名**，迁移前先读旧值 |
| `survival.bestScoreMigrated` | 读/写 | **显式迁移标记**（`"1"`）；没有它才读旧键，有它之后永不回读 |
| `survival.nickname` | 读/写 | Shell 输入；开始进入时规范化后保存 |
| `survival.difficulty` | 读/写 | 只由 `onDifficultyChanged` 写、EnterGame 时读；H5 无选择 UI |
| `survival.bestScore.easy/normal/hard` | **只读一次** | 仅迁移时读；旧键**不删**、之后不再读 |
| 最近一局 | 内存 | 只从 `onGameOver` 更新；刷新清空，不持久化 |

**迁移算法（防止 v1.0 单键 + v1.1 三键并存时丢分）**

```text
best = storage.getInt('bestScore', 0)          // 注意：这可能已经是 v1.0 的旧值，也可能是新值
if (!storage.get('bestScoreMigrated')):
    maxOld = max(best, easy, normal, hard)      // 读旧三档；同名单键的当前值也参与
    storage.set('bestScore', maxOld)            // 覆盖为新单键
    storage.set('bestScoreMigrated', '1')       // 标记后不再回读旧键
else:
    // 标记存在：只认新键；新键缺失按 0，绝不回读旧键
    best = storage.getInt('bestScore', 0)
```

- 迁移时点：页面启动（`storage` 可用）；写入新键（即使 0）+ 标记，之后不再看旧键。
- 旧键只读不删；标记存在但新键缺失 → 0。
- `localStorage` 不可用：回退内存 + 页面提示；最高分/昵称在本会话内仍工作。
- 昵称：空 → 「玩家」；去首尾空白、按码点截 16；渲染一律 `textContent`。

## 7. 视觉与文案

- 沿用现有深色主题（`#0b0b12` / 面板 `#171724` / 紫色主按钮）。
- 新增文案（H5，不受 Unity 字体限制）：
  `数据`、`最高分`、`最近一局`、`游戏已就绪`、`已进入`、`游戏中`、`已暂停`、`本局结束`、
  `难度在游戏内选择`、`全屏`、`退出全屏`（不提供按钮，仅提示）、`暂无`、`新纪录`。
- 状态文案只做提示，不控制布局；难度中文名由 H5 映射（easy/normal/hard）。
- 全屏时隐藏全部网页 UI（包括状态文案、页脚、数据栏），画布独占。

## 8. 测试

### 8.1 新增 `tools/e2e/h5-shell.mjs`（无头 Chrome）

1. **加载/就绪**：开始按钮在 `onReady` 前 disabled；`protocol===3` 后启用、遮罩文案「游戏已就绪」。
2. **协议不匹配**：注入 `protocol:2` 的 onReady → 开始保持禁用、显示错误卡「版本不匹配，请刷新重试」，
   不出现 Entered 布局。
3. **昵称**：输入 → 开始 → localStorage `survival.nickname`；空/超长/特殊字符截断；刷新回填；
   数据栏显示昵称，Entered 后只读。
4. **进入**：点开始 → 遮罩消失、开始隐藏、昵称只读、全屏按钮出现；Unity console 出现
   `EnterGame nickname=... lastDifficulty=...`；**连续点击开始只发一次 EnterGame**（按钮禁用/防护）。
5. **无旧局内指令**：整个流程中 Unity console 不出现 `PauseGame/ResumeGame/RestartGame/ReturnToMenu`
   的旧指令 warning；H5 源码无这些 `SendMessage`。
6. **菜单/状态文案**：用画布坐标点 Unity 菜单开始（复用 `ui-layout.mjs`）→ 状态文案「游戏中」；
   Esc → 「已暂停」；继续 → 「游戏中」；`menu` 进 Entered 后显示「游戏菜单/已进入」，不误导成未进入。
7. **难度偏好**：画布点困难 → `survival.difficulty=hard`；刷新 → EnterGame 载荷含 `lastDifficulty:hard`
   （Unity console 校验）。
8. **结算/最高分**：`?perf=1&roundDuration=2` 短局 → `onGameOver`；数据面板最高分/最近一局更新；
   低分**不覆盖**最高分；刷新后最高分仍在、最近一局清空。
9. **迁移（v1.0 单键 + v1.1 三键并存）**：预置 `bestScore=1000`、`bestScore.hard=1200`（及 easy/normal）→
   首次加载新 `bestScore=1200`、`bestScoreMigrated=1`；之后修改旧键再刷新，新键不变；旧键仍存在（不删）；
   标记存在但删掉新键 → 读 0，不回读旧键。
10. **全屏**：Shell 无全屏按钮；Entered 后出现；点全屏 → `document.fullscreenElement` 存在；
    全屏内可见网页按钮数 = 0、所有网页 UI 不可见；toast「按 Esc / 系统手势退出全屏」2~3s 消失（不是按钮）；
    `exitFullscreen()` 后数据栏/全屏按钮恢复；模拟 `requestFullscreen` 拒绝 → 提示降级。
11. **响应式**：320×568 / 390×844 / 844×390 / 768×1024 / 1280×720 / 1920×1080：
    Shell、Entered、GameOver 截图；`document.documentElement.scrollWidth <= clientWidth`；无横向滚动。
12. **存储不可用**：override `localStorage` 抛错 → 内存回退 + 提示，页面仍可进入/结算。

### 8.2 复用 / 保留

- `tools/e2e/standalone-*.mjs`（Unity 侧）保留，不受 H5 改版影响。
- `tools/e2e/perf.mjs`：若仍依赖 H5 开始按钮/难度卡，改为 `?perf=1&autostart=1` 或指向 standalone；
  性能口径不变（40 只 165FPS/p95 7ms）。
- `launch.mjs` / `ui-layout.mjs` 复用。

### 8.3 旧脚本影响

| 脚本 | 处理 |
|---|---|
| `smoke.mjs` | **重写**：加载→昵称→进入→Unity 菜单开始→暂停→继续→结算→刷新最高分 |
| `difficulty-e2e.mjs` | **废弃**：难度 UI 已移到 Unity；难度偏好改由 `h5-shell` 覆盖 |
| `menu-check.mjs` | **重写/拆分**：6 视口 + 数据面板 + 迁移 + 全屏 |
| `fullscreen-states.mjs` | **重写**：入口只在 Entered 后、全屏 0 网页控件、退出恢复 |
| `responsive-shot.mjs` | **重写**：新布局 6 视口截图 |
| `joystick-shot.mjs` | 保留（Unity 画布内摇杆，与 H5 布局无关，确认视口后再跑） |
| `crowd-clip.mjs` / `perf.mjs` | 视依赖调整；优先 standalone |

## 9. 验收标准（作业七条）

| # | 原文 | 本阶段证据 |
|---|---|---|
| 1 | 输入玩家昵称 | Shell 输入、保存、刷新回填；H5 显示昵称；不进 Unity |
| 2 | 开始游戏按钮 | `onReady` 前禁用；点击进入（EnterGame）；重复点击防护 |
| 3 | 嵌入 Unity WebGL 游戏 | 画布嵌入 + 16:9 + 响应式 |
| 4 | Unity 把分数传给 H5 | `onGameOver` 更新数据面板；console/事件证据 |
| 5 | H5 显示本次成绩和历史最高分 | 数据面板「最高分 / 最近一局」；不弹 H5 结算卡 |
| 6 | 最高分 localStorage | 单键 `survival.bestScore`；刷新仍在；旧三档迁移一次 |
| 7 | 页面适配电脑和手机宽度 | 6 视口；无横向滚动；手机顶部数据栏 |

- **不部署**：本阶段只在本地/局域网验收，分享链接和线上 v1.0.0 不动；部署与 README 版本收口留到发布步。
- 回归：Unity 侧 `standalone-*` 证据仍有效（本阶段不改 Unity）；旧 H5 过渡路径不再需要。
- 全屏：入口时机、全屏 0 网页控件、Esc/手势退出、iPhone 降级提示。

## 10. 风险与停点

| 风险 | 处理 |
|---|---|
| 全屏内 canvas 尺寸变化导致 Unity 画面错位 | `fullscreenchange` 后等一帧再读尺寸；截图验证；必要时触发 window resize |
| iPhone Safari 不支持全屏 | 提示降级，普通布局可玩；不是失败 |
| 旧键迁移误删/重复 | 新键存在即不再读旧键；旧键只读不写不删；E2E 覆盖 |
| 昵称/分数 XSS | 全部 `textContent`，不拼 HTML |
| 缓存旧 JS | 保持 `?v=1.2.0`；本地无痕/Disable cache；改动后再 bump |
| 数据面板与 Unity 结算重复 | H5 只显示数据，不弹结算卡 |
| 协议/状态需要 Unity 改动 | 停报，不在本阶段扩协议/重建 |
| 全屏手势/真机差异 | 记录，停点报告；不静默改口径 |
| 6 视口与全屏自动化 | 无头 Chrome 全屏 API 可用性先探测；不可用就记录并转为 DOM/截图证据 |

**必须停下**：需要 Unity 重建或协议变更；需要用户 Unity/GUI 操作；全屏 API 行为不符预期；
字体/素材需求；部署或线上变更。

## 11. 实施顺序（预计 0.5~1 天，0 次 Unity 构建）

1. `web/index.html` + `style.css` 新布局（Shell/Entered 两态静态骨架）+ 数据面板。
2. `web/js/app.js` 壳状态机 + EnterGame/事件/存储/迁移 + `bridge.js` 加 `onDifficultyChanged`。
3. `tools/e2e/h5-shell.mjs`（加载/进入/数据/迁移/全屏/6 视口）。
4. 本地 1.2.0 构建联调；截图 + 事件证据；旧脚本按 §8.3 处理/标注。
5. 文档同步（实现总览 §6.2、bridge-protocol 的 H5 侧、acceptance、README 说明“开始=进入游戏”）。
6. 汇报 + 等验收；**不部署、main 不自动前移**（按策划决定）。

## 12. 策划裁决（2026-11-02，放行）

1. **版本串保持 `?v=1.2.0`**（与 Unity 构建一致）；验收后若只改 H5 再发，再 bump 1.2.1 并注明 H5-only，不混用。
2. **数据栏显示昵称**（Shell 可编辑，Entered 后只读）；昵称不进 Unity。
3. **最近一局格式按 mock：`普通 980`**（难度 + 分数）；胜负/存活在 Unity 结算卡里已有，不重复上屏；
   完整信息放 `title` / aria。
4. **状态文案保留**，只读提示、不控制布局；`menu` 进 Entered 后显示「游戏菜单 / 已进入」，不要误导成未进入。
5. **全屏退出提示**可做：进入全屏后显示 2~3 秒自动消失的 toast「按 Esc / 系统手势退出全屏」，**不是按钮**；
   手机文案用「手势返回」。
6. **旧键不删、只读一次**（按 §6 的显式迁移标记）。
7. **`perf.mjs` 保留**：改成新 H5 + `?perf=1&autostart=1` 跑一次 40 只作为“交付页”记录（不是新门禁）；
   跑不了就沿用 standalone 证据并在执行记录写清。
