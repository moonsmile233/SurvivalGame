// 响应式矩阵截图：Playing / Paused / Result 三态，一次一个视口。
// 用法：BASE_URL=http://127.0.0.1:8000 VIEWPORT=390x844 node responsive-shot.mjs
// 证据：docs/evidence/resp-<label>-playing|paused|result.png
import { chromium } from 'playwright';
import { chromiumLaunchOptions } from './launch.mjs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const evidenceDir = path.resolve(here, '..', '..', 'docs', 'evidence');

const BASE_URL = (process.env.BASE_URL || 'http://127.0.0.1:8000').replace(/\/$/, '');
const [width, height] = (process.env.VIEWPORT || '390x844').split('x').map(Number);
const label = process.env.LABEL || `${width}x${height}`;
const E2E_PROXY = (process.env.E2E_PROXY || '').trim();

const browser = await chromium.launch(
  chromiumLaunchOptions({
    ...(E2E_PROXY ? { proxy: { server: E2E_PROXY } } : {}),
  }),
);
const context = await browser.newContext({
  viewport: { width, height },
  deviceScaleFactor: 1,
});
const page = await context.newPage();
const shot = (state) => path.join(evidenceDir, `resp-${label}-${state}.png`);

const hint = () => page.locator('#status-hint').textContent().catch(() => '');
async function waitForHint(text, timeout) {
  await page.waitForFunction(
    (t) => (document.querySelector('#status-hint')?.textContent || '').includes(t),
    text,
    { timeout },
  );
}

await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
await page.waitForSelector('#btn-start:not([disabled])', { timeout: 300000 });
await page.fill('#nickname', '响应式');
await page.click('#btn-start');
await waitForHint('游戏中', 30000);
await page.waitForTimeout(400);

const layout = await page.evaluate(() => ({
  innerW: window.innerWidth,
  innerH: window.innerHeight,
  scrollW: document.documentElement.scrollWidth,
  scrollH: document.documentElement.scrollHeight,
  canvas: (() => {
    const r = document.querySelector('#unity-canvas')?.getBoundingClientRect();
    return r ? { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height) } : null;
  })(),
}));
console.log('layout', JSON.stringify(layout));
await page.screenshot({ path: shot('playing') });

// 暂停：先截面板；再按一次 P 主动恢复，截 3-2-1（M4.6：失焦/暂停后不会自动恢复）
await page.keyboard.press('p');
await waitForHint('已暂停', 10000);
await page.waitForTimeout(400);
await page.screenshot({ path: shot('paused') });

await page.keyboard.press('p');
await page.waitForTimeout(600);
await page.screenshot({ path: shot('countdown') });
await waitForHint('游戏中', 20000);

// 结算：不操作，等自然结束（最长 60 秒）
await page.waitForSelector('#result:not([hidden])', { timeout: 120000 });
// scrollIntoView 是 smooth；等滚动真正停下再测量/截图，否则会截到动画中间、坐标互相打架。
await page.waitForFunction(
  () => new Promise((resolve) => {
    let last = window.scrollY;
    let stable = 0;
    const tick = () => {
      const y = window.scrollY;
      stable = y === last ? stable + 1 : 0;
      last = y;
      if (stable >= 6) resolve(true);
      else requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  }),
  null,
  { timeout: 8000 },
).catch(() => {});
await page.waitForTimeout(200);
const diag = await page.evaluate(() => ({
  scrollY: window.scrollY,
  docH: document.documentElement.scrollHeight,
  bodyH: document.body.scrollHeight,
  innerH: window.innerHeight,
  htmlOverflow: getComputedStyle(document.documentElement).overflow,
  bodyOverflow: getComputedStyle(document.body).overflow,
  resultTop: document.querySelector('#result')?.getBoundingClientRect().top,
  resultBottom: document.querySelector('#result')?.getBoundingClientRect().bottom,
}));
console.log('diag', JSON.stringify(diag));
const resultBox = await page.locator('#result').boundingBox();
const restartBox = await page.locator('#btn-restart').boundingBox();
console.log('result', JSON.stringify({ resultBox, restartBox }));
await page.screenshot({ path: shot('result') });

await browser.close();
console.log(`saved resp-${label}-{playing,paused,result}.png`);
