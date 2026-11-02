// v1.2 H5 thin-shell E2E (new web/index.html + existing Unity 1.2.0 build).
//
// Modes:
//   MODE=core       protocol, nickname, enter, data panel, difficulty pref, gameOver/best, low score,
//                   no legacy commands, refresh (default; also runs migration + fullscreen)
//   MODE=viewports  6 viewports: Shell / Entered screenshots + no horizontal scroll;
//                   390×844 plays a real 2s round (gameover-real), other viewports inject
//                   a layout-only GameOver (gameover-layout-only); mobile viewports use hasTouch.
//
// Unity buttons are clicked by fractional canvas coordinates (ui-layout.mjs); H5 assertions use
// localStorage + the ?testhook=1 state/message log exposed by web/js/app.js.
import { chromium } from 'playwright';
import {
  ensureEvidenceDir,
  saveJson,
  saveScreenshot,
  startServer,
  assert,
} from './standalone-common.mjs';
import { getLayout } from './ui-layout.mjs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');
const EVIDENCE = path.join(repoRoot, 'docs', 'evidence', 'm6-refactor');

const MODE = (process.env.MODE || 'core').toLowerCase();
const PORT = Number(process.env.PORT || 8132);
const HEADED = process.env.HEADED === '1';
const READY_TIMEOUT = Number(process.env.READY_TIMEOUT || 180000);
const GAME_TIMEOUT = Number(process.env.GAME_TIMEOUT || 300000);

const VIEWPORTS = [
  { name: '320x568', width: 320, height: 568 },
  { name: '390x844', width: 390, height: 844 },
  { name: '844x390', width: 844, height: 390 },
  { name: '768x1024', width: 768, height: 1024 },
  { name: '1280x720', width: 1280, height: 720 },
  { name: '1920x1080', width: 1920, height: 1080 },
];

function log(message) {
  console.log('[h5-shell] ' + message);
}

async function newPage(browser, viewport, url) {
  const context = await browser.newContext({ viewport, deviceScaleFactor: 1 });
  const page = await context.newPage();
  const consoleLines = [];
  page.on('console', (message) => consoleLines.push(message.text()));
  page.on('pageerror', (error) => consoleLines.push('[pageerror] ' + error.message));
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  return { context, page, consoleLines };
}

async function waitReady(page) {
  await page.waitForFunction(
    () => window.SurvivalApp && window.SurvivalApp.getState && window.SurvivalApp.getState().ready,
    null,
    { timeout: READY_TIMEOUT },
  );
}

async function appState(page) {
  return page.evaluate(() => window.SurvivalApp.getState());
}

async function sentMessages(page) {
  return page.evaluate(() => window.SurvivalApp.getSentMessages());
}

async function waitStatus(page, text, timeout = 30000) {
  await page.waitForFunction(
    (needle) => (document.getElementById('status-hint')?.textContent || '').includes(needle),
    text,
    { timeout },
  );
}

async function canvasLayout(page) {
  const rect = await page.evaluate(() => {
    const box = document.querySelector('#unity-canvas').getBoundingClientRect();
    return { width: box.width, height: box.height };
  });
  return getLayout(rect.width, rect.height);
}

async function clickCanvas(page, point) {
  const rect = await page.evaluate(() => {
    const box = document.querySelector('#unity-canvas').getBoundingClientRect();
    return { left: box.left, top: box.top, width: box.width, height: box.height };
  });
  await page.mouse.click(rect.left + point.fx * rect.width, rect.top + point.fy * rect.height);
}

async function text(page, selector) {
  return (await page.textContent(selector)) || '';
}

async function waitGameOverH5(page, timeout = GAME_TIMEOUT) {
  await page.waitForFunction(
    () => {
      const value = document.getElementById('data-last')?.textContent || '';
      return value.trim() !== '—' && window.SurvivalApp.getState().page === 'GameOver';
    },
    null,
    { timeout },
  );
}

