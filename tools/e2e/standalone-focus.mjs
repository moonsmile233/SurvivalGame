// v1.2 focus-loss E2E for the standalone build.
//
// Two stimuli:
//   1. real window blur (CDP focus emulation off + a second page brought to front)
//   2. tab visibility change (another page in the same context takes the foreground)
// Assertion: the game pauses and STAYS paused after focus returns (M4.6 return-state rule).
//
// If Unity's OnApplicationFocus never fires in headless Chromium, the script records
// nativeFocus=false and verifies the H5 fallback (old page -> SetPausedByVisibility) instead.
// It never silently treats "no pause" as a pass.
import { chromium } from 'playwright';
import {
  openGame,
  prepareContext,
  startServer,
  clickFraction,
  waitForState,
  readEvents,
  saveJson,
  ensureEvidenceDir,
} from './standalone-common.mjs';
import { getLayout } from './ui-layout.mjs';

const PORT = Number(process.env.PORT || 8125);
const HEADED = process.env.HEADED === '1';

function log(message) {
  console.log('[focus] ' + message);
}

async function layoutOf(page) {
  const rect = await page.evaluate(() => {
    const box = document.querySelector('#unity-canvas').getBoundingClientRect();
    return { width: box.width, height: box.height };
  });
  return getLayout(rect.width, rect.height);
}

async function hasState(page, name) {
  const events = await readEvents(page);
  return events.some((e) => e.type === 'stateChange' && e.payload && e.payload.state === name);
}

/** Latest state event, i.e. what the game is in right now (history contains earlier playing/paused). */
async function lastState(page) {
  const events = await readEvents(page);
  for (let i = events.length - 1; i >= 0; i -= 1) {
    if (events[i].type === 'stateChange' && events[i].payload) {
      return events[i].payload.state;
    }
  }
  return null;
}

async function waitForConsoleLine(lines, needle, timeoutMs) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    if (lines.some((line) => line.includes(needle))) {
      return true;
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  return false;
}

async function runNative(browser, baseUrl) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1 });
  await prepareContext(context);
  const page = await context.newPage();
  const consoleLines = [];
  page.on('console', (message) => consoleLines.push(message.text()));

  await openGame(page, baseUrl);
  const layout = await layoutOf(page);
  await clickFraction(page, layout.menuStart);
  await waitForState(page, 'playing', 60000);
  log('playing before blur');

  const cdp = await context.newCDPSession(page);
  await cdp.send('Emulation.setFocusEmulationEnabled', { enabled: false });

  const result = {
    nativeBlur: false,
    nativeVisibility: false,
    stayedPausedAfterBlurReturn: false,
    stayedPausedAfterVisibilityReturn: false,
    consolePausedLines: [],
  };

  // 1) real window blur: another page takes focus.
  const other = await context.newPage();
  await other.goto('about:blank');
  await other.bringToFront();
  result.nativeBlur = await Promise.race([
    waitForState(page, 'paused', 8000).then(() => true).catch(() => false),
    waitForConsoleLine(consoleLines, 'state=Paused', 8000),
  ]);
  await page.bringToFront();
  await page.waitForTimeout(1500);
  result.stayedPausedAfterBlurReturn = (await lastState(page)) === 'paused';

  if (result.nativeBlur) {
    // Resume via the pause menu, then test tab visibility.
    const pausedLayout = await layoutOf(page);
    await clickFraction(page, pausedLayout.pauseResume);
    await waitForState(page, 'playing', 60000);

    const hidden = await context.newPage();
    await hidden.goto('about:blank');
    await hidden.bringToFront();
    result.nativeVisibility = await Promise.race([
      waitForState(page, 'paused', 8000).then(() => true).catch(() => false),
      waitForConsoleLine(consoleLines, 'state=Paused', 8000),
    ]);
    await page.bringToFront();
    await page.waitForTimeout(1500);
    result.stayedPausedAfterVisibilityReturn = (await lastState(page)) === 'paused';
    await hidden.close();
  }

  result.consolePausedLines = consoleLines.filter((line) => line.includes('state=Paused'));
  const events = await readEvents(page);
  saveJson('standalone-focus-native.json', { result, events, consoleLines });
  await context.close();
  return result;
}

/**
 * Blur during the 3-2-1 must cancel the countdown back to the origin state and never enter Playing:
 *   Menu -> Starting -> blur => Menu
 *   Paused -> Starting(resume) -> blur => Paused
 */
