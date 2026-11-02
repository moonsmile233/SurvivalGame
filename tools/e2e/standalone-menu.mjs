// v1.2 standalone (no H5) menu/pause/result E2E.
//
// Modes:
//   MODE=flow      (default) 1280x720: menu -> hard -> 3-2-1 -> pause -> resume -> restart -> result -> again -> menu
//   MODE=mobile    390x844: same click chain (hits the 640x360 reference-resolution branch)
//   MODE=viewports menu/countdown/pause screenshots at 6 viewports
//
// Clicks use fractional canvas coordinates derived from SceneSetup anchors (ui-layout.mjs);
// assertions use the bridge events captured in window.__survivalEvents, because Unity has no DOM.
import { chromium } from 'playwright';
import {
  openGame,
  prepareContext,
  startServer,
  clickFraction,
  waitForState,
  waitForDifficulty,
  waitForGameOver,
  gameOverCount,
  waitForGameOverAfter,
  readEvents,
  saveScreenshot,
  saveJson,
  assert,
  ensureEvidenceDir,
} from './standalone-common.mjs';
import { getLayout } from './ui-layout.mjs';

const MODE = (process.env.MODE || 'flow').toLowerCase();
const PORT = Number(process.env.PORT || 8124);
const HEADED = process.env.HEADED === '1';
const EVIDENCE_TAG = process.env.EVIDENCE_TAG || '';

const VIEWPORTS = [
  { name: '320x568', width: 320, height: 568 },
  { name: '390x844', width: 390, height: 844 },
  { name: '844x390', width: 844, height: 390 },
  { name: '768x1024', width: 768, height: 1024 },
  { name: '1280x720', width: 1280, height: 720 },
  { name: '1920x1080', width: 1920, height: 1080 },
];

function log(message) {
  console.log('[standalone] ' + message);
}

async function layoutOf(page) {
  const rect = await page.evaluate(() => {
    const box = document.querySelector('#unity-canvas').getBoundingClientRect();
    return { width: box.width, height: box.height };
  });
  return getLayout(rect.width, rect.height);
}

async function runFlow(page, label, withResult) {
  const shotPrefix = 'standalone-' + label + (EVIDENCE_TAG ? '-' + EVIDENCE_TAG : '');
  await saveScreenshot(page, shotPrefix + '-menu.png');

  let layout = await layoutOf(page);
  log(`${label}: canvas metrics ` + JSON.stringify(layout.metrics));

  // Difficulty card 3 = hard; must emit onDifficultyChanged before starting.
  await clickFraction(page, layout.menuCards[2]);
  await waitForDifficulty(page, 'hard');
  await saveScreenshot(page, shotPrefix + '-menu-hard.png');

  await clickFraction(page, layout.menuStart);
  await page.waitForTimeout(400);
  await saveScreenshot(page, shotPrefix + '-countdown.png');

  await waitForState(page, 'playing', 60000);
  await saveScreenshot(page, shotPrefix + '-playing.png');

  // Pause via Esc (desktop shortcut), then resume through the 3-2-1.
  await page.keyboard.press('Escape');
  await waitForState(page, 'paused', 30000);
  // Wait one UI frame before clicking a panel that was just activated: the GraphicRaycaster
  // needs a Canvas rebuild, otherwise the first click after SetActive can be swallowed.
  await page.waitForTimeout(400);
  await saveScreenshot(page, shotPrefix + '-paused.png');

  layout = await layoutOf(page);
  await clickFraction(page, layout.pauseResume);
  await waitForState(page, 'playing', 60000);

  // Pause again, restart from the pause menu (same difficulty, fresh round).
  await page.keyboard.press('Escape');
  await waitForState(page, 'paused', 30000);
  await page.waitForTimeout(400);
  layout = await layoutOf(page);
  await clickFraction(page, layout.pauseRestart);
  await waitForState(page, 'playing', 60000);

  if (!withResult) {
    return;
  }

  const gameOver = await waitForGameOver(page, 300000);
  log(`${label}: gameOver ` + JSON.stringify(gameOver));
  assert(gameOver.payload.difficulty === 'hard', 'difficulty must stay hard, got ' + gameOver.payload.difficulty);
  // roundDuration=2 + hard multiplier: 2s * 10 * 1.6 + 200 win bonus = 232.
  assert(gameOver.payload.score === 232, 'hard 2s win score must be 232, got ' + gameOver.payload.score);
  await page.waitForTimeout(400);
  await saveScreenshot(page, shotPrefix + '-result.png');

  // Play again -> 3-2-1 -> playing; then pause -> return to menu.
  layout = await layoutOf(page);
  await clickFraction(page, layout.resultAgain);
  await waitForState(page, 'playing', 60000);

  await page.keyboard.press('Escape');
  await waitForState(page, 'paused', 30000);
  await page.waitForTimeout(400);
  layout = await layoutOf(page);
  await clickFraction(page, layout.pauseMenu);
  await waitForState(page, 'menu', 30000);
  await saveScreenshot(page, shotPrefix + '-menu-returned.png');

  // Second short round: exercise Result -> 返回菜单 (the first result used 再来一局).
  const previousGameOvers = await gameOverCount(page);
  layout = await layoutOf(page);
  await clickFraction(page, layout.menuStart);
  await waitForState(page, 'playing', 60000);
  const secondGameOver = await waitForGameOverAfter(page, previousGameOvers);
  assert(secondGameOver.payload.difficulty === 'hard', 'second round must keep hard difficulty');
  await page.waitForTimeout(400);

  layout = await layoutOf(page);
  await clickFraction(page, layout.resultMenu);
  await waitForState(page, 'menu', 30000);
  await saveScreenshot(page, shotPrefix + '-menu-after-result.png');
}