async function runCore(browser, baseUrl) {
  const url = `${baseUrl}/index.html?testhook=1&perf=1&roundDuration=2`;
  const { context, page, consoleLines } = await newPage(browser, { width: 1280, height: 720 }, url);

  // 1) Start must be disabled before Unity onReady.
  const disabledAtLoad = await page.$eval('#btn-start', (el) => el.disabled);
  assert(disabledAtLoad, 'start must be disabled before ready');

  await waitReady(page);
  const shell = await appState(page);
  assert(shell.page === 'Shell', 'expected Shell after ready, got ' + shell.page);
  assert((await text(page, '#overlay-text')).includes('游戏已就绪'), 'overlay must say 游戏已就绪');
  assert(await page.$eval('#btn-fullscreen', (el) => el.hidden), 'fullscreen button must be hidden in Shell');
  assert(!(await page.$eval('#nickname', (el) => el.disabled)), 'nickname must be editable in Shell');
  await saveScreenshot(page, 'h5-shell-shell-1280.png');

  // 2) Nickname + enter. The input is the only nickname display (no data-panel duplicate).
  await page.fill('#nickname', '阿宝');
  assert((await page.locator('#data-nickname').count()) === 0, 'nickname must not be duplicated in the data panel');
  await page.click('#btn-start');
  await page.waitForTimeout(500);

  const entered = await appState(page);
  assert(entered.page === 'Entered', 'expected Entered, got ' + entered.page);
  assert(await page.$eval('#overlay', (el) => el.hidden), 'overlay must hide after EnterGame');
  assert(await page.$eval('#btn-start', (el) => el.hidden), 'start must hide after EnterGame');
  assert(!(await page.$eval('#btn-fullscreen', (el) => el.hidden)), 'fullscreen button must appear after enter');
  assert(await page.$eval('#nickname', (el) => el.disabled), 'nickname must be read-only after enter');
  assert(
    (await page.evaluate(() => localStorage.getItem('survival.nickname'))) === '阿宝',
    'nickname must be saved',
  );

  let sent = await sentMessages(page);
  let enterGames = sent.filter((message) => message.method === 'EnterGame');
  assert(enterGames.length === 1, 'exactly one EnterGame, got ' + enterGames.length);
  assert(enterGames[0].payload.includes('"lastDifficulty":"normal"'), 'EnterGame must carry lastDifficulty');
  assert(enterGames[0].payload.includes('"nickname":"阿宝"'), 'EnterGame must carry nickname');

  // Double click must not send another EnterGame.
  await page.evaluate(() => document.getElementById('btn-start').click());
  await page.evaluate(() => document.getElementById('btn-start').click());
  sent = await sentMessages(page);
  assert(sent.filter((m) => m.method === 'EnterGame').length === 1, 'start click must be idempotent');
  await saveScreenshot(page, 'h5-shell-entered-1280.png');

  // 3) Difficulty preference comes back from the Unity menu.
  let layout = await canvasLayout(page);
  await clickCanvas(page, layout.menuCards[2]); // hard
  await page.waitForFunction(
    () => localStorage.getItem('survival.difficulty') === 'hard',
    null,
    { timeout: 15000 },
  );

  // 4) Unity menu start -> playing; pause -> resume -> pause -> menu.
  layout = await canvasLayout(page);
  await clickCanvas(page, layout.menuStart);
  await waitStatus(page, '游戏中', 60000);
  await saveScreenshot(page, 'h5-shell-playing-1280.png');

  await page.keyboard.press('Escape');
  await waitStatus(page, '已暂停', 30000);
  await page.waitForTimeout(400);
  layout = await canvasLayout(page);
  await clickCanvas(page, layout.pauseResume);
  await waitStatus(page, '游戏中', 60000);

  await page.keyboard.press('Escape');
  await waitStatus(page, '已暂停', 30000);
  await page.waitForTimeout(400);
  layout = await canvasLayout(page);
  await clickCanvas(page, layout.pauseMenu);
  await waitStatus(page, '游戏菜单', 30000);

  // 5) Finish a real 2s round (hard): best score + last round.
  layout = await canvasLayout(page);
  await clickCanvas(page, layout.menuStart);
  await waitStatus(page, '游戏中', 60000);
  await waitGameOverH5(page);

  const best = Number(await page.evaluate(() => localStorage.getItem('survival.bestScore')));
  const migrated = await page.evaluate(() => localStorage.getItem('survival.bestScoreMigrated'));
  assert(best === 232, 'hard 2s win best must be 232, got ' + best);
  assert(migrated === '1', 'migration marker must be set');
  assert((await text(page, '#data-last')) === '困难 232', 'last round must be 困难 232');
  assert((await page.getAttribute('#data-last', 'title') || '').includes('胜利'), 'title must keep win info');
  // 快照必须在下面的注入/刷新之前抓：刷新会清空最近一局，JSON 里才不会被写成「—」。
  const lastRoundAfterRound = await text(page, '#data-last');
  await saveScreenshot(page, 'h5-shell-gameover-1280.png');

  // 6) Low score must not overwrite best (inject a lower result through the same handler).
  await page.evaluate(() => {
    window.SurvivalApp.onGameOver(JSON.stringify({
      protocol: 3,
      event: 'gameOver',
      payload: { result: 'lose', score: 100, survivedSeconds: 10, difficulty: 'easy' },
    }));
  });
  assert(Number(await page.evaluate(() => localStorage.getItem('survival.bestScore'))) === 232, 'low score overwrote best');
  assert((await text(page, '#data-last')).startsWith('简单 100'), 'last round should update even for a low score');

  // 7) No legacy in-game commands were sent.
  sent = await sentMessages(page);
  const legacy = ['StartGame', 'PauseGame', 'ResumeGame', 'RestartGame', 'ReturnToMenu'];
  for (const method of legacy) {
    assert(!sent.some((m) => m.method === method), 'legacy command must not be sent: ' + method);
  }
  assert(!consoleLines.some((line) => line.includes('旧指令已废弃')), 'Unity saw a legacy command');

  // 8) Refresh: best + nickname persist; last round is memory-only.
  await page.reload({ waitUntil: 'domcontentloaded' });
  await waitReady(page);
  const refreshed = await appState(page);
  assert(refreshed.page === 'Shell', 'reload must return to Shell');
  assert(refreshed.best === 232, 'best must survive reload');
  assert((await text(page, '#data-last')) === '—', 'last round must be cleared on reload');
  assert((await page.$eval('#nickname', (el) => el.value)) === '阿宝', 'nickname must survive reload');

  saveJson('h5-shell-core.json', {
    shell,
    entered,
    enterGames,
    best,
    migrated,
    lastRound: lastRoundAfterRound,
    lastRoundAfterRefresh: await text(page, '#data-last'),
    sentMethods: sent.map((m) => m.method),
    consoleLines,
  });

  await context.close();
  log('core PASS');
}