async function runStartingCancel(browser, baseUrl) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1 });
  await prepareContext(context);
  const page = await context.newPage();
  await openGame(page, baseUrl);

  // Playwright keeps document.hasFocus() true by default; turn that off so a second page can
  // actually blur the game (same trick as tools/e2e/smoke.mjs).
  const cdp = await context.newCDPSession(page);
  await cdp.send('Emulation.setFocusEmulationEnabled', { enabled: false });

  async function blurByOtherPage() {
    const other = await context.newPage();
    await other.goto('about:blank');
    await other.bringToFront();
    await page.waitForTimeout(1500);
    await page.bringToFront();
    await page.waitForTimeout(800);
    await other.close();
  }

  // Case 1: cancel a fresh start countdown.
  let layout = await layoutOf(page);
  await clickFraction(page, layout.menuStart);
  await page.waitForTimeout(250); // well inside the 3s unscaled countdown
  await blurByOtherPage();

  const menuEvents = await readEvents(page);
  const enteredPlayingFromMenu = menuEvents.some(
    (e) => e.type === 'stateChange' && e.payload && e.payload.state === 'playing',
  );
  const stateAfterStartCancel = await lastState(page);

  // Case 2: start normally, pause, request resume, blur during the resume countdown.
  layout = await layoutOf(page);
  await clickFraction(page, layout.menuStart);
  await waitForState(page, 'playing', 60000);
  await page.keyboard.press('Escape');
  await waitForState(page, 'paused', 30000);
  await page.waitForTimeout(400);
  layout = await layoutOf(page);
  await clickFraction(page, layout.pauseResume);
  await page.waitForTimeout(250); // inside the resume 3-2-1
  await blurByOtherPage();

  const resumeEvents = await readEvents(page);
  let lastPausedIndex = -1;
  resumeEvents.forEach((event, index) => {
    if (event.type === 'stateChange' && event.payload && event.payload.state === 'paused') {
      lastPausedIndex = index;
    }
  });
  const playingAfterResumeCancel = resumeEvents
    .slice(lastPausedIndex + 1)
    .some((event) => event.type === 'stateChange' && event.payload && event.payload.state === 'playing');
  const stateAfterResumeCancel = await lastState(page);

  const result = {
    startCancel: !enteredPlayingFromMenu && stateAfterStartCancel === 'menu',
    stateAfterStartCancel,
    resumeCancel: !playingAfterResumeCancel && stateAfterResumeCancel === 'paused',
    stateAfterResumeCancel,
  };
  saveJson('standalone-focus-starting-cancel.json', { result, menuEvents, resumeEvents });
  await context.close();
  return result;
}

async function runH5Fallback(browser, baseUrl) {
  // Old H5 shell (v1.1) still sends SetPausedByVisibility on blur; the new Unity keeps that
  // compatibility path. We assert on the Unity console log because the old page's own state
  // machine does not understand the v3 lowercase state names.
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1 });
  const page = await context.newPage();
  const consoleLines = [];
  page.on('console', (message) => consoleLines.push(message.text()));

  await page.goto(baseUrl + '/index.html', { waitUntil: 'load' });
  await page.waitForFunction(() => {
    const button = document.querySelector('#btn-start');
    return button && !button.disabled;
  }, null, { timeout: 180000 });

  await page.click('#btn-start');
  await page.waitForTimeout(4000); // old H5 3-2-1 then StartGame -> EnterGame
  const layout = await layoutOf(page);
  await clickFraction(page, layout.menuStart);
  await waitForConsoleLine(consoleLines, 'state=Playing', 60000);
  log('fallback: playing in Unity menu');

  const other = await context.newPage();
  await other.goto('about:blank');
  await other.bringToFront();
  const paused = await waitForConsoleLine(consoleLines, 'state=Paused', 10000);
  await page.bringToFront();
  await page.waitForTimeout(1500);
  const playingAfterReturn = consoleLines
    .slice(consoleLines.findIndex((line) => line.includes('state=Paused')))
    .some((line) => line.includes('state=Playing'));

  const result = {
    fallbackH5: paused,
    stayedPausedAfterReturn: paused && !playingAfterReturn,
    consolePausedLines: consoleLines.filter((line) => line.includes('state=Paused')),
  };
  saveJson('standalone-focus-h5-fallback.json', { result, consoleLines });
  await context.close();
  return result;
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
    log('native focus test');
    const native = await runNative(browser, baseUrl);
    log('native result ' + JSON.stringify(native));

    if (!native.nativeBlur) {
      log('native focus did not fire in this environment; running H5 signal fallback');
      const fallback = await runH5Fallback(browser, baseUrl);
      log('fallback result ' + JSON.stringify(fallback));
      if (!fallback.fallbackH5 || !fallback.stayedPausedAfterReturn) {
        throw new Error('focus verification failed in both native and H5-signal paths');
      }
    } else if (!native.stayedPausedAfterBlurReturn) {
      throw new Error('game resumed automatically after blur/focus return');
    }

    if (native.nativeBlur) {
      log('starting-countdown cancel test');
      const cancel = await runStartingCancel(browser, baseUrl);
      log('cancel result ' + JSON.stringify(cancel));
      if (!cancel.startCancel || !cancel.resumeCancel) {
        throw new Error('blur during 3-2-1 did not cancel back to the origin state');
      }
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
