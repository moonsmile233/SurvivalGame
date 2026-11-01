// M6 §2 H5 主界面检查：6 视口 Ready 布局 + 选档记忆 + 旧最高分迁移 + 跨档不串分 + Ready 全屏可开始。
// 这一轮对着仓库里的 1.0.0 构建跑（旧 Unity 忽略 difficulty → normal 是预期），
// 只验 H5 自身；难度真正生效由 ② 的构建验收。
//
// 用法：node menu-check.mjs（本地起静态服务，默认端口 8124）
// 证据：docs/evidence/m6-h5-ready-<WxH>.png / m6-h5-*.png / m6-h5-layout.json
import { chromium } from 'playwright';
import { chromiumLaunchOptions } from './launch.mjs';
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');
const webRoot = path.join(repoRoot, 'web');
const evidenceDir = path.join(repoRoot, 'docs', 'evidence');
const PORT = Number(process.env.PORT || 8124);
const BASE_URL = (process.env.BASE_URL || `http://127.0.0.1:${PORT}`).replace(/\/$/, '');
const READY_TIMEOUT = Number(process.env.READY_TIMEOUT || 180000);

const failures = [];
const consoleErrors = [];
const layoutReport = [];

function check(ok, message) {
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${message}`);
  if (!ok) failures.push(message);
}

const evidence = (name) => {
  fs.mkdirSync(evidenceDir, { recursive: true });
  return path.join(evidenceDir, name);
};

// ---------- 本地静态服务（与 smoke 同一套 MIME，no-store） ----------

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.otf': 'font/otf',
  '.wasm': 'application/wasm',
  '.unityweb': 'application/octet-stream',
};

function startServer() {
  const server = http.createServer((request, response) => {
    let pathname = '/';
    try {
      pathname = decodeURIComponent(new URL(request.url, 'http://127.0.0.1').pathname);
    } catch {
      response.writeHead(400).end('bad request');
      return;
    }

    const relative = pathname === '/' ? 'index.html' : pathname.replace(/^\/+/, '');
    const file = path.resolve(webRoot, relative);
    if (file !== webRoot && !file.startsWith(webRoot + path.sep)) {
      response.writeHead(403).end('forbidden');
      return;
    }

    fs.readFile(file, (error, data) => {
      if (error) {
        response.writeHead(404).end('not found');
        return;
      }
      response.writeHead(200, {
        'Content-Type': MIME[path.extname(file).toLowerCase()] || 'application/octet-stream',
        'Cache-Control': 'no-store',
      });
      response.end(data);
    });
  });

  return new Promise((resolve) => server.listen(PORT, '127.0.0.1', () => resolve(server)));
}

// ---------- 页面检查 ----------

function attachConsole(page, tag) {
  page.on('console', (message) => {
    if (message.type() === 'error') {
      consoleErrors.push(`[${tag}] ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => consoleErrors.push(`[${tag}] ${error}`));
}

async function waitReady(page) {
  await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('#btn-start:not([disabled])', { timeout: READY_TIMEOUT });
  await page.waitForTimeout(300);
}

async function measure(page) {
  return page.evaluate(() => {
    const rect = (selector) => {
      const el = document.querySelector(selector);
      if (!el) return null;
      const r = el.getBoundingClientRect();
      return { x: r.x, y: r.y, w: r.width, h: r.height, bottom: r.bottom, right: r.right };
    };

    return {
      innerW: window.innerWidth,
      innerH: window.innerHeight,
      scrollW: document.documentElement.scrollWidth,
      scrollH: document.documentElement.scrollHeight,
      page: document.body.dataset.page,
      tagline: rect('.tagline'),
      difficulty: rect('#difficulty'),
      cards: Array.from(document.querySelectorAll('.difficulty-card')).map((el) => {
        const r = el.getBoundingClientRect();
        return { w: r.width, h: r.height };
      }),
      bestLine: rect('#best-line'),
      frame: rect('.game-frame'),
      controls: rect('.controls'),
      start: rect('#btn-start'),
      fullscreenButtonVisible: (() => {
        const el = document.querySelector('#btn-fullscreen');
        return !!el && !el.hidden && el.getBoundingClientRect().width > 0;
      })(),
    };
  });
}