async function runProtocolMismatch(browser, baseUrl) {
  const { context, page } = await newPage(browser, { width: 1280, height: 720 }, `${baseUrl}/index.html?testhook=1`);
  await page.evaluate(() => {
    window.SurvivalApp.onReady('{"protocol":2}');
  });
  await page.waitForTimeout(500);

  const state = await appState(page);
  assert(state.page === 'Error', 'protocol mismatch must enter Error, got ' + state.page);
  assert(await page.$eval('#btn-start', (el) => el.disabled), 'start must stay disabled on protocol mismatch');
  assert((await text(page, '#error-text')).includes('版本不匹配'), 'error card must explain the version mismatch');
  await page.waitForTimeout(4000); // the real onReady (protocol 3) must not overwrite Error
  assert((await appState(page)).page === 'Error', 'late real onReady must not leave Error');
  await saveScreenshot(page, 'h5-shell-protocol-mismatch.png');
  await context.close();
  log('protocol mismatch PASS');
}

async function runMigration(browser, baseUrl) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1 });
  // Seed once per context; addInitScript also runs on reload and must not re-create old data.
  await context.addInitScript(() => {
    if (localStorage.getItem('__migrationSeeded') === '1') {
      return;
    }
    localStorage.setItem('survival.bestScore', '1000');
    localStorage.setItem('survival.bestScore.easy', '800');
    localStorage.setItem('survival.bestScore.normal', '1100');
    localStorage.setItem('survival.bestScore.hard', '1200');
    localStorage.setItem('__migrationSeeded', '1');
  });
  const page = await context.newPage();
  await page.goto(`${baseUrl}/index.html?testhook=1`, { waitUntil: 'domcontentloaded' });
  await waitReady(page);

  let state = await appState(page);
  assert(state.best === 1200, 'migration must take max(1000,800,1100,1200)=1200, got ' + state.best);
  assert((await page.evaluate(() => localStorage.getItem('survival.bestScore'))) === '1200', 'new key must be 1200');
  assert((await page.evaluate(() => localStorage.getItem('survival.bestScoreMigrated'))) === '1', 'marker must be set');
  // The v1.0 single key shares the new key's name, so it is overwritten with the max by design.
  // The three v1.1 per-difficulty keys must stay untouched.
  for (const [key, value] of [['bestScore.easy', '800'], ['bestScore.normal', '1100'], ['bestScore.hard', '1200']]) {
    assert(
      (await page.evaluate((k) => localStorage.getItem('survival.' + k), key)) === value,
      'old key must be preserved: ' + key,
    );
  }

  // Change an old key after migration: must not be read again.
  await page.evaluate(() => localStorage.setItem('survival.bestScore.hard', '9999'));
  await page.reload({ waitUntil: 'domcontentloaded' });
  await waitReady(page);
  state = await appState(page);
  assert(state.best === 1200, 'old keys must not be read after migration, got ' + state.best);

  // Marker exists but new key missing => 0, never fall back to old keys.
  await page.evaluate(() => localStorage.removeItem('survival.bestScore'));
  await page.reload({ waitUntil: 'domcontentloaded' });
  await waitReady(page);
  state = await appState(page);
  assert(state.best === 0, 'missing new key with marker must be 0, got ' + state.best);

  saveJson('h5-shell-migration.json', { migratedBest: 1200, afterOldKeyChange: 1200, missingNewKey: state.best });
  await context.close();
  log('migration PASS');
}

