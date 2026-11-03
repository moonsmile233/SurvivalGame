# C4 — Flanker（绕行者）代码与未完成验证

> 日期：2026-11-03 · 状态：**代码已落盘，未编译/未构建/未验证**
> 原因：UnityMCP 8081 服务在监听，但编辑器桥接 ping 不响应（插件每 ~45s 注册后断开 1005）；
> 编辑器主线程疑似被弹窗/对话框/编译弹窗卡住。`web/unity` 仍是 1.2.1；本步未提交构建。

## 代码

- `Assets/Scripts/Enemy/EnemyAI.cs`：
  - `FlankerState { Flank, Pounce, Direct }`；`FlankEngageDistance = 4.5`（进入交战距离后才计 6s 侧绕时间，
    避免从出生点 11.7u 赶路把计时耗光）。
  - Flank：目标点 = 玩家 + 侧后方 `2.4`（相对玩家速度方向；静止时用“玩家→绕行者”方位），目标点钳在地图内；
    距目标 <0.6 后直接 seek 玩家；距玩家 ≤`1.2` → Pounce；6s 超时 → Direct。
  - Pounce/Direct：正常 seek + 公共软墙/让位/防重合；接触伤害仍 12×难度倍率。
  - `SetLogFlankerStates` 只给 `enemyKind=flanker&enemyLimit≤2` 的探针用。
- `Assets/Scripts/Core/PerfProbe.cs`：强制 flanker 时打开绕行状态日志（仅小上限探针）。
- `tools/e2e/enemy-probe.mjs` 已支持 `KIND=flanker`（默认 headless，Flank/Pounce/Direct 自动截图）。

## 待验证（下一会话）

```bash
cd tools/e2e
KIND=flanker node enemy-probe.mjs        # 期望 [Flanker] Flank → Pounce / Direct；截图在 review/m6-c3-flanker/
CAPS=40 EXTRA_QUERY=enemyKind=flanker RUN_LABEL=c4-40 PERF_SECONDS=90 node perf.mjs
```

- 目标：侧后 2.4 可见、≤1.2 扑、6s 超时直追；40 只 FPS/p95 不明显劣于 C3。
- 完成后：`jj restore web/unity Assets/Fonts/NotoSansSC-UI-SDF.asset Assets/Scenes/Game.unity`，
  证据放本目录，提交 C4，只推 wip。
- 水印构建仅内部证据，C7 必须用干净授权重建。
