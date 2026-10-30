# E2E smoke（M4）

不进入 `web/`，不参与线上发布。独立 `package.json`，只依赖 Playwright。

## 跑一次

```bash
cd tools/e2e
npm install            # 首次（Playwright 会复用本机 Chrome/Edge，不下载 Chromium）
node smoke.mjs         # 默认：本地起静态服务 + 系统 Chrome（有头）
```

可用环境变量：

| 变量 | 默认 | 说明 |
|---|---|---|
| `BASE_URL` | 空 | 给了就打这个地址（如 `https://survivalgame-demo.netlify.app`），不再起本地服务 |
| `BROWSER` | `chrome` | `chrome` / `edge` / `firefox`（firefox 用 Playwright 自带内核） |
| `MOBILE` | `0` | `1` → 390×844 触屏上下文，并做一次 CDP 触摸拖动 |
| `HEADLESS` | `0` | `1` → 无头（Unity WebGL 在无头下帧率低，仅用于快速冒烟） |
| `PORT` | `8123` | 本地静态服务端口 |
| `RESULT_TIMEOUT` | `300000` | 等一局结束的毫秒数（不操作时约 20~40s 死亡，无头会更慢） |
| `ROUNDS` | `3` | 总局数（第 1 局结束后点「重新开始」跑满） |
| `AUTOMOVE` | `0` | `1` → 第 1 局用键盘方形走位（每边 2.6s），用来跑满 60 秒做自动化胜利回归；建议配合 `ROUNDS=1` |
| `RUN_LABEL` | 空 | 额外写一份 `summary-<label>.json`（如 `10rounds`、`win`），避免后来的运行覆盖不同场景的摘要 |
| `NICKNAME` | `E2E测试` | 测试昵称 |

示例：

```bash
BASE_URL=https://survivalgame-demo.netlify.app node smoke.mjs
MOBILE=1 node smoke.mjs
BROWSER=edge node smoke.mjs
```

## 断言

- 能加载到 `onReady`（开始按钮可用）
- 昵称 → 开始 → H5 进入「游戏中」
- 真实窗口失焦（另开一个浏览器窗口并聚焦）→ H5「已暂停」
- 切回 → 3-2-1 后自动恢复「游戏中」
- 结算卡片出现：标题、本次得分、最高分、昵称、存活秒数
- 连续重开 2 次（共 3 局）：每局都能重新进入 Playing，分数不累加
- 刷新后昵称与最高分仍在
- 页面 console 无 error（忽略 WebGL INVALID_ENUM 噪声）

截图与日志写到 `docs/evidence/e2e-{online|local}-{chrome|edge|firefox}-{desktop|mobile}-*.png` / `.log`；
结果摘要在同目录的 `e2e-*-summary*.json`。任何断言失败 → 退出码 1。
