# M6 第 2 步交互最终版（交接文档）

> 文档版本：2026-11-01T21:35+08 · 作者：程序 · 状态：**自动化验收全绿（真机/人工待测，未部署、main 未动）**
> 依据：用户 2026-11-01 三次明确的交互口径；以**磁盘上的实际代码**为准核对过，与规格有出入的地方写在 §4「未完成」。
> 前序：`docs/proposals/proposal-M6-步骤2-难度与协议v2.md`（协议/数值/迁移方案，仍有效）。

## 0. 一句话

难度卡常驻可点；点卡 = 换档/中止本局回主菜单；开始与重新开始都有 3 秒倒计时；画布全状态保持完整尺寸，页面允许纵向滚动。

## 1. 交互规格（最终口径）

### 1.1 难度卡

- 在 **Ready / Starting / Playing / Paused / Result** 全部**常驻可见、可点击**；全屏时由 CSS 隐藏（全屏例外）。
- 点击行为：
  - **Ready**：只选择（写 `localStorage.survival.difficulty`），不发消息；
  - **Starting**（H5 3-2-1 倒计时中）：取消倒计时、回 Ready、选中该档（Unity 仍在 Ready，不发 ReturnToMenu）；
  - **Playing / Paused / Result**：**中止本局**（清场、不计分、不写最高分、不发 `onGameOver`）→ `ReturnToMenu` → 回 Ready → 选中该档；结算卡隐藏；
  - **点已选中的那张也执行**（等同「回到该档开始界面」）。
- 键盘方向键只改 radio、不发 click：非 Ready 状态会回弹，不会误触发中止（中止只由鼠标/触摸点击卡片触发）。

### 1.2 3-2-1 倒计时（H5）

- **开始游戏**与**重新开始**都要：3→2→1，每秒一跳，覆盖在画布上；3 秒后才发 `StartGame` / `RestartGame`。
- 倒计时中点击难度卡 → 取消（Starting 回 Ready；Result 走中止流程），**不发开始指令**。
- 倒计时中页面隐藏/失焦 → 取消，**不在后台开局**。
- Unity 暂停面板自己的 3-2-1（resume）**不动**；两处视觉尽量一致（深色底 + 大号白字）。

### 1.3 布局

- **画布在所有状态保持完整尺寸**（原来的 Playing 尺寸），不再按菜单缩小。
- **菜单（难度卡 + 最高分行）常驻**。
- DOM 顺序：标题 → 一句话规则 → 昵称 → 难度卡 → 最高分行 → **开始/全屏/状态行** → 画布 → 操作提示 → 结算卡 → 页脚。
- 页面**允许纵向滚动**（1280×720 约 774–870px、844×390 约 468–500px；320×568 / 390×844 基本不滚）；**无横向滚动**。
- `touch-action: none` 只作用于画布（拖摇杆不滚页）；滚轮/页面的其它区域照常滚动。
- 结算卡在画布下方，出现时滚动到可见（F2 不回退）。

### 1.4 Unity `ReturnToMenu`

- 允许 **Playing / Paused / Won / Lost → Ready**（含 resume 3-2-1 倒计时中）；Ready 下幂等。
- 动作：停止生成、`ResetRound()`（清场/血量/计时/分数/玩家位置）、`Time.timeScale = 1`、暂停面板收起、倒计时取消。
- **不结算、不发 `onGameOver`**（中止局不写最高分）。

### 1.5 协议

- protocol 仍为 **2**（不升 v3）。`docs/bridge-protocol.md` 已补 `ReturnToMenu`（H5→Unity，无参数）与状态转换、时序、版本历史。

## 2. 实现落点（已核对代码）

| 文件 | 内容 |
|---|---|
| `web/index.html` | 难度区在 header 下；controls（开始/全屏/状态）**在画布上方**；`#countdown` 覆盖层在 `.game-frame` 内；结算卡在画布下方 |
| `web/js/app.js` | `onDifficultyPicked()`（四态规则）、`startCountdown()`/`cancelCountdown()`、`beginStartCountdown()`/`sendStartCommand()`、`beginRestartCountdown()`/`sendRestartCommand()`、`cancelCountdownBecauseHidden()`、`onCanvasWheel()`（画布滚轮穿透）；`COUNTDOWN_SECONDS = 3`；难度卡用 `click`（不是 `change`）触发；`body[data-page]` 驱动全屏 CSS；分档最高分与旧键迁移沿用前一版 |
| `web/css/style.css` | `.countdown` 覆盖层；`.difficulty` 常驻；画布恢复完整尺寸（基础规则 `min(100%, (100dvh - 170px) × 16/9)`，短横屏 `height: 100dvh - 132px`）；全屏时 Playing/Paused/Result 隐藏难度区与操作区；页面不设 `overflow: hidden`（仅全屏 `html` 有） |
| `Assets/Scripts/Core/GameManager.cs` | `ReturnToMenu()`：StopSpawning → ResetRound → 再 StopSpawning → SetState(Ready) |
| `Assets/Scripts/Bridge/WebGLBridge.cs` | `ReturnToMenu(string)`；protocol 2；StartGame 解析 difficulty；gameOver 回传 difficulty |
| `docs/bridge-protocol.md` | `ReturnToMenu` 行、状态守卫、时序、v2 历史 |
| `tools/e2e/menu-check.mjs` | 6 视口 Ready/Playing/Result、滚动、绘制缓冲、迁移/跨档/全屏 |
| `tools/e2e/difficulty-e2e.mjs` | A–D（三档生效）+ E–G（倒计时取消/点卡中止/重开倒计时/失焦取消） |
| `tools/e2e/fullscreen-states.mjs` | Ready 允许全屏 |