async function checkLayout(context, width, height) {
  const label = `${width}x${height}`;
  const page = await context.newPage();
  attachConsole(page, `layout-${label}`);
  await page.setViewportSize({ width, height });
  await waitReady(page);

  const m = await measure(page);
  const shortLandscape = height <= 560 && width > height;
  const minCard = shortLandscape ? 36 : 44;

  check(m.scrollW <= m.innerW + 1, `${label} 无横向溢出（scrollW ${m.scrollW} ≤ innerW ${m.innerW}）`);
  check(m.scrollH <= m.innerH + 1, `${label} 无纵向溢出（scrollH ${m.scrollH} ≤ innerH ${m.innerH}）`);
  check(!!m.difficulty && m.difficulty.h > 0, `${label} 难度区可见`);
  check(m.cards.length === 3, `${label} 三张难度卡都在（${m.cards.length}）`);
  check(m.cards.every((card) => card.h + 0.5 >= minCard), `${label} 难度卡命中区 ≥${minCard}px（${m.cards.map((c) => Math.round(c.h)).join('/')}）`);
  check(!!m.start && m.start.bottom <= m.innerH + 0.5 && m.start.y >= 0, `${label} 开始按钮在首屏内（bottom ${Math.round(m.start.bottom)} ≤ ${m.innerH}）`);
  check(m.difficulty.bottom <= m.frame.y + 0.5, `${label} 难度区不与画面重叠`);
  check(m.frame.bottom <= m.controls.y + 0.5, `${label} 画面不与操作区重叠`);
  check(m.fullscreenButtonVisible, `${label} Ready 下全屏按钮可见`);
  check(m.page === 'Ready', `${label} 页面状态 body[data-page]=Ready`);

  await page.screenshot({ path: evidence(`m6-h5-ready-${label}.png`) });
  layoutReport.push({ label, ...m });
  console.log(`      截图 → docs/evidence/m6-h5-ready-${label}.png`);
  await page.close();
}

