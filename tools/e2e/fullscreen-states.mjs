// 全屏口径（L1 用户要求，推翻 M6 §2 的「Ready 全屏」）：
// - 只有 Playing / Paused 提供全屏；Ready / Starting / Result 不显示全屏按钮。
// - 全屏里网页端可见按钮 = 0：不提供「退出全屏」等任何网页控件，退出靠 Esc / 系统手势；
//   页头、难度区、控制区、页脚、结算卡、全屏提示、倒计时覆盖层全部隐藏。
// - Unity 暂停面板/暂停按钮在画布内，不算网页控件；全屏下仍可用。
// 用法：BASE_URL=http://127.0.0.1:8125 VIEWPORT=1280x800 node fullscreen-states.mjs
import { chromium } from 'playwright';
import { chromiumLaunchOptions } from './launch.mjs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const evidenceDir = path.resolve(here, '..', '..', 'docs', 'evidence');

const BASE_URL = (process.env.BASE_URL || 'http://127.0.0.1:8000').replace(/\/$/, '');
const [width, height] = (process.env.VIEWPORT || '1280x800').split('x').map(Number);
const label = process.env.LABEL || `${width}x${height}`;

const browser = await chromium.launch(chromiumLaunchOptions());
const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: 1 });
const page = await context.newPage();

