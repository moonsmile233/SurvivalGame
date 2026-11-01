// 全屏验收截图（M4.5 §1）：进入全屏 → Playing / Paused → 退出全屏。
// 用法：BASE_URL=http://127.0.0.1:8000 VIEWPORT=390x844 node fullscreen-shot.mjs
// 失败降级：FALLBACK=1 时把 requestFullscreen 替换成 reject，验证提示文案。
import { chromium } from 'playwright';
import { chromiumLaunchOptions } from './launch.mjs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const evidenceDir = path.resolve(here, '..', '..', 'docs', 'evidence');

const BASE_URL = (process.env.BASE_URL || 'http://127.0.0.1:8000').replace(/\/$/, '');
const [width, height] = (process.env.VIEWPORT || '390x844').split('x').map(Number);
const label = process.env.LABEL || `${width}x${height}`;
const FALLBACK = process.env.FALLBACK === '1';

const browser = await chromium.launch(chromiumLaunchOptions());
const context = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: 1 });
const page = await context.newPage();
const consoleErrors = [];
page.on('console', (message) => {
  if (message.type() === 'error') consoleErrors.push(message.text());
});

await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
await page.waitForSelector('#btn-start:not([disabled])', { timeout: 300000 });
await page.fill('#nickname', '全屏');
await page.click('#btn-start');
await page.waitForFunction(
  () => (document.querySelector('#status-hint')?.textContent || '').includes('游戏中'),
  null,
  { timeout: 30000 },
);

if (FALLBACK) {
  // 模拟 iOS Safari：requestFullscreen 存在但 reject。
  await page.evaluate(() => {
    document.documentElement.requestFullscreen = () => Promise.reject(new Error('not supported'));
  });
  await page.click('#btn-fullscreen');
  await page.waitForSelector('#fullscreen-hint:not([hidden])', { timeout: 5000 });
  console.log('hint', await page.locator('#fullscreen-hint').textContent());
  await page.screenshot({ path: path.join(evidenceDir, `resp-${label}-fullscreen-unsupported.png`) });
} else {
  await page.click('#btn-fullscreen');
  await page.waitForFunction(() => !!document.fullscreenElement, null, { timeout: 8000 });
  await page.waitForTimeout(500);

  const state = await page.evaluate(() => {
    const frame = document.querySelector('.game-frame').getBoundingClientRect();
    return {
      fullscreen: !!document.fullscreenElement,
      headerDisplay: getComputedStyle(document.querySelector('.header')).display,
      exitVisible: !document.querySelector('#btn-exit-fullscreen').hidden,
      frame: { w: Math.round(frame.width), h: Math.round(frame.height) },
      inner: { w: innerWidth, h: innerHeight },
    };
  });
  console.log('fullscreen', JSON.stringify(state));
  await page.screenshot({ path: path.join(evidenceDir, `resp-${label}-fullscreen-playing.png`) });

  await page.keyboard.press('p');
  await page.waitForFunction(
    () => (document.querySelector('#status-hint')?.textContent || '').includes('已暂停'),
    null,
    { timeout: 10000 },
  );
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.join(evidenceDir, `resp-${label}-fullscreen-paused.png`) });

  await page.click('#btn-exit-fullscreen');
  await page.waitForFunction(() => !document.fullscreenElement, null, { timeout: 8000 });
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(evidenceDir, `resp-${label}-fullscreen-exited.png`) });
}

console.log('consoleErrors', JSON.stringify(consoleErrors));
await browser.close();
