# 旧 H5（v1.1）E2E 存档

这里的脚本针对 **v1.1 页面**（H5 拥有难度卡/开始/3-2-1/暂停/结算、`data-page=Ready/Result`、
`#result`、`#btn-restart`、`window.SurvivalApp.__sendMessage`）。v1.2 薄壳把这些 DOM 和状态机全部删除，
因此**这些脚本不可运行**，仅保留用于追溯当时的断言口径。

对应脚本与替代：

| 旧脚本 | 替代 |
|---|---|
| `difficulty-e2e.mjs` | `h5-shell.mjs`（难度偏好）+ `standalone-menu.mjs`（难度/数值） |
| `menu-check.mjs` | `h5-shell.mjs MODE=viewports` |
| `fullscreen-states.mjs` / `fullscreen-shot.mjs` | `h5-shell.mjs`（core 内含全屏用例） |
| `responsive-shot.mjs` | `h5-shell.mjs MODE=viewports` |
| `joystick-shot.mjs` | 保留 1.0.0 时期证据；v1.2 布局变化后如需重拍，按 `h5-shell` 的画布点击方式重写 |
| `crowd-clip.mjs` | 拥挤手感取证；需要时基于 `standalone-common.mjs` 重写 |
| `h5-transition.mjs` | 旧 H5 + 新 Unity 的过渡验证；v1.2 页面替换后不再需要 |

不要为这些脚本修 import 路径或改断言——它们依赖的页面已经不存在。需要新的回归时，
在 `tools/e2e/` 下用 `standalone-common.mjs` + `ui-layout.mjs` 写新脚本。