async function runFullscreen(browser, baseUrl) {
  const url = `${baseUrl}/index.html?testhook=1&perf=1&roundDuration=2`;
  const { context, page } = await newPage(browser, { width: 1280, height: 720 }, url);
  await waitReady(page);
  await page.click('#btn-start');
  await page.waitForTimeout(500);

  await page.click('#btn-fullscreen');
  let active = false;
  try {
    await page.waitForFunction(() => Boolean(document.fullscreenElement), null, { timeout: 5000 });
    active = true;
  } catch (error) {
    active = false;
  }

  if (!active) {
    saveJson('h5-shell-fullscreen.json', { supported: false, note: 'headless Chrome refused fullscreen' });
    await context.close();
    log('fullscreen unsupported in this environment (recorded)');
    return;
  }

  const info = await page.evaluate(() => {
    const frame = document.getElementById('game-frame');
    const buttons = Array.from(frame.querySelectorAll('button')).filter(
      (button) => button.getClientRects().length > 0 && !button.closest('[hidden]'),
    );
    return {
      id: document.fullscreenElement && document.fullscreenElement.id,
      visibleButtonsInFrame: buttons.length,
      toast: document.getElementById('fs-toast')?.textContent || '',
      toastVisible: !(document.getElementById('fs-toast')?.hidden ?? true),
    };
  });
  assert(info.id === 'game-frame', 'fullscreen element must be game-frame');
  assert(info.visibleButtonsInFrame === 0, 'fullscreen must show 0 web buttons');
  assert(info.toastVisible && info.toast.length > 0, 'fullscreen toast must be visible');
  await saveScreenshot(page, 'h5-shell-fullscreen.png');

  await page.waitForTimeout(3000);
  assert(await page.$eval('#fs-toast', (el) => el.hidden), 'fullscreen toast must auto-hide');

  await page.evaluate(() => document.exitFullscreen());
  await page.waitForFunction(() => !document.fullscreenElement, null, { timeout: 5000 });
  assert(!(await page.$eval('#btn-fullscreen', (el) => el.hidden)), 'fullscreen button must return after exit');
  assert(await page.$eval('.panel', (el) => el.getClientRects().length > 0), 'data panel must return after exit');
  saveJson('h5-shell-fullscreen.json', { supported: true, ...info });
  await context.close();

  // Rejection path (iPhone-like): requestFullscreen rejects.
  const rejectContext = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 1 });
  await rejectContext.addInitScript(() => {
    const proto = Element.prototype;
    Object.defineProperty(proto, 'requestFullscreen', {
      configurable: true,
      value: () => Promise.reject(new Error('denied')),
    });
  });
  const rejectPage = await rejectContext.newPage();
  await rejectPage.goto(url, { waitUntil: 'domcontentloaded' });
  await rejectPage.waitForFunction(
    () => window.SurvivalApp && window.SurvivalApp.getState && window.SurvivalApp.getState().ready,
    null,
    { timeout: READY_TIMEOUT },
  );
  await rejectPage.click('#btn-start');
  await rejectPage.waitForTimeout(400);
  await rejectPage.click('#btn-fullscreen');
  await rejectPage.waitForFunction(
    () => (document.getElementById('fullscreen-hint')?.textContent || '').includes('不支持网页全屏'),
    null,
    { timeout: 5000 },
  );
  saveJson('h5-shell-fullscreen-reject.json', { hint: await rejectPage.textContent('#fullscreen-hint') });
  await rejectContext.close();
  log('fullscreen PASS');
}

