# M6 第 2 步方案：三档难度 + 协议 v2 + H5 主界面

> 文档版本：2026-11-01T19:57+08 · 作者：程序 · 状态：**待策划确认，未写实现**
> 依据：`docs/tickets/M6-内容扩展.md` §2 / §4 / §6 / §7.2；`docs/bridge-protocol.md`（v1）。
> 基线：`main = wip = 7b295bff`（拥挤重做已验收）；回滚点 `m6-crowd-v1 = 43306e88`。

## 0. 本步边界

**做**：难度数据（全部进配置）+ 协议 v2 + H5 一屏主界面（难度选择 / 分档最高分 / 一个主 CTA）。
**不做**：敌人新类型（第 4/5 步）、计分公式重做（第 3 步；本步只加难度倍率）、新素材文件、Unity 内主菜单、
攻击/远程/击杀（硬边界不变）、排行榜。

敌人仍只有「追击」1 种；同屏上限、速度、伤害、生成节奏按档位生效。

## 1. 难度数据

### 1.1 数据形状

| 件 | 内容 |
|---|---|
| `Survival.Core.Difficulty`（新，`Assets/Scripts/Core/Difficulty.cs`） | 枚举 `Easy=0 / Normal=1 / Hard=2`。全工程只用枚举比较，不写 `difficulty == "easy"` 这类字符串分支（字符串只在桥接层解析一次） |
| `DifficultyProfile`（新 ScriptableObject） | 纯数据：档位 id + 6 个乘区/上限，无逻辑 |
| 资产 | `Assets/Configs/dif_Easy.asset`、`dif_Normal.asset`、`dif_Hard.asset`，由 `SceneSetup` 生成（沿用 `EnsureEnemyDefinition` 的写法）；**已存在时不覆盖数值**，否则重建场景会把调好的平衡改回默认 |
| `GameConfig` | 新增 `DifficultyProfile[] _difficulties`（固定 Easy/Normal/Hard 顺序）+ 运行时 `SetActiveDifficulty()`；对外只读 getter。现有 `EnemyLimit` / `EnemyPressureMaxMultiplier` 改为「压测覆盖 > 档位 > 原字段」 |
| 回落 | 数组缺失/档位为空 → 用 `GameConfig` 现有字段值（等价普通档），打一次 warning；Editor 场景不接档位也能跑 |

`DifficultyProfile` 字段（本步只放**被消费**的，避免死字段；冲锋/绕行解锁时间第 4/5 步再加）：

```
_enemySpeedMultiplier     敌人基础速度乘区（生成时算一次）
_spawnIntervalMultiplier  生成间隔乘区（>1 更慢）
_damageMultiplier         接触伤害乘区
_enemyLimit               同屏上限（压测覆盖优先）
_pressureMaxMultiplier    压力曲线终点
_scoreMultiplier          存活分乘区（胜利奖励不乘）
```

### 1.2 数值表与代码落点

| 参数 | 简单 | 普通 | 困难 | 落点（单点、无分支） |
|---|---|---|---|---|
| 敌人速度倍率 | 0.90 | 1.00 | 1.10 | `EnemyAI.Initialize`：`_baseSpeed = Min(definition.MoveSpeed × 倍率 × jitter, EnemySpeedMax)`；总上限 2.2 不变 |
| 生成间隔倍率 | 1.20 | 1.00 | 0.85 | `EnemySpawner.GetCurrentInterval`：`Max(SpawnIntervalMin, (start − steps×dec) × 倍率)`，下限仍 0.5s |
| 伤害倍率 | 0.80 | 1.00 | 1.15 | `EnemyAI.Initialize` 生成时算 `contactDamage = Max(1, Round(ContactDamage × 倍率))`；伤害入口仍只有 Trigger 接触一处 |
| 同屏上限 | 14 | 20 | 20 | `GameConfig.EnemyLimit`（`?perf=` 覆盖优先） |
| 压力曲线终点 | 1.10 | 1.15 | 1.20 | `GameConfig.EnemyPressureMaxMultiplier` |
| 计分倍率 | 1.0 | 1.3 | 1.6 | `ScoreService`：只乘存活分，胜利奖励固定 200 |

