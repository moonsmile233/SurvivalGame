// 全屏口径（L1.1 用户要求）：
// - Ready / Starting / Playing / Paused 都能进全屏；Result 不提供。
// - Ready / Starting 全屏保留菜单：难度区 + 最高分行 + 开始按钮（否则进全屏没法开局）。
// - 游戏中（Playing / Paused）全屏：网页端可见按钮 = 0，网页外壳全部隐藏；画布内 Unity 控件保留。
// - 全屏内没有网页「退出全屏」按钮：退出靠 Esc / 系统手势 / 原生 API；fullscreenchange 负责恢复。
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
  };
});
const allHidden = (shell) => Object.values(shell).every((value) => value === false);

await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
await page.waitForSelector('#btn-start:not([disabled])', { timeout: 300000 });

// ---------- Ready：主界面能进全屏，且全屏里仍可看菜单、点开始 ----------
// 昵称在页头里，全屏时页头隐藏，所以先填昵称再进全屏。
await page.fill('#nickname', '全屏门禁');
check('Ready 显示全屏按钮', await visible(fullscreenButton()));
await page.screenshot({ path: path.join(evidenceDir, `resp-${label}-fullscreen-gate-ready-menu.png`) });
await page.click('#btn-fullscreen');
await page.waitForFunction(() => !!document.fullscreenElement, null, { timeout: 8000 });
await page.waitForTimeout(500);
check('Ready 能进入全屏', await page.evaluate(() => !!document.fullscreenElement));
check('全屏 Ready 下难度区可见', await visible(page.locator('#difficulty')));
check('全屏 Ready 下最高分行可见', await visible(page.locator('#best-line')));
check('全屏 Ready 下开始按钮可见且可用', await page.evaluate(() => {
  const button = document.querySelector('#btn-start');
  if (!button || button.hidden || button.disabled) return false;
  const rect = button.getBoundingClientRect();
  return rect.width > 0 && rect.height > 0 && rect.bottom <= innerHeight;
}));
check('全屏 Ready 下页头隐藏（只留菜单 + 画面）', !(await shellVisible()).header);
await page.screenshot({ path: path.join(evidenceDir, `resp-${label}-fullscreen-gate-ready.png`) });

// ---------- Starting：全屏里开始 3-2-1 可见，倒计时结束进入 Playing ----------
await page.click('#btn-start');
await page.waitForTimeout(200);
const starting = await page.evaluate(() => ({
  page: document.body.dataset.page,
  countdownHidden: document.querySelector('#countdown')?.hidden,
  countdownValue: document.querySelector('#countdown-value')?.textContent,
  fullscreen: !!document.fullscreenElement,
}));
check('全屏 Starting 保留 H5 3-2-1 覆盖层',
  starting.page === 'Starting' && starting.countdownHidden === false && starting.countdownValue === '3',
  JSON.stringify(starting));
await page.waitForFunction(
  () => (document.querySelector('#status-hint')?.textContent || '').includes('游戏中'),
  null,
  { timeout: 30000 },
);
await page.waitForTimeout(500);
check('全屏里点开始能进入 Playing', await page.evaluate(() => !!document.fullscreenElement));

// ---------- Playing：0 网页按钮 + 外壳全隐藏 ----------
const fsButtons = await visibleButtonCount();
check('Playing 全屏内网页端可见按钮为 0', fsButtons === 0, `buttons=${fsButtons}`);
const fsShell = await shellVisible();
check('Playing 全屏内页头/难度/控制区/页脚/操作提示全部隐藏', allHidden(fsShell), JSON.stringify(fsShell));
await page.screenshot({ path: path.join(evidenceDir, `resp-${label}-fullscreen-gate-playing.png`) });

// ---------- Paused：Unity 暂停面板仍可用，网页端仍无按钮 ----------
await page.keyboard.press('p');
await page.waitForFunction(
  () => (document.querySelector('#status-hint')?.textContent || '').includes('已暂停'),
  null,
  { timeout: 10000 },
);
check('全屏内按 P 能暂停（Unity 暂停面板仍可用）', true);
check('Paused 全屏内仍无网页按钮', (await visibleButtonCount()) === 0);
check('Paused 全屏内仍无网页外壳', allHidden(await shellVisible()));
await page.screenshot({ path: path.join(evidenceDir, `resp-${label}-fullscreen-gate-paused.png`) });

await page.keyboard.press('p');
await page.waitForFunction(
  () => (document.querySelector('#status-hint')?.textContent || '').includes('游戏中'),
  null,
  { timeout: 20000 },
);
check('全屏内从暂停恢复（3-2-1 后回到游戏）', true);

// ---------- 退出：Esc 或原生 API；退出后页面恢复 ----------
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
// 退出全屏后游戏可能停在 Paused；真实浏览器通常由浏览器消费 Esc。按实际状态恢复后再等结算。
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

// ---------- Result：不提供全屏，结算卡可见 ----------
await page.waitForSelector('#result:not([hidden])', { timeout: 120000 });
await page.waitForTimeout(400);
check('Result 不提供全屏', !(await visible(fullscreenButton())));
check('Result 时结算卡可见', await visible(page.locator('#result')));

await browser.close();
console.log(failed === 0 ? '全部通过' : `${failed} 项失败`);
process.exit(failed === 0 ? 0 : 1);
