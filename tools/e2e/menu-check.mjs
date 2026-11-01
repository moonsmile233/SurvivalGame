// M6 §2 H5 主界面检查：6 视口 Ready 布局 + 选档记忆 + 旧最高分迁移 + 跨档不串分。
// L1 起 Ready 不提供全屏（全屏只属于 Playing/Paused），这里的 Ready 断言按新口径检查。
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
  // 纵向滚动是允许的（菜单常驻后页面会变高），只记录高度用于报告。
  check(!!m.difficulty && m.difficulty.h > 0, `${label} 难度区可见`);
  check(m.cards.length === 3, `${label} 三张难度卡都在（${m.cards.length}）`);
  check(m.cards.every((card) => card.h + 0.5 >= minCard), `${label} 难度卡命中区 ≥${minCard}px（${m.cards.map((c) => Math.round(c.h)).join('/')}）`);
  check(!!m.start && m.start.bottom <= m.innerH + 0.5 && m.start.y >= 0, `${label} 开始按钮在首屏内（bottom ${Math.round(m.start.bottom)} ≤ ${m.innerH}）`);
  check(m.difficulty.bottom <= m.controls.y + 0.5, `${label} 难度区在操作区之上`);
  check(m.controls.bottom <= m.frame.y + 0.5, `${label} 开始/全屏在画布上方`);
  check(!m.fullscreenButtonVisible, `${label} Ready 不显示全屏按钮（全屏只属于 Playing/Paused）`);
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