- 速度/伤害在**生成时取一次**，不在 `Update` 里读配置：难度局内不可变，热路径零成本、不改 AI 帧内逻辑。
- `RestartGame` 沿用当前难度（不重新解析），`StartGame` 是唯一设置入口。
- 玩家、敌人碰撞/软墙/回收等逻辑**一行不动**，本步不碰手感。

### 1.3 第 2 步的计分中间态（需确认，见 §7）

本步只在旧公式上加难度倍率，分段存活分 + 血量分是第 3 步：

| 结果 | 简单 | 普通 | 困难 |
|---|---|---|---|
| 60 秒胜利（旧公式 600 + 200） | 600×1.0+200 = **800** | 600×1.3+200 = **980** | 600×1.6+200 = **1160** |

- 胜利奖励不乘倍率（与 §5 最终公式一致），H5 显示的「得分倍率 ×1.0/1.3/1.6」只对存活分生效。
- 受击伤害：普通 12；简单 `Round(12×0.8)=10`；困难 `Round(12×1.15)=14`。
- 第 3 步换公式后普通 60 秒满血 = 1442（票面值），届时最高分会再变一次；这是开发期中间态，不影响 v1.0.0 线上。

## 2. 协议 v2（先改文档，再改代码）

### 2.1 变更

| 项 | v1 | v2 | 说明 |
|---|---|---|---|
| `protocol` | 1 | **2** | `onReady` / `onStateChange` / `onGameOver` 统一 |
| `StartGame` 参数 | `{"nickname":"..."}` | `{"nickname":"...","difficulty":"easy\|normal\|hard"}` | 仍是一个 string 的 JSON |
| `onGameOver.payload` | 无难度 | 新增 `"difficulty"` | Unity **实际采用**的档位（非法值/缺省已回落 normal），H5 以此分档记分，不靠自己的猜测 |
| `RestartGame` | 沿用 | 沿用当前难度 | 参数不变 |
| 状态机 | Ready→Playing→Won/Lost | **不变** | 难度是 `StartGame` 的参数，不是状态；**不新增 Menu 状态**（D1 选 H5 主界面） |

### 2.2 校验与兼容矩阵

- 非法/缺失/大小写异常 → `normal`，打一次 warning；纯字符串载荷（v1 写法）→ 只取昵称、难度 normal。
- 难度只在 `Ready → Playing` 时设置；Playing/Paused/Won/Lost 期间收到的 `StartGame` 仍按现有状态守卫忽略。

| H5 | Unity | 结果 |
|---|---|---|
| v1（无 difficulty） | v2 | 按 normal 开局（旧页面不崩） |
| v2 | v1（旧缓存包） | `JsonUtility` 忽略未知字段 → normal；H5 收到 `protocol:1` 只 `console.warn`，不阻塞 |
| v2 | v2 | 按选择开局 |

- `docs/bridge-protocol.md`：标题改 protocol 2，§2 表格补参数、§3 补字段、§4 时序补难度、§6 版本历史加一行；`web/js` 的 `?v=` 同步提到 `1.1.0`。
- `onGameOver.breakdown` 本步**不改形状**（仍 `survival / kills / winBonus`），第 3 步再补血量明细，避免同一步既改公式又改协议。

## 3. H5 主界面（一屏、一个主 CTA）

### 3.1 DOM 顺序（自上而下）

```text
header      : 标题 → 一句话规则（躲开追踪你的敌人，存活 60 秒）→ 昵称
难度区       : 三张卡（名称 / 一句话 / ×倍率）
最高分行     : 「普通最高分 980」（跟随当前选择刷新，只显示一档）
game-frame  : 画布（与原位置一致）
controls    : [开始游戏]（主 CTA） [全屏]（次要） 状态提示
op-hint     : 操作提示
result      : 结算卡（新增「难度」项，最高分改为「本难度最高分」）
footer
```

- **难度卡位置**：按票面顺序放在画布**之上**。Playing 时整个难度区 + 最高分行 `hidden`，画布回到顶部 —— 布局变化只发生在「开始」那一下，不来回跳。
- 只有 Ready（含 Loading/Starting）能看到难度卡与开始按钮；其余状态隐藏（`renderButtons` 统一管）。
- 不做新页面、不做弹窗：难度卡就是页面内的一组 radio。

### 3.2 难度卡实现