let failed = 0;
const check = (name, ok, detail) => {
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail === undefined ? '' : ' — ' + detail}`);
  if (!ok) failed += 1;
};
const fullscreenButton = () => page.locator('#btn-fullscreen');
const visible = async (locator) => {
  const box = await locator.boundingBox();
  return !!box && box.width > 0 && box.height > 0;
};
// 网页端可见按钮数：hidden / display:none / 0 尺寸都算不可见。
const visibleButtonCount = () => page.evaluate(() => (
  Array.from(document.querySelectorAll('button')).filter((el) => {
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0 && getComputedStyle(el).visibility !== 'hidden';
  }).length
));
const shellVisible = () => page.evaluate(() => {
  const isVisible = (selector) => {
    const el = document.querySelector(selector);
    if (!el) return false;
    const rect = el.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0 && getComputedStyle(el).display !== 'none';
  };
  return {
    header: isVisible('.header'),
    difficulty: isVisible('#difficulty'),
    controls: isVisible('.controls'),
    footer: isVisible('.footer'),
    opHint: isVisible('.op-hint'),
    result: isVisible('#result'),
    fullscreenHint: isVisible('#fullscreen-hint'),
    countdown: isVisible('#countdown'),
  };
});
const allHidden = (shell) => Object.values(shell).every((value) => value === false);

await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
await page.waitForSelector('#btn-start:not([disabled])', { timeout: 300000 });

// Ready：不给全屏入口（L1 推翻 M6 §2 的 Ready 全屏）。
check('Ready 不显示全屏按钮', !(await visible(fullscreenButton())));
const readyButtons = await visibleButtonCount();
check('Ready 网页可见按钮只有开始（不提供全屏）', readyButtons === 1, `buttons=${readyButtons}`);

await page.fill('#nickname', '全屏门禁');
await page.click('#btn-start');
await page.waitForFunction(
  () => (document.querySelector('#status-hint')?.textContent || '').includes('游戏中'),
  null,
  { timeout: 30000 },
);
check('Playing 时全屏按钮可见', await visible(fullscreenButton()));
check('Playing 全屏按钮落在控制区', await page.evaluate(() => {
  const button = document.querySelector('#btn-fullscreen');
  const controls = document.querySelector('.controls');
  return !!button && !!controls && controls.contains(button);
}));

await page.click('#btn-fullscreen');
await page.waitForFunction(() => !!document.fullscreenElement, null, { timeout: 8000 });
await page.waitForTimeout(500);
check('Playing 能进入全屏', await page.evaluate(() => !!document.fullscreenElement));

// 核心口径：全屏里没有任何网页端按钮 / 外壳。
const fsButtons = await visibleButtonCount();
check('全屏内网页端可见按钮为 0', fsButtons === 0, `buttons=${fsButtons}`);
const fsShell = await shellVisible();
check('全屏内页头/难度/控制区/页脚/操作提示全部隐藏', allHidden(fsShell), JSON.stringify(fsShell));
await page.screenshot({ path: path.join(evidenceDir, `resp-${label}-fullscreen-gate-playing.png`) });

// Paused 全屏：Unity 暂停面板仍可用（键盘 P 走的是 Unity 暂停/恢复状态机；
// 面板本身的「继续」按钮在画布内，不是网页控件）。
await page.keyboard.press('p');
await page.waitForFunction(
  () => (document.querySelector('#status-hint')?.textContent || '').includes('已暂停'),
  null,
  { timeout: 10000 },
);
check('全屏内按 P 能暂停（Unity 暂停面板仍可用）', true);
const pausedShell = await shellVisible();
check('Paused 全屏内仍无网页外壳', allHidden(pausedShell), JSON.stringify(pausedShell));
check('Paused 全屏内仍无网页按钮', (await visibleButtonCount()) === 0);
await page.screenshot({ path: path.join(evidenceDir, `resp-${label}-fullscreen-gate-paused.png`) });

await page.keyboard.press('p');
await page.waitForFunction(
  () => (document.querySelector('#status-hint')?.textContent || '').includes('游戏中'),
  null,
  { timeout: 20000 },
);
check('全屏内从暂停恢复（3-2-1 后回到游戏）', true);

// 退出：优先 Esc；无头环境若 Esc 不生效，退回 exitFullscreen API（两者都是平台原生退出路径）。
await page.keyboard.press('Escape');
await page.waitForTimeout(600);
if (await page.evaluate(() => !!document.fullscreenElement)) {
  await page.evaluate(() => document.exitFullscreen());
  await page.waitForFunction(() => !document.fullscreenElement, null, { timeout: 8000 });
  console.log('      注：无头 Chrome 下 Esc 未退出全屏，改用 document.exitFullscreen()（平台原生 API）');
}
check('Esc / 原生 API 能退出全屏', !(await page.evaluate(() => !!document.fullscreenElement)));
check('退出后页面恢复：全屏按钮可见', await visible(fullscreenButton()));
check('退出后页面恢复：难度区可见', await visible(page.locator('#difficulty')));
check('退出后页面恢复：页头可见', await visible(page.locator('.header')));
await page.screenshot({ path: path.join(evidenceDir, `resp-${label}-fullscreen-gate-exited.png`) });

// 无头 Chrome 不把 Esc 当全屏退出键，却会把 Esc 传给 Unity（Esc 也是暂停热键），
// 于是退出全屏后游戏停在 Paused。真实浏览器里 Esc 由浏览器消费、通常不会到页面；
// 这里按实际状态恢复，保证后面的自然结算用例不被暂停卡死。
const hintAfterExit = await page.evaluate(() => document.querySelector('#status-hint')?.textContent || '');
if (hintAfterExit.includes('已暂停')) {
  console.log('      注：Esc 同时触发 Unity 暂停，按 P 恢复后再等结算');
  await page.keyboard.press('p');
  await page.waitForFunction(
    () => (document.querySelector('#status-hint')?.textContent || '').includes('游戏中'),
    null,
    { timeout: 20000 },
  );
}

// 结算：不操作，等自然结束（最长 60 秒）。
await page.waitForSelector('#result:not([hidden])', { timeout: 120000 });
await page.waitForTimeout(400);
check('Result 不提供全屏', !(await visible(fullscreenButton())));
check('Result 时结算卡可见', await visible(page.locator('#result')));

await browser.close();
console.log(failed === 0 ? '全部通过' : `${failed} 项失败`);
process.exit(failed === 0 ? 0 : 1);
