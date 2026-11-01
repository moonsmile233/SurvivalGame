// 全屏按钮的状态门禁（M4.5 §1 策划验收 P2）：
// Ready / Result 不允许进全屏（否则 .controls 或 .result 被 CSS 隐藏，玩家卡在静止画面），
// Playing / Paused 才显示「全屏」。一次跑完，截图写 docs/evidence/resp-<label>-fullscreen-gate-*.png。
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

await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
await page.waitForSelector('#btn-start:not([disabled])', { timeout: 300000 });

// Ready：M6 §2 起允许全屏，且全屏里必须还能选难度 + 点开始（CSS 按 body[data-page] 保留 .controls）
check('Ready 时全屏按钮可见', await visible(fullscreenButton()));
await page.click('#btn-fullscreen');
await page.waitForTimeout(500);
check('Ready 能进入全屏', await page.evaluate(() => !!document.fullscreenElement));
check('全屏 Ready 下开始按钮仍可见', await visible(page.locator('#btn-start')));
check('全屏 Ready 下难度区仍可见', await visible(page.locator('#difficulty')));
await page.screenshot({ path: path.join(evidenceDir, `resp-${label}-fullscreen-gate-ready.png`) });
await page.click('#btn-exit-fullscreen');
await page.waitForTimeout(300);

await page.fill('#nickname', '全屏门禁');
await page.click('#btn-start');
await page.waitForFunction(
  () => (document.querySelector('#status-hint')?.textContent || '').includes('游戏中'),
  null,
  { timeout: 30000 },
);
check('Playing 时全屏按钮可见', await visible(fullscreenButton()));

await page.keyboard.press('p');
await page.waitForFunction(
  () => (document.querySelector('#status-hint')?.textContent || '').includes('已暂停'),
  null,
  { timeout: 10000 },
);
check('Paused 时全屏按钮可见', await visible(fullscreenButton()));

await page.keyboard.press('p');
await page.waitForFunction(
  () => (document.querySelector('#status-hint')?.textContent || '').includes('游戏中'),
  null,
  { timeout: 20000 },
);

// 结算：不操作，等自然结束（最长 60 秒）
await page.waitForSelector('#result:not([hidden])', { timeout: 120000 });
await page.waitForTimeout(400);
check('Result 时全屏按钮隐藏', !(await visible(fullscreenButton())));
check('Result 时结算卡可见', await visible(page.locator('#result')));
await page.screenshot({ path: path.join(evidenceDir, `resp-${label}-fullscreen-gate-result.png`) });

await browser.close();
console.log(failed === 0 ? '全部通过' : `${failed} 项失败`);
process.exit(failed === 0 ? 0 : 1);