- 原生 `<input type="radio" name="difficulty">` + 样式化 `<label>`：键盘方向键、读屏、「disabled」全部浏览器原生，不需要 JS 处理按键。
- 命中区 ≥44 CSS px（短横屏 ≥36），选中态用描边 + 底色，不只靠颜色（色弱可辨）。
- 文案（H5 静态，中文只在 H5 渲染，不进 Unity 字体图集）：简单「敌人更慢、更少」×1.0；普通「标准」×1.3；困难「敌人更快、更多」×1.6。
- 倍率文字与 Unity 配置是两处：本步接受静态文案（同一次改动同时更新两处 + E2E 断言），不做 Unity→H5 下发（要下发就得往 `onReady` 加数组，本步不扩协议面；如需另议）。

### 3.3 全屏修复（M4.5 遗留）

现状 `html:fullscreen .controls { display:none }` 会把开始按钮一起藏掉。改法：

- `app.js` 在 `setPage` 里同步 `document.body.dataset.page = page`。
- CSS 从「全屏一律隐藏 .controls」改成按状态：Playing/Paused/Result 隐藏整块；**Ready 保留**难度区、最高分行、开始按钮、全屏/退出按钮，只隐藏 header/提示/footer。
- Ready 全屏下必须能选难度并点开始（验收项）。

### 3.4 响应式

- 视口矩阵沿用现有 6 档（含 320×568 / 390×844 / 844×390 / 1280×720）：不溢出、不重叠、`#btn-start` 与难度卡在首屏可见。
- 竖屏 320 宽：难度卡三列等宽、隐藏每卡第二行描述（只留名称 + 倍率），避免把开始按钮挤出首屏；`hover:none` 设备不依赖 hover 态。

## 4. 分档最高分

| key（`storage.js` 自动加 `survival.` 前缀） | 值 | 默认 |
|---|---|---|
| `bestScore.easy` / `bestScore.normal` / `bestScore.hard` | 非负整数 | 0 |
| `difficulty` | `easy` / `normal` / `hard` | `normal`（非法值回落） |

**迁移（只迁一次）**：

1. 读旧键 `bestScore`（v1.0.0 写入的单一最高分）。
2. 仅当 `bestScore.normal` **不存在**且旧值 ≥0 时：写 `bestScore.normal = 旧值`，成功后 `remove('bestScore')`；写失败（隐私模式/配额）保留旧键，下次再试。
3. 不换算：v1.0.0 的分数没有难度倍率，按普通档原值迁移；换算会让旧纪录凭空变高。旧值偏低，刷新纪录更容易，属可接受偏差（交付说明里写一句）。

**游戏流程**：

- 选难度 → 立即写 `difficulty`；最高分行切到该档。
- 结算：`payload.difficulty` 合法则用它分档，否则用本局发送值；`isNewRecord` 只与本档最高分比较；跨档互不影响。
- 结算卡：本次得分 / 本难度最高分 / 难度 / 存活秒数 / 昵称 / 新纪录。「重新开始」沿用同档。
- `tools/e2e/smoke.mjs` 现在直接读 `SurvivalStorage.getInt('bestScore', 0)`，改为读本档 key；并新增迁移断言。

## 5. 实施顺序、构建次数、验收点

| # | 内容 | 构建 | 验收点 |
|---|---|---|---|
| 1 | `docs/bridge-protocol.md` 升 v2（§2/§3/§4/§6）+ 本票 §7.2 状态 | 无 | 策划确认字段与状态机；单独 `docs(m6-p2)` 提交 |
| 2 | Unity：枚举 + `DifficultyProfile` + `GameConfig` + `GameManager.StartGame(Difficulty)` + 桥接解析 + Spawner/AI/Score 乘区 + `SceneSetup` 生成资产并重建场景 | **构建 #1（唯一必做）** | Editor 日志/探针：easy 上限 14、首间隔 1.8s；hard 首间隔 1.275s、伤害 14；`StartGame("{\"difficulty\":\"hard\"}")` 60s 分 = 1160；非法值→normal；纯昵称字符串→normal |
| 3 | H5：主界面 + 难度卡 + 分档最高分 + 迁移 + 全屏修复 + E2E 新用例 | 无（静态文件；`?v=1.1.0`） | 三档可选可开、刷新记住；迁移用例；跨档不串分；Ready 全屏可开始；6 视口；console 0 error；E2E smoke 3 局 + 三档 + 非法值 |
| 4 | 真机 Honor 90 Pro（**需要用户操作**） | 用 #1 产物 | 三档各 1 局（至少 easy/hard）+ `?perf=1` 40 只抽查 + 全屏/失焦暂停回归；截图/录屏 |
| 5 | 提交：`feat(m6-p2)` + 场景/资产；推 `wip`；`main` 等验收后前移 | 无 | `jj diff --stat` + 证据路径 |

