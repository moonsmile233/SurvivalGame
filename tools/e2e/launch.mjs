// E2E 浏览器启动参数：默认无头，避免测试时弹窗打扰使用者（2026-11-01 用户反馈）。
// HEADED=1 才开真实窗口，并把窗口移到屏幕外（-32000,-32000），仍然不闪。
// 无头 Chrome 用 SwiftShader 软件渲染，只为跑通 Unity WebGL 与截布局；帧率/性能结论以真机为准。
export const HEADED = process.env.HEADED === '1';

const HEADLESS_ARGS = ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'];
const HEADED_ARGS = ['--window-position=-32000,-32000'];

/** Chromium（Chrome/Edge）统一参数；extra 可覆盖 channel/proxy 等。 */
export function chromiumLaunchOptions(extra = {}) {
  return {
    channel: 'chrome',
    headless: !HEADED,
    args: HEADED ? HEADED_ARGS : HEADLESS_ARGS,
    ...extra,
  };
}
