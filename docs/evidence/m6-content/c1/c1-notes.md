# C1 — 敌人数据模型与三定义资产（无行为变化）

> 日期：2026-11-02 · 类型：内部验证构建（productVersion 1.2.1，**未入库、未部署**）
> 构建：`builtAt 2026-11-02T14:07:27Z`（本地 22:07），约 9.2 MB
> 仓库外副本：`D:/unityFile/_builds/SurvivalGame-1.2.1-c1-2026-11-02/`
> `web/unity` 测试后已 `jj restore` 回 1.2.1；本 checkpoint 不提交构建产物。

## 本步改动

- 新增 `EnemyKind`（Chaser/Charger/Flanker）。
- `EnemyDefinition` 增加 kind / tint / visualScale / 冲锋参数 / 绕行参数；三份资产由 `SceneSetup` 生成：
  - `def_Enemy_Chaser.asset`：kind=0，速度 1.8，伤害 12，半径 0.38，白/1.0。
  - `def_Enemy_Charger.asset`：kind=1，速度 1.5，伤害 12，半径 0.38，橙/1.05；
    冲锋参数 4.5 / 0.45 / 0.45 / 5.0 / 0.8 / 1.5 / 15。
  - `def_Enemy_Flanker.asset`：kind=2，速度 1.9，伤害 12，半径 0.38，青/0.95；
    绕行参数 2.4 / 135° / 1.2 / 6。
- `DifficultyProfile` 增加解锁/权重字段，旧 `dif_*.asset` 一次性迁移：
  - 简单 25 / 45；普通 15 / 35；困难 10 / 25。
  - 权重按计划 §2.1 写入；**`_enemyLimit` 保持 14/20/20**（C5 再改 14/30/50）。
- `SceneSetup` 把三份定义挂到 `EnemySpawner._enemyDefinitions`；`PickDefinition` 仍只返回 Chaser。
- 无 AI 行为变化、无计分变化、无 UI/协议变化。

## 验证

- Unity 编译：0 error（MCP `read_console` 仅有一条历史构建提示）。
- `Survival/Setup Scene`：创建 Charger/Flanker、迁移三档权重、场景保存成功，无 error。
- 构建：`Survival/Build WebGL` 成功（`build-info.productVersion=1.2.1`）。
- `node tools/e2e/smoke.mjs`：**9/9 PASS**，结算仍为「普通 265」，console 0 未预期 error →
  证明 C1 没有改变 Chaser/计分/状态机。

## 说明

- `Assets/Fonts/NotoSansSC-UI-SDF.asset` 由构建重烤，C1 无文案变化，已还原，不纳入本 checkpoint。
- 场景由 `SceneSetup` 重建，`Game.unity` 的 diff 较大（与历史重建一致）。
- 下一步 C2：`EnemyAI` 公共管线抽取（行为不变），构建后做 40 只回归。