// Playing 态：菜单隐藏、画面恢复全尺寸后是否溢出；Unity 绘制缓冲是否与 CSS 尺寸匹配（放大=糊）。
async function checkPlaying(context, width, height) {
  const label = `${width}x${height}`;
  const page = await context.newPage();
  attachConsole(page, `playing-${label}`);
  await page.setViewportSize({ width, height });
  await waitReady(page);

  const ready = await measure(page);

  await page.fill('#nickname', '布局');
  await page.click('#btn-start');
  await page.waitForTimeout(150);
  const countdownState = await page.evaluate(() => ({
    hidden: document.querySelector('#countdown')?.hidden,
    value: document.querySelector('#countdown-value')?.textContent,
    page: document.body.dataset.page,
  }));
  check(
    countdownState.hidden === false && countdownState.value === '3' && countdownState.page === 'Starting',
    `${label}（Playing）开始有 3-2-1 倒计时（${countdownState.value}/${countdownState.page}）`,
  );

  await page.waitForFunction(
    () => (document.querySelector('#status-hint')?.textContent || '').includes('游戏中'),
    null,
    { timeout: 60000 },
  );
  await page.waitForTimeout(800);

  const m = await page.evaluate(() => {
    const rect = (selector) => {
      const el = document.querySelector(selector);
      if (!el) return null;
      const r = el.getBoundingClientRect();
      return { x: r.x, y: r.y, w: r.width, h: r.height, bottom: r.bottom };
    };
    const canvas = document.querySelector('#unity-canvas');
    const dpr = window.devicePixelRatio || 1;
    return {
      innerW: window.innerWidth,
      innerH: window.innerHeight,
      scrollW: document.documentElement.scrollWidth,
      scrollH: document.documentElement.scrollHeight,
      difficultyHidden: document.querySelector('#difficulty')?.hidden,
      difficultyLocked: document.querySelector('#difficulty')?.classList.contains('is-locked'),
      cardsDisabled: Array.from(document.querySelectorAll('.difficulty-input')).every((el) => el.disabled),
      startHidden: document.querySelector('#btn-start')?.hidden,
      frame: rect('.game-frame'),
      // Unity 的绘制缓冲（像素）应与 CSS 尺寸 × DPR 一致，否则浏览器会把小缓冲放大（糊）
      canvasBuffer: canvas ? { w: canvas.width, h: canvas.height } : null,
      canvasCss: canvas
        ? { w: canvas.clientWidth, h: canvas.clientHeight, dpr, expectW: Math.round(canvas.clientWidth * dpr), expectH: Math.round(canvas.clientHeight * dpr) }
        : null,
    };
  });

  check(m.scrollW <= m.innerW + 1, `${label}（Playing）无横向溢出（${m.scrollW} ≤ ${m.innerW}）`);
  check(m.startHidden === true, `${label}（Playing）开始按钮隐藏`);

  // 菜单常驻：所有状态都显示难度区、卡片可点；画布尺寸与 Ready 相同（不再有局内放大）
  check(m.difficultyHidden === false, `${label}（Playing）难度区常驻可见`);
  check(m.cardsDisabled === false, `${label}（Playing）难度卡仍可点`);
  check(
    Math.abs(m.frame.w - ready.frame.w) <= 0.5 && Math.abs(m.frame.h - ready.frame.h) <= 0.5,
    `${label}（Playing）画布与 Ready 同尺寸（${Math.round(m.frame.w)}×${Math.round(m.frame.h)} vs ${Math.round(ready.frame.w)}×${Math.round(ready.frame.h)}）`,
  );

  // 画布保持完整尺寸；页面允许纵向滚动，所以不要求 frame 整个在首屏内。
  const touchAction = await page.evaluate(
    () => getComputedStyle(document.querySelector('#unity-canvas')).touchAction,
  );
  check(touchAction === 'none', `${label}（Playing）画布 touch-action:none（拖动摇杆不滚页）`);

  if (m.scrollH > m.innerH + 1) {
    // 滚轮两条路径都要能滚页（M6 §1.3）：
    // - 画布上：Unity 默认 preventDefault，app.js 的 onCanvasWheel 自己 scrollBy；
    // - 画布外：走浏览器原生滚动。
    // 同时校验「不双重滚动」：单次 40px 若被浏览器默认+我们的 scrollBy 叠加会变成 80px。
    // 每次测前先回顶，避免点击开始按钮留下的滚动量造成假通过（320 之前就是这样暴露的）。
    const maxScroll = m.scrollH - m.innerH;
    const step = 40;
    const wanted = Math.min(step, maxScroll);
    const resetScroll = async () => {
      await page.evaluate(() => window.scrollTo(0, 0));
      await page.waitForTimeout(100);
    };

    const centerX = Math.round(Math.min(Math.max(m.frame.x + m.frame.w / 2, 20), m.innerW - 20));
    const centerY = Math.round(Math.min(Math.max(m.frame.y + 40, 20), m.innerH - 20));
    await resetScroll();
    await page.mouse.move(centerX, centerY);
    await page.mouse.wheel(0, step);
    await page.waitForTimeout(300);
    const canvasScrolled = await page.evaluate(() => window.scrollY);
    check(
      canvasScrolled >= Math.min(10, wanted) && canvasScrolled <= wanted + 8,
      `${label}（Playing）画布上滚轮能滚页且不双重（scrollY ${Math.round(canvasScrolled)}，期望 ${wanted}）`,
    );

    await resetScroll();
    await page.mouse.move(Math.round(m.innerW / 2), 6);
    await page.mouse.wheel(0, step);
    await page.waitForTimeout(300);
    const outsideScrolled = await page.evaluate(() => window.scrollY);
    check(
      outsideScrolled > 0,
      `${label}（Playing）画布外滚轮能滚动页面（scrollY ${Math.round(outsideScrolled)}）`,
    );

    await resetScroll();
  }

  const buf = m.canvasBuffer;
  const css = m.canvasCss;
  const ratioOk = !!buf && !!css && Math.abs(buf.w - css.expectW) <= 2 && Math.abs(buf.h - css.expectH) <= 2;
  check(
    ratioOk,
    `${label}（Playing）绘制缓冲匹配 CSS×DPR（buffer ${buf?.w}×${buf?.h} vs expect ${css?.expectW}×${css?.expectH}, dpr ${css?.dpr}）`,
  );

  await page.screenshot({ path: evidence(`m6-h5-playing-${label}.png`) });
  console.log(`      截图 → docs/evidence/m6-h5-playing-${label}.png`);

  // Result：无论视口多高都不显示菜单（结算卡优先，避免溢出）
  await page.evaluate(() => {
    window.SurvivalApp.onStateChange(JSON.stringify({ protocol: 2, state: 'Playing' }));
    window.SurvivalApp.onGameOver(JSON.stringify({
      protocol: 2,
      event: 'gameOver',
      payload: {
        result: 'lose', score: 100, survivedSeconds: 10, kills: 0, nickname: '布局',
        difficulty: 'normal', breakdown: { survival: 100, kills: 0, winBonus: 0 },
      },
    }));
  });
  await page.waitForTimeout(700);
  const resultState = await page.evaluate(() => {
    const el = document.querySelector('#result');
    const r = el ? el.getBoundingClientRect() : null;
    return {
      menuHidden: document.querySelector('#difficulty')?.hidden,
      resultVisible: !!el && !el.hidden && !!r && r.height > 0,
      resultBottom: r ? r.bottom : null,
      innerH: window.innerHeight,
      cardsDisabled: Array.from(document.querySelectorAll('.difficulty-input')).some((input) => input.disabled),
    };
  });
  check(resultState.menuHidden === false && resultState.cardsDisabled === false, `${label}（Result）难度区常驻且可点`);
  check(resultState.resultVisible === true, `${label}（Result）结算卡可见`);
  check(
    resultState.resultBottom !== null && resultState.resultBottom <= resultState.innerH + 2,
    `${label}（Result）结算卡能滚到可见（bottom ${Math.round(resultState.resultBottom)} ≤ ${resultState.innerH}）`,
  );

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

  // ONLY=ready|storage|playing 只跑其中一段（调布局时省时间）；不传则全跑。
  const only = (process.env.ONLY || '').trim();
  const want = (section) => !only || only === section;

  if (want('ready')) {
    console.log('\n[1/3] Ready 布局（6 视口）');
    for (const [w, h] of [[320, 568], [390, 844], [844, 390]]) {
      await checkLayout(mobile, w, h);
    }
    for (const [w, h] of [[768, 1024], [1280, 720], [1920, 1080]]) {
      await checkLayout(desktop, w, h);
    }
  }

  if (want('storage')) {
    console.log('\n[2/3] 选档 / 迁移 / 跨档最高分');
    await checkStorageFlow(mobile, 390, 844);
  }

  if (want('playing')) {
    console.log('\n[3/3] Playing 布局与绘制缓冲');
    for (const [w, h] of [[320, 568], [844, 390]]) {
      await checkPlaying(mobile, w, h);
    }
    for (const [w, h] of [[390, 844], [1280, 720], [768, 1024]]) {
      await checkPlaying(w === 390 ? mobile : desktop, w, h);
    }
  }

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