async function runViewports(browser, baseUrl) {
  ensureEvidenceDir();
  for (const viewport of VIEWPORTS) {
    // 移动视口必须开触摸：CSS 用 (pointer: coarse) 切手机操作文案，Playwright 不开 hasTouch 会
    // 渲染成键盘文案，截图就不是真手机状态。
    const isMobile = viewport.width < 900;
    // 390×844 跑一次真实 2s 短局（Unity 结算卡 + H5 数据栏）；其余视口只验布局，
    // GameOver 用注入态并在文件名标 layout-only，避免被当成真实结算。
    const realRound = viewport.name === '390x844';
    const query = realRound ? '&perf=1&roundDuration=2' : '';
    const context = await browser.newContext({
      viewport: { width: viewport.width, height: viewport.height },
      deviceScaleFactor: 1,
      hasTouch: isMobile,
    });
    const page = await context.newPage();
    await page.goto(`${baseUrl}/index.html?testhook=1${query}`, { waitUntil: 'domcontentloaded' });
    await waitReady(page);

    const noHorizontal = await page.evaluate(
      () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
    );
    assert(noHorizontal, viewport.name + ' has horizontal overflow');
    await saveScreenshot(page, `h5-shell-${viewport.name}-shell.png`);

    await page.click('#btn-start');
    await page.waitForTimeout(500);
    await saveScreenshot(page, `h5-shell-${viewport.name}-entered.png`);

    if (realRound) {
      await waitStatus(page, '游戏菜单', 30000);
      const layout = await canvasLayout(page);
      await clickCanvas(page, layout.menuStart);
      await waitStatus(page, '游戏中', 60000);
      await waitGameOverH5(page);
      await saveScreenshot(page, `h5-shell-${viewport.name}-gameover-real.png`);
    } else {
      // Layout-only：注入与 bridge 相同的载荷，只验 H5 排版；真实结算见 h5-shell-gameover-1280.png
      // 与 h5-shell-390x844-gameover-real.png。
      await page.evaluate(() => {
        window.SurvivalApp.onGameOver(JSON.stringify({
          protocol: 3,
          event: 'gameOver',
          payload: { result: 'win', score: 980, survivedSeconds: 60, difficulty: 'normal' },
        }));
      });
      await page.waitForTimeout(200);
      await saveScreenshot(page, `h5-shell-${viewport.name}-gameover-layout-only.png`);
    }

    const overflowAfter = await page.evaluate(
      () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
    );
    assert(overflowAfter, viewport.name + ' GameOver has horizontal overflow');
    await context.close();
    log('viewport ' + viewport.name + ' ok');
  }
}

async function main() {
  ensureEvidenceDir();
  const server = await startServer(PORT);
  const baseUrl = `http://127.0.0.1:${PORT}`;
  const browser = await chromium.launch({
    channel: 'chrome',
    headless: !HEADED,
    args: HEADED ? [] : ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'],
  });

  try {
    if (MODE === 'viewports') {
      await runViewports(browser, baseUrl);
    } else {
      await runCore(browser, baseUrl);
      await runProtocolMismatch(browser, baseUrl);
      await runMigration(browser, baseUrl);
      await runFullscreen(browser, baseUrl);
    }
  } finally {
    await browser.close();
    await new Promise((resolve) => server.close(resolve));
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