- **预计构建 1 次**；只有 Unity 侧被 E2E 拉出问题时才加第 2 次（4 分钟/次）。
- 证据路径：`docs/evidence/m6-step2-*.{png,json}` + E2E 控制台输出；真机录屏由用户提供或我给清单。
- 每步单独提交；`main` 只在验收后前移（协作协议）。

## 6. 风险与停点

| 风险 | 处理 |
|---|---|
| `SceneSetup` 重建 `Game.unity` 带入无关改动 | 重建后先看 `jj diff Assets/Scenes/Game.unity`：只应有新字段/新引用；出现意外差异就停，先报告 |
| H5 倍率文案与 Unity 配置漂移 | 同一次改动两处 + E2E 断言（断言的是「卡面显示」与「结算分」的关系，不读 Unity 内部） |
| 旧 Unity 包被浏览器缓存，新 H5 发 difficulty | `netlify.toml` 已 no-cache；协议层已兼容（未识别的字段被忽略 → normal） |
| 难度卡把开始按钮挤出 320×568 首屏 | 竖屏隐藏描述行 + gap 压缩；6 视口截图验收，不达标就再压，不加滚动 |
| 真机门禁 | 需要用户操作，我提供一页清单 + 构建产物路径 |
| 计分中间态与第 3 步冲突 | §1.3 明确本步只乘倍率；第 3 步换公式后重记基线，最高分重置一次（开发期，不进 Release） |

## 7. 需要策划确认的点（带推荐）

1. **难度卡位置**：推荐放画布之上（按票面顺序），Playing 时整块隐藏 —— 只接受「放画布下方」的反例。
2. **第 2 步计分中间态**：推荐只乘难度倍率（60 秒胜利 800 / 980 / 1160），分段存活分与血量分留到第 3 步。
3. **难度数据形态**：推荐 3 个 `DifficultyProfile` SO 资产（Inspector 可调、场景可重建）；备选是嵌在 `GameConfig` 里的子结构（资产少一个，但调整时整块在一个文件里）。
4. **旧最高分迁移**：推荐按普通档原值迁移、不换算（理由见 §4）。
5. **H5 倍率文案来源**：推荐静态文案 + 同步更新（本步不扩协议）；备选 `onReady` 下发难度表（协议面变大，H5 依赖 Unity 才画卡）。

## 8. 预计改动文件

```text
docs/bridge-protocol.md                         协议 v2
docs/tickets/M6-内容扩展.md                      §7.2 状态/执行记录
docs/proposals/proposal-M6-步骤2-难度与协议v2.md  本文件

Assets/Scripts/Core/Difficulty.cs               新增（枚举）
Assets/Scripts/Core/DifficultyProfile.cs        新增（SO）
Assets/Scripts/Core/GameConfig.cs               档位数组 + getter
Assets/Scripts/Core/GameManager.cs              StartGame(Difficulty)
Assets/Scripts/Core/ScoreService.cs             难度倍率
Assets/Scripts/Enemy/EnemySpawner.cs            间隔倍率
Assets/Scripts/Enemy/EnemyAI.cs                 速度/伤害倍率
Assets/Scripts/Bridge/WebGLBridge.cs            解析 difficulty + 回传
Assets/Editor/SceneSetup.cs                     生成 3 个档位资产并挂到 GameConfig
Assets/Configs/dif_*.asset                      新增（生成）
Assets/Configs/cfg_GameConfig.asset             新字段
Assets/Scenes/Game.unity                        重建

web/index.html / web/js/app.js / web/css/style.css   主界面 / 分档 / 全屏
tools/e2e/smoke.mjs                                  新档位用例 + 迁移断言
```