async function checkStorageFlow(context, width, height) {
  const page = await context.newPage();
  attachConsole(page, 'storage');
  await page.setViewportSize({ width, height });
  await page.goto(BASE_URL, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('#btn-start:not([disabled])', { timeout: READY_TIMEOUT });

  // 1) 旧版单一最高分 → 普通档，迁一次后删旧键
  await page.evaluate(() => {
    window.localStorage.clear();
    window.localStorage.setItem('survival.bestScore', '500');
  });
  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.waitForSelector('#btn-start:not([disabled])', { timeout: READY_TIMEOUT });
  await page.waitForTimeout(200);

  const migrated = await page.evaluate(() => ({
    legacy: window.localStorage.getItem('survival.bestScore'),
    normal: window.localStorage.getItem('survival.bestScore.normal'),
    label: document.querySelector('#best-label')?.textContent,
    value: document.querySelector('#best-value')?.textContent,
  }));
  check(migrated.normal === '500', `旧最高分迁到普通档（normal=${migrated.normal}）`);
  check(migrated.legacy === null, `迁移成功后旧键已删除（legacy=${migrated.legacy}）`);
  check(migrated.label === '普通' && migrated.value === '500', `最高分行显示普通档 500（${migrated.label} ${migrated.value}）`);

  // 再刷一次不应重复迁移、也不应改动 normal
  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.waitForSelector('#btn-start:not([disabled])', { timeout: READY_TIMEOUT });
  const secondLoad = await page.evaluate(() => window.localStorage.getItem('survival.bestScore.normal'));
  check(secondLoad === '500', `二次加载不重复迁移（normal=${secondLoad}）`);

  // 2) 选档 → 立即刷新最高分行 + 记住选择 → 刷新后仍在
  await page.locator('.difficulty-card').nth(2).click();
  await page.waitForTimeout(150);
  const selected = await page.evaluate(() => ({
    stored: window.localStorage.getItem('survival.difficulty'),
    checked: document.querySelector('input[value="hard"]')?.checked,
    label: document.querySelector('#best-label')?.textContent,
    value: document.querySelector('#best-value')?.textContent,
    selectedClass: document.querySelectorAll('.difficulty-card.is-selected').length,
  }));
  check(selected.stored === 'hard', `选择困难后写入 localStorage（${selected.stored}）`);
  check(selected.checked === true && selected.selectedClass === 1, '困难卡为选中态（radio + 样式）');
  check(selected.label === '困难' && selected.value === '0', `最高分行切到困难档 0（${selected.label} ${selected.value}）`);

  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.waitForSelector('#btn-start:not([disabled])', { timeout: READY_TIMEOUT });
  const persisted = await page.evaluate(() => ({
    checked: document.querySelector('input[value="hard"]')?.checked,
    label: document.querySelector('#best-label')?.textContent,
  }));
  check(persisted.checked === true, '刷新后仍选中困难');
  check(persisted.label === '困难', `刷新后最高分行仍是困难（${persisted.label}）`);

  await page.screenshot({ path: evidence('m6-h5-difficulty-hard.png') });
  console.log('      截图 → docs/evidence/m6-h5-difficulty-hard.png');

  // 3) 跨档不串分：直接喂 onGameOver 载荷（H5 层验证，不依赖 Unity 难度是否生效）
  const sendGameOver = (difficulty, score) => page.evaluate(({ difficulty: d, score: s }) => {
    window.SurvivalApp.onStateChange(JSON.stringify({ protocol: 2, state: 'Playing' }));
    window.SurvivalApp.onGameOver(JSON.stringify({
      protocol: 2,
      event: 'gameOver',
      payload: {
        result: 'win',
        score: s,
        survivedSeconds: 60,
        kills: 0,
        nickname: '迁移',
        difficulty: d,
        breakdown: { survival: Math.max(0, s - 200), kills: 0, winBonus: 200 },
      },
    }));
  }, { difficulty, score });

  await sendGameOver('hard', 1160);
  const afterHard = await page.evaluate(() => ({
    hard: window.localStorage.getItem('survival.bestScore.hard'),
    normal: window.localStorage.getItem('survival.bestScore.normal'),
    easy: window.localStorage.getItem('survival.bestScore.easy'),
    diff: document.querySelector('#result-difficulty')?.textContent,
    best: document.querySelector('#result-best')?.textContent,
    title: document.querySelector('#result-title')?.textContent,
  }));
  check(afterHard.hard === '1160', `困难最高分单独写入（${afterHard.hard}）`);
  check(afterHard.normal === '500' && afterHard.easy === null, `困难局不影响普通/简单（normal=${afterHard.normal}, easy=${afterHard.easy}）`);
  check(afterHard.diff === '困难' && afterHard.best === '1160', `结算卡显示难度与同档最高分（${afterHard.diff} / ${afterHard.best}）`);
  check(afterHard.title === '胜利', `结算标题正确（${afterHard.title}）`);

  await sendGameOver('easy', 800);
  const afterEasy = await page.evaluate(() => ({
    hard: window.localStorage.getItem('survival.bestScore.hard'),
    normal: window.localStorage.getItem('survival.bestScore.normal'),
    easy: window.localStorage.getItem('survival.bestScore.easy'),
    diff: document.querySelector('#result-difficulty')?.textContent,
    best: document.querySelector('#result-best')?.textContent,
  }));
  check(afterEasy.easy === '800' && afterEasy.hard === '1160' && afterEasy.normal === '500', '简单局只写简单档（800/1160/500）');
  check(afterEasy.diff === '简单' && afterEasy.best === '800', `简单局结算卡正确（${afterEasy.diff} / ${afterEasy.best}）`);

  await page.screenshot({ path: evidence('m6-h5-result-difficulty.png') });
  console.log('      截图 → docs/evidence/m6-h5-result-difficulty.png');
  await page.close();
}

async function checkReadyFullscreen(context, width, height) {
  const page = await context.newPage();
  attachConsole(page, 'fullscreen');
  await page.setViewportSize({ width, height });
  await waitReady(page);

  await page.click('#btn-fullscreen');
  await page.waitForTimeout(500);

  const state = await page.evaluate(() => {
    const visible = (selector) => {
      const el = document.querySelector(selector);
      if (!el || el.hidden) return false;
      const r = el.getBoundingClientRect();
      return r.width > 0 && r.height > 0;
    };
    return {
      fullscreen: !!document.fullscreenElement,
      start: visible('#btn-start'),
      difficulty: visible('#difficulty'),
      exit: visible('#btn-exit-fullscreen'),
      startBottom: document.querySelector('#btn-start')?.getBoundingClientRect().bottom,
      innerH: window.innerHeight,
    };
  });

  check(state.fullscreen, 'Ready 能进入全屏');
  check(state.difficulty, '全屏 Ready 下难度区可见');
  check(state.start, '全屏 Ready 下开始按钮可见');
  check(state.exit, '全屏退出按钮可见');
  check(state.startBottom <= state.innerH + 0.5, `全屏 Ready 下开始按钮在视口内（${Math.round(state.startBottom)} ≤ ${state.innerH}）`);

  await page.screenshot({ path: evidence(`m6-h5-ready-fullscreen-${width}x${height}.png`) });
  console.log(`      截图 → docs/evidence/m6-h5-ready-fullscreen-${width}x${height}.png`);

  await page.click('#btn-exit-fullscreen');
  await page.waitForTimeout(300);
  check(!(await page.evaluate(() => !!document.fullscreenElement)), '能退出全屏');
  await page.close();
}

// ---------- 主流程 ----------

const server = await startServer();
const browser = await chromium.launch(chromiumLaunchOptions());

try {
  const desktop = await browser.newContext({ viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1 });
  const mobile = await browser.newContext({
    viewport: { width: 390, height: 844 },
    deviceScaleFactor: 1,
    isMobile: true,
    hasTouch: true,
  });

  console.log('\n[1/3] Ready 布局（6 视口）');
  for (const [w, h] of [[320, 568], [390, 844], [844, 390]]) {
    await checkLayout(mobile, w, h);
  }
  for (const [w, h] of [[768, 1024], [1280, 720], [1920, 1080]]) {
    await checkLayout(desktop, w, h);
  }

  console.log('\n[2/3] 选档 / 迁移 / 跨档最高分');
  await checkStorageFlow(mobile, 390, 844);

  console.log('\n[3/3] Ready 全屏');
  await checkReadyFullscreen(desktop, 1280, 720);

  const noise = /INVALID_ENUM|GL_INVALID_OPERATION|favicon|AudioContext|WebGL: CONTEXT_LOST/i;
  const realErrors = consoleErrors.filter((line) => !noise.test(line));
  if (consoleErrors.length > 0) {
    fs.writeFileSync(evidence('m6-h5-console.log'), consoleErrors.join('\n'));
  }
  check(realErrors.length === 0, `console 无未预期的 error（${realErrors.length}）`);

  fs.writeFileSync(evidence('m6-h5-layout.json'), JSON.stringify(layoutReport, null, 2));
  console.log('\n布局摘要 → docs/evidence/m6-h5-layout.json');
} finally {
  await browser.close();
  server.close();
}

console.log(failures.length === 0 ? '\n全部通过' : `\n${failures.length} 项失败`);
process.exit(failures.length === 0 ? 0 : 1);