测试钩子：`?testhook=1` 时 `window.SurvivalApp.__sendMessage` 暴露原始 SendMessage（验证 Unity 对非法/旧协议载荷的回落）；正常页面不挂。

## 3. 已完成

- 协议 v2 文档与实现（`StartGame.difficulty`、`onGameOver.difficulty`、`ReturnToMenu`）。
- Unity 三档难度：`Difficulty` 枚举、`DifficultyProfile` SO ×3、运行时活动难度（不写回资产）、速度/间隔/伤害/上限/压力/计分倍率、`StartGame` 快照、`RestartGame` 沿用。
- Unity `ReturnToMenu`（含从 resume 倒计时中返回：`PausePanel.HandleStateChanged` 收到非 Paused 会停倒计时）。
- H5：一屏主界面、难度卡常驻可点、四态点击规则、3-2-1 倒计时、分档最高分 + 旧键迁移、Ready 全屏修复、`body[data-page]` 布局规则、画布完整尺寸 + 允许滚动。
- E2E 脚本：`menu-check.mjs`（6 视口/滚动/迁移/跨档/全屏/绘制缓冲）、`difficulty-e2e.mjs`（A–G）。
- 构建：`web/unity` 为 `2026-11-01T12:44:35Z` 构建（含 `ReturnToMenu`）；H5 之后的改动都是静态文件，不需要重出构建。

## 4. 未完成 / 待验收

1. ~~E/F/G 新交互 E2E 还没跑过~~ → **已跑**：2026-11-01 `2026-11-01T12:44:35Z` 构建上 A–G 全 PASS
   （47 条 PASS / 0 FAIL，`docs/evidence/m6-difficulty-e2e-stdout.log`）。
2. ~~17 条回归 + smoke 3 局 + fullscreen-states 还没重跑~~ → **已跑**：smoke 3 局 25 PASS、
   移动视口 smoke 19 PASS、fullscreen-states 11 PASS（含全屏内滚轮不滚）、menu-check 137 PASS
   （含画布上/画布外滚轮、不双重）；wheel 修复后 smoke 3 局 25 PASS + 1 局 21 PASS 复跑。证据见
   `docs/evidence/m6-step2-h5-smoke.log`、`m6-step2-h5-smoke-mobile.log`、`m6-step2-h5-smoke-wheel.log`、
   `m6-fullscreen-states.log`、`m6-h5-menu-check.log`。
3. **真机 Honor 90 Pro 未测**（需要人操作）。
4. ~~文档同步~~ → **已做**：`实现总览 §2/§6/§7/§9`、`acceptance.md §3.3/§4.8`、`README` 难度一节。
5. `main` 未动；当前只有本地 WIP checkpoint，未推。

## 5. 已知问题

1. ~~320×568 Playing 的「画布上滚轮滚动」断言失败~~ → **根因已确认并修复**。
   Unity WebGL 的 Emscripten wheel 回调返回 true 时调用 `e.preventDefault()`；实测画布上的 wheel
   `defaultPrevented=true`，Ready/Playing、320/1280 都一样。其它视口的「PASS」是假通过：点击开始按钮后
   页面已滚了 60–65px，断言在未实际滚动的情况下也成立。
   **修复（2026-11-01，策划确认）**：`app.js` 的 `onCanvasWheel` 在 document 捕获阶段接管画布 wheel：
   `preventDefault()` 后 `scrollBy(0, deltaY)`（deltaMode 行/页换算），全屏与 Ctrl+滚轮放行；只改 H5、不重建 Unity。
   `menu-check` 在 320/844/1280 断言「画布上 40px → 恰好 40px（不双重）+ 画布外可滚」，
   `fullscreen-states` 断言全屏内不滚，均 PASS。
