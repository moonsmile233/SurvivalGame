// v1.2 standalone 40-enemy perf check.
// Headed Chrome (offscreen) for a real GPU, same approach as tools/e2e/perf.mjs; the standalone
// Unity page is used directly so the old H5 start button is not involved.
import { chromium } from 'playwright';
import { ensureEvidenceDir, prepareContext, saveJson, startServer } from './standalone-common.mjs';

const PORT = Number(process.env.PORT || 8126);
const CAP = Number(process.env.CAP || 40);
const PERF_SECONDS = Number(process.env.PERF_SECONDS || 60);
const TIMEOUT = Number(process.env.PERF_TIMEOUT || 300000);

const PERF_ARGS = [
  '--disable-backgrounding-occluded-windows',
  '--disable-renderer-backgrounding',
  '--disable-background-timer-throttling',
  '--disable-features=CalculateNativeWinOcclusion',
  '--window-position=-32000,-32000',
];

function log(message) {
  console.log('[perf] ' + message);
}

async function main() {
  ensureEvidenceDir();
  const server = await startServer(PORT);
  const baseUrl = `http://127.0.0.1:${PORT}`;
  const browser = await chromium.launch({
    channel: 'chrome',
    headless: false,
    args: PERF_ARGS,
  });

  try {
    const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1 });
    await prepareContext(context);
    const page = await context.newPage();
    const consoleLines = [];
    page.on('console', (message) => {
      consoleLines.push(message.text());
      if (process.env.DEBUG === '1' && /PERF|state=|Bridge/.test(message.text())) {
        console.log('CONSOLE:', message.text());
      }
    });
    page.on('pageerror', (error) => console.error('PAGEERROR:', error.message));

    // invincible=1: the player must survive the fill phase; otherwise Result freezes the run
    // before the 40-enemy steady sample and no summary is logged (same as tools/e2e/perf.mjs).
    const query = `?perf=1&enemyLimit=${CAP}&roundDuration=600&perfSeconds=${PERF_SECONDS}&autostart=1&invincible=1`;
    await page.goto(`${baseUrl}/unity/index.html${query}`, { waitUntil: 'load' });

    // PerfProbe logs [PERF-SUMMARY] when the sample window closes.
    const started = Date.now();
    let summary = null;
    while (Date.now() - started < TIMEOUT) {
      const line = consoleLines.find((text) => text.startsWith('[PERF-SUMMARY] '));
      if (line) {
        summary = JSON.parse(line.slice('[PERF-SUMMARY] '.length));
        break;
      }
      await page.waitForTimeout(1000);
    }

    if (!summary) {
      throw new Error('no [PERF-SUMMARY] within ' + TIMEOUT + 'ms');
    }

    const payload = {
      cap: CAP,
      perfSeconds: PERF_SECONDS,
      capturedAt: new Date().toISOString(),
      summary,
      perfLines: consoleLines.filter((text) => text.startsWith('[PERF')),
    };
    saveJson(`perf-${CAP}.json`, payload);
    log(`${CAP} enemies: fps=${summary.steadyFps} p95=${summary.steadyP95ms}ms alloc=${summary.steadyAllocKBps}KB/s capAt=${summary.capAt}`);
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
