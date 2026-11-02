# C2 — EnemyAI 公共管线抽取（行为不变）

> 日期：2026-11-02 · 类型：内部验证构建（productVersion 1.2.1，**未入库、未部署**）
> 状态：**代码/性能通过；构建水印按用户 2026-11-02 确认留作内部证据，不作为最终视觉；C7 必须干净重建。**
> 构建副本：
> - before（C1）：`D:/unityFile/_builds/SurvivalGame-1.2.1-c1-2026-11-02/`（`builtAt 2026-11-02T14:07:27Z`）
> - after（C2）：`D:/unityFile/_builds/SurvivalGame-1.2.1-c2-2026-11-02/`（`builtAt 2026-11-02T14:20:04Z`）

## 本步改动（只有 1 个文件）

- `Assets/Scripts/Enemy/EnemyAI.cs`：把原 `Update` 里的
  「被挡/被顶测量 → 玩家侧闪 → 切向让位 → 软墙 → 速度平滑 → 防重合 → 积分」
  原样搬进 `ApplyCrowdMovement(...)`，`Update` 只负责取方向/距离/seekVelocity 后调用它。
- **没有改任何参数、顺序、判定、速度上限、停距、回收计时**；Chaser 行为逐行等价。

## 40 只对照（同一 `?perf=1&autostart=1&enemyLimit=40&roundDuration=600&invincible=1`，90s）

| 指标 | C1 before | C2 after | 差值 |
|---|---|---|---|
| 到顶 capAt | 49.43s | 49.45s | +0.02s |
| steadyFps | 165 | 165 | 0 |
| p95 / p99 | 7 / 7 ms | 7 / 7 ms | 0 |
| steadyMaxMs | 7 | 8 | +1（噪声） |
| steadyAllocKBps | 30.2 | 29.4 | −0.8 |
| totalAllocMB | 2.97 | 2.94 | −0.03 |
| steadyMotionUps | 0.001 | 0.001 | 0 |
| steadySettled | 5 | 5 | 0 |
| steadyMinPairDist | 0.709 | 0.740 | +0.031 |
| heapKB / wasmMB | 2620 / 55.4 | 2620 / 55.4 | 0 |
| gc | 11 | 11 | 0 |
| peakAlive | 40 | 40 | 0 |
| consoleErrors | [] | [] | — |

- 证据 JSON：`before/perf-local-chrome-desktop-cap40-c1-before.json`、`after/perf-local-chrome-desktop-cap40-c2-after.json`。
- 结论：性能与拥挤指标在噪声范围内一致，没有可见的玩法回归。

## 短局 smoke

- before：9/9 PASS，结算「普通 265」，console 0 未预期 error。
- after：9/9 PASS，结算「普通 265」，console 0 未预期 error。
- 截图：`before/smoke-local-chrome-desktop-*`、`after/smoke-local-chrome-desktop-*`。

## 阻塞：`trial version` 水印（已确认按内部证据继续）

- C2 构建的 `gameover` / `perf` 截图右下角出现 **`trial version`**；C1 构建同场景没有。
- 玩法/UI/布局/分数在两张图中一致（只有敌人 RNG 位置不同）；水印是 Unity 构建的许可证标记，不是代码差异。
- **用户 2026-11-02 确认**：C2 水印留作内部性能/视觉对照，不作为最终视觉；C7 必须等用户重启 Unity 并确认
  `Help > Manage License` 是 Unity Personal 后干净重建；若 C7 仍带 `trial version`，停，不带水印交付。
- 已核对：
  - `C:\ProgramData\Unity\Unity_lic.ulf`：`UnityPersonal`，`StopDate 2026-12-01`；
  - `%LOCALAPPDATA%\Unity\licenses\UnityEntitlementLicense.xml`：`Unity Personal Version`，`UpdateDate 2026-12-02`；
  - 许可证文件在 **22:08** 被刷新（正好在 C1 构建之后、C2 构建之前），运行中的编辑器很可能仍持有旧的/trial 授权状态；
  - Editor.log 最近构建无编译错误，构建成功。
- 处理：**先停，不提交 C2**。需要用户在 Unity 里刷新/重启授权后重跑 C2 构建与截图；代码与性能证据保留。

## 提交与后续

- 本 checkpoint 提交 `EnemyAI.cs` + 本目录证据；提交说明标明「水印构建，仅内部对照，C7 干净重建」。
- 继续 C3/C4；C3/C4 截图即使带水印也先给用户看手感，标注“内部、非最终视觉”。
- C7 前必须重启 Unity 并确认 `Help > Manage License` 是 Unity Personal，干净重建；若仍带 `trial version` 则停，不带水印交付。
