// Transitional check: old H5 shell (v1.1) + new Unity package (protocol 3).
//
// Expected (documented in docs/bridge-protocol.md §6.3):
//   - onReady warns about protocol 3 but does not block;
//   - H5 StartGame is treated as EnterGame: the canvas shows the Unity menu;
//   - the player can start and finish a round from the Unity menu;
//   - onGameOver still updates the old H5 best score.
// This is NOT the v1.2 shell acceptance; it only proves the transitional compatibility path.
import { chromium } from 'playwright';
import {
  ensureEvidenceDir,
  saveJson,
  saveScreenshot,
  startServer,
  clickFraction,
} from './standalone-common.mjs';
import { getLayout } from './ui-layout.mjs';

const PORT = Number(process.env.PORT || 8127);
const TIMEOUT = Number(process.env.READY_TIMEOUT || 180000);

function log(message) {
  console.log('[h5-transition] ' + message);
}

async function main() {
  ensureEvidenceDir();
  const server = await startServer(PORT);
  const baseUrl = `http://127.0.0.1:${PORT}`;
  const browser = await chromium.launch({
    channel: 'chrome',
    headless: true,
    args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'],
  });

  try {
    const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1 });
    const page = await context.newPage();
    const consoleLines = [];
    page.on('console', (message) => consoleLines.push(message.text()));

    await page.goto(baseUrl + '/index.html?perf=1&roundDuration=2', { waitUntil: 'load' });
    await page.waitForFunction(() => {
      const button = document.querySelector('#btn-start');
      return button && !button.disabled;
    }, null, { timeout: TIMEOUT });

    const protocolWarning = consoleLines.some((line) => line.includes('协议版本不一致'));
    log('protocol warning seen: ' + protocolWarning);

    // Old H5 start button -> its own 3s countdown -> StartGame (now EnterGame).
    await page.click('#btn-start');
    await page.waitForTimeout(4000);

    const canvas = await page.evaluate(() => {
      const box = document.querySelector('#unity-canvas').getBoundingClientRect();
      return { width: box.width, height: box.height };
    });
    const layout = getLayout(canvas.width, canvas.height);
    await clickFraction(page, layout.menuStart);
    await page.waitForFunction(
      () => document.body.dataset.page === 'Result',
      null,
      { timeout: 300000 },
    );

    const bestScore = await page.evaluate(() => localStorage.getItem('survival.bestScore.normal'));
    const resultVisible = await page.evaluate(() => {
      const result = document.querySelector('#result');
      return result ? !result.hidden : false;
    });
    await saveScreenshot(page, 'h5-transition-result.png');
    await saveJson('h5-transition.json', {
      protocolWarning,
      bestScore,
      resultVisible,
      h5Page: await page.evaluate(() => document.body.dataset.page),
      consoleLines: consoleLines.filter((line) => /协议|App|Bridge|GameManager/.test(line)),
    });

    if (!protocolWarning) {
      throw new Error('old H5 did not report the protocol-3 warning');
    }
    if (Number(bestScore) !== 226) {
      throw new Error('old H5 best score not updated to 226, got ' + bestScore);
    }
    log('transition ok: bestScore=' + bestScore + ' h5Page=Result');
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
