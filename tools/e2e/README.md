# E2E / 压测脚本（v1.2）

独立 `package.json`，只依赖 Playwright；不进入 `web/`，不参与发布。

> **前置**：H5 与 standalone 脚本都需要 `web/unity` 里是可运行的对应构建。
> v1.2 薄壳要求 `protocol 3`（`productVersion=1.2.0`）；仓库内目前可能是旧的 1.0.0，
> 测试前把 1.2.0 构建临时复制进 `web/unity`，跑完 `jj restore web/unity`（见 `docs/遗留问题.md` §2）。

## 有效脚本（v1.2）

| 脚本 | 作用 | 关键环境变量 |
|---|---|---|
| `smoke.mjs` | 薄壳主流程：加载 → 昵称 → 进入 → 画布菜单开局 → 暂停/继续 → 短局结算 → 再来一局/返回菜单 → 刷新保留 | `BASE_URL`、`E2E_PROXY`、`BROWSER`、`MOBILE`、`HEADED`、`READY_TIMEOUT`、`ROUND_SECONDS` |
| `h5-shell.mjs` | H5 薄壳验收：`MODE=core`（协议/昵称/进入/难度偏好/结算/最高分/迁移/全屏）、`MODE=viewports`（6 视口；移动视口 `hasTouch`，390×844 跑真实 2s 短局，其余 GameOver 为 `layout-only`） | `MODE`、`PORT`、`HEADED`、`READY_TIMEOUT`、`GAME_TIMEOUT` |
| `standalone-menu.mjs` | 无 H5 直开 Unity 构建：菜单/难度/3-2-1/暂停/继续/重开/结算/再来一局/返回菜单 | `MODE=flow\|mobile\|viewports`、`PORT`、`HEADED`、`EVIDENCE_TAG` |
| `standalone-focus.mjs` | 失焦暂停 + 切回保持暂停（原生 blur / visibilitychange） | `PORT`、`HEADED`、`READY_TIMEOUT` |
| `standalone-perf.mjs` | standalone 40 只压测（headed、离屏窗口） | `CAP`、`PERF_SECONDS`、`PERF_TIMEOUT` |
| `perf.mjs` | 交付页 40 只压测（`?perf=1&autostart=1`，不再依赖 H5 开始按钮） | `BASE_URL`、`CAPS`、`PERF_SECONDS`、`BROWSER`、`EXTRA_QUERY` |
| `check-version.mjs` | 校验 `web/index.html` 的 `?v=` 与 `web/unity/build-info.json.productVersion` 一致；不一致非 0 退出 | — |
| `legacy/` | v1.1 旧 H5 的脚本存档，**不可运行**（依赖已删除的 DOM/状态机）；只供追溯 | — |

## 跑一次

```bash
cd tools/e2e
npm install                 # 首次（Playwright 复用本机 Chrome/Edge，不下载 Chromium）

# H5 薄壳（默认 core 含迁移/协议不匹配/全屏）
node h5-shell.mjs
MODE=viewports node h5-shell.mjs

# 薄壳主流程（本地静态服务默认 8123）
node smoke.mjs

# 无 H5：直接打开 Unity 构建模板
node standalone-menu.mjs
MODE=mobile node standalone-menu.mjs

# 版本一致性（在仓库根或 tools/e2e 下都可跑）
node tools/check-version.mjs
```

- 默认无头，`HEADED=1` 才开真实窗口；无头 Chrome 走 SwiftShader，**帧率结论必须用 headed/真机**。
- 线上跑必须给代理：`E2E_PROXY=http://127.0.0.1:7897`（国内直连 Netlify 大文件会被掐断）。
- `BASE_URL=https://...` 时 `smoke.mjs` 不再起本地服务；H5 的 `?testhook=1` 由脚本附加。

## 证据

- H5 / v1.2：`docs/evidence/m6-refactor/`（`h5-shell-*`、`standalone-*`、`perf-40.json`、`h5-transition-*`）。
- 旧 H5：`docs/evidence/e2e-*`（1.0.0 时期，历史，不覆盖）。
- 任何断言失败 → 退出码 1；不要把失败截图当通过证据。

## 已知限制

- `ui-layout.mjs` 的点击坐标由 `SceneSetup` 锚点推导，改菜单/面板布局必须同步；
  它目前按 canvas CSS 宽判断 640/1280 参考分辨率，DPR>1 的窄平板边界尚未实测（`docs/代码审视-2026-11-02.md` P1-8）。
- 无头/软件渲染下 Unity 帧率很低（`Time.maximumDeltaTime=0.1` 让游戏时间约慢 10 倍），流程测试要留足超时。
- 旧 H5 过渡脚本 `legacy/h5-transition.mjs` 需要 v1.1 页面，v1.2 页面替换后无法复跑。
