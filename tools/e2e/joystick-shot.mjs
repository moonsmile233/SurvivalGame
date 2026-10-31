// M4.7 浮动摇杆截图：一次只跑一个视口，用于 docs/evidence/m47-joystick-*.png。
// 用法（先起本地服务，例如 cd web && python -m http.server 8125）：
//   BASE_URL=http://127.0.0.1:8125 VIEWPORT=390x844 node joystick-shot.mjs
//   BASE_URL=http://127.0.0.1:8125 VIEWPORT=844x390 node joystick-shot.mjs
//   BASE_URL=http://127.0.0.1:8125 MOUSE=1 node joystick-shot.mjs   # 桌面鼠标：应看不到摇杆
// 触摸用 CDP 真实事件；截图时手指保持按住，松手/暂停各截一张。
import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const evidenceDir = path.resolve(here, '..', '..', 'docs', 'evidence');

const BASE_URL = (process.env.BASE_URL || 'http://127.0.0.1:8125').replace(/\/$/, '');
const [width, height] = (process.env.VIEWPORT || '390x844').split('x').map(Number);
const MOUSE = process.env.MOUSE === '1';
const label = process.env.LABEL || (MOUSE ? 'desktop' : width > height ? 'landscape' : 'mobile');
const E2E_PROXY = (process.env.E2E_PROXY || '').trim();

const browser = await chromium.launch({
  channel: 'chrome',
  headless: false,
  ...(E2E_PROXY ? { proxy: { server: E2E_PROXY } } : {}),
});
const context = await browser.newContext({
  viewport: { width, height },
  deviceScaleFactor: MOUSE ? 1 : 2,
  hasTouch: !MOUSE,
  isMobile: !MOUSE,
});
const page = await context.newPage();

await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
await page.waitForSelector('#btn-start:not([disabled])', { timeout: 300000 });
await page.fill('#nickname', '摇杆测试');
await page.click('#btn-start');
await page.waitForFunction(
  () => (document.querySelector('#status-hint')?.textContent || '').includes('游戏中'),
  null,
  { timeout: 30000 },
);
await page.waitForTimeout(300);

const box = await page.locator('#unity-canvas').boundingBox();
if (!box) {
  throw new Error('找不到 #unity-canvas');
}

const cx = Math.round(box.x + box.width / 2);
const cy = Math.round(box.y + box.height / 2);
const dx = Math.round(box.width * 0.12);

if (MOUSE) {
  await page.mouse.move(cx, cy);
  await page.mouse.down();
  await page.mouse.move(cx + dx, cy, { steps: 6 });
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.join(evidenceDir, 'm47-joystick-desktop.png') });
  await page.mouse.up();
} else {
  const client = await context.newCDPSession(page);

  await client.send('Input.dispatchTouchEvent', {
    type: 'touchStart',
    touchPoints: [{ x: cx, y: cy, id: 1 }],
  });
  await page.waitForTimeout(100);
  await client.send('Input.dispatchTouchEvent', {
    type: 'touchMove',
    touchPoints: [{ x: cx + dx, y: cy, id: 1 }],
  });
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.join(evidenceDir, `m47-joystick-${label}.png`) });

  // 松手：Playing 状态下摇杆应立即消失。
  await client.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(evidenceDir, `m47-joystick-${label}-released.png`) });

  // 再次按住并暂停：摇杆也应立即隐藏（状态不是 Playing）。
  await client.send('Input.dispatchTouchEvent', {
    type: 'touchStart',
    touchPoints: [{ x: cx, y: cy, id: 2 }],
  });
  await client.send('Input.dispatchTouchEvent', {
    type: 'touchMove',
    touchPoints: [{ x: cx + dx, y: cy, id: 2 }],
  });
  await page.waitForTimeout(200);
  await page.keyboard.press('p');
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.join(evidenceDir, `m47-joystick-${label}-paused.png`) });

  await client.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
}

console.log(`saved m47-joystick-${label}*.png (${width}x${height}, ${MOUSE ? 'mouse' : 'touch'})`);
await browser.close();