async function runViewports(browser, baseUrl) {
  for (const viewport of VIEWPORTS) {
    const context = await browser.newContext({
      viewport: { width: viewport.width, height: viewport.height },
      deviceScaleFactor: 1,
    });
    await prepareContext(context);
    const page = await context.newPage();
    log(`viewport ${viewport.name}: loading`);
    await openGame(page, baseUrl, '?perf=1&roundDuration=2');
    await saveScreenshot(page, `standalone-viewport-${viewport.name}-menu.png`);

    const layout = await layoutOf(page);
    await clickFraction(page, layout.menuStart);
    await page.waitForTimeout(400);
    await saveScreenshot(page, `standalone-viewport-${viewport.name}-countdown.png`);

    await waitForState(page, 'playing', 60000);
    await page.keyboard.press('Escape');
    await waitForState(page, 'paused', 30000);
    await page.waitForTimeout(400);
    await saveScreenshot(page, `standalone-viewport-${viewport.name}-paused.png`);

    // Resume and let the 2s (game time) round finish so every viewport also has a result shot.
    const resumeLayout = await layoutOf(page);
    await clickFraction(page, resumeLayout.pauseResume);
    await waitForState(page, 'playing', 60000);
    await waitForGameOver(page, 300000);
    await page.waitForTimeout(400);
    await saveScreenshot(page, `standalone-viewport-${viewport.name}-result.png`);
    await context.close();
  }
}

async function main() {
  ensureEvidenceDir();
  const server = await startServer(PORT);
  const baseUrl = `http://127.0.0.1:${PORT}`;
  log(`static server ${baseUrl}`);

  const browser = await chromium.launch({
    channel: 'chrome',
    headless: !HEADED,
    args: HEADED ? [] : ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'],
  });

  try {
    if (MODE === 'viewports') {
      await runViewports(browser, baseUrl);
      log('viewports done');
      return;
    }

    const viewport = MODE === 'mobile' ? { width: 390, height: 844 } : { width: 1280, height: 720 };
    const context = await browser.newContext({ viewport, deviceScaleFactor: 1 });
    await prepareContext(context);
    const page = await context.newPage();
    // ?perf=1 gives us the round-duration override; autostart is deliberately NOT set so the
    // menu -> difficulty -> start path is exercised (策划裁决 B).
    await openGame(page, baseUrl, '?perf=1&roundDuration=2');
    await runFlow(page, MODE === 'mobile' ? 'mobile' : 'flow', true);

    const events = await readEvents(page);
    saveJson(`standalone-${MODE}-events.json`, events);
    log(`events saved (${events.length})`);
    await context.close();
  } finally {
    await browser.close();
    await new Promise((resolve) => server.close(resolve));
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