2. **全屏 Paused 下难度卡被 CSS 隐藏**（全屏例外），且旧的两个「返回主菜单」按钮已删除；换档需先退出全屏。属规格内（全屏例外），但要在验收说明里写清。
   **L1.1 修订（2026-11-01 用户口径）**：Ready/Starting/Playing/Paused 都能进全屏（Result 不提供）；
   Ready/Starting 全屏保留难度区 + 最高分行 + 开始；游戏中（Playing/Paused）全屏隐藏所有网页外壳与按钮
   （含删除「退出全屏」），退出只靠 Esc/系统手势。见 `acceptance.md §3.4`。
3. **Result 下难度卡在画布上方**：结算卡出现时可能把菜单滚出视口，换档需要向上滚动。属预期，但体验上要确认。
4. `docs/evidence/` 里 `m6-h5-*`、`m6-difficulty-*` 部分截图来自旧交互（按钮版/缩画布版），**最终状态以重跑为准**。
5. 预编译警告 `_pausedByVisibility` assigned but never used（M4.6 遗留，未处理）。

## 6. 测试清单（2026-11-01 已全部重跑，除真机）

| # | 用例 | 结果 |
|---|---|---|
| 1 | 6 视口 Ready：无横向滚动、难度卡 + 开始按钮首屏可见、卡高 ≥44（短横屏 ≥36） | **PASS**（menu-check，`m6-h5-ready-*.png`） |
| 2 | 6 视口 Playing：画布与 Ready 同尺寸、菜单常驻可点、开始隐藏、无横向滚动、滚轮可滚 | **PASS**（画布上由 H5 接管、画布外原生，均不双重；见 §5.1） |
| 3 | 390×844 Playing 点「简单」→ Ready + 简单选中 + 本局不计分 | **PASS**（difficulty-e2e E） |
| 4 | 点开始 → 3-2-1 → 简单局；`onGameOver.difficulty=easy`、5 秒胜利 250 | **PASS**（difficulty-e2e E） |
| 5 | 倒计时中点别的档 → 取消、不后台开局 | **PASS**（difficulty-e2e E） |
| 6 | 重新开始也有 3-2-1；倒计时中点卡 → Ready | **PASS**（difficulty-e2e F） |
| 7 | 暂停/结算中点难度 → Ready | **PASS**（difficulty-e2e E/F） |
| 8 | 倒计时中失焦 → 取消 | **PASS**（difficulty-e2e G） |
| 9 | hard 60 秒 = 1160、damage=14、`onGameOver.difficulty`、非法→normal、v1 载荷→normal | **PASS**（difficulty-e2e A–D） |
| 10 | smoke 3 局 + 暂停/失焦/3-2-1/重开/刷新 | **PASS**（25 条，`m6-step2-h5-smoke.log`） |
| 11 | fullscreen-states（Ready 可全屏、Playing/Paused 可进、Result 不可） | **PASS**（8 条，`m6-fullscreen-states.log`） |
| 12 | 17 条回归、真机 40 只性能抽查 | 回归 **PASS**（自动化覆盖）；**真机未测** |

证据目录：`docs/evidence/m6-h5-*`、`m6-difficulty-*`、`m6-return-menu-*`、`m6-step2-h5-smoke.log`、
`m6-fullscreen-states.log`、`m6-difficulty-e2e-stdout.log`。

## 7. 下一步建议

1. ~~`menu-check` 320 滚动断言~~ → 已确认为 Unity 引擎拦截 wheel，测试改为画布外验证（见 §5.1）。
2. ~~difficulty-e2e 全量~~ → 47 PASS。
3. ~~smoke 3 局 + fullscreen-states~~ → 25 PASS / 11 PASS。
4. ~~文档同步~~ → 已做；**`main` 是否前移由策划决定**（自动化全绿，真机/人工未测）。
5. 真机 Honor 90 Pro：三档各 1 局 + 40 只 `?perf=1` 抽查 + 换档/倒计时/滚动。
6. ~~待决策：H5 wheel 穿透~~ → 已按策划口径实现并验证（见 §5.1），只改 H5。

## 8. 仓库状态

- 当前 WIP checkpoint 由 `jj describe` 写在 `@` 上（含 Unity 源码、H5、E2E、构建产物 `web/unity`、证据、
  本次验收的 E2E 修正与文档同步）；**未 push、main/wip 未动**。
- 回滚点：上一验收点 `main = 7b295bff`；拥挤重做回滚点 `m6-crowd-v1 = 43306e88`。
- 构建备份：`C:\Users\moonsmile\AppData\Local\Temp\sg_build_m6p2`（12:15 旧构建，可删；`web/unity` 现在是 12:44 构建）。
