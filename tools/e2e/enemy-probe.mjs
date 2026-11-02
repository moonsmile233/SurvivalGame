// M6 C3/C4 single-enemy probe (standalone WebGL, no H5).
//
// Usage:
//   KIND=charger node enemy-probe.mjs
//   KIND=flanker node enemy-probe.mjs
//   HEADED=0 KIND=charger node enemy-probe.mjs       # headless (fast, but SwiftShader fps is meaningless)
//
// What it does:
//   - loads web/unity/index.html with ?perf=1&autostart=1&enemyKind=<kind>&enemyLimit=1&invincible=1
//   - listens for [Charger] / [Flanker] state logs from EnemyAI
//   - takes screenshots on Windup / Dash / Recover (or Flank / Pounce for flanker)
//   - writes screenshots + console log to review/m6-c3-<kind>/ (user-facing evidence)
//
// The build may carry a Unity "trial version" watermark until the editor license is refreshed;
// that is an environment artifact, not a gameplay result. C7 must be rebuilt clean.
import { chromium } from 'playwright';
import fs from 'node:fs';
import path from 'node:path';
import { startServer, prepareContext, openGame, repoRoot } from './standalone-common.mjs';
import { chromiumLaunchOptions } from './launch.mjs';

const KIND = (process.env.KIND || 'charger').toLowerCase();
const PORT = Number(process.env.PORT || 8131);
// 默认无头（SwiftShader 足够拍静态表现）；HEADED=1 才开窗，且 launch.mjs 会把窗口移到屏幕外。
// 无头下 Unity 游戏时间约慢 10 倍，默认给 120s 墙钟时间等一次完整 Approach→Windup→Dash。
const HEADED = process.env.HEADED === '1';
const INVINCIBLE = process.env.INVINCIBLE !== '0';
const RUN_SECONDS = Number(process.env.RUN_SECONDS || (HEADED ? 45 : 120));
const OUT = process.env.OUT || path.join(repoRoot, 'review', `m6-c3-${KIND}`);

fs.mkdirSync(OUT, { recursive: true });

const server = await startServer(PORT);
const browser = await chromium.launch(chromiumLaunchOptions());
const context = await browser.newContext({
  viewport: { width: 1280, height: 720 },
  deviceScaleFactor: 1,
});
await prepareContext(context);
const page = await context.newPage();

const logs = [];
const pendingShots = [];
let shotIndex = 0;
let sawState = false;

async function shot(tag) {
  const index = String(shotIndex++).padStart(2, '0');
  const file = path.join(OUT, `${KIND}-${tag}-${index}.png`);
  try {
    await page.screenshot({ path: file });
    console.log(`[enemy-probe] shot ${path.relative(repoRoot, file)}`);
  } catch (error) {
    console.warn(`[enemy-probe] screenshot failed (${tag}): ${error.message}`);
  }
}

page.on('console', (message) => {
  const text = message.text();
  if (text.includes(`[${KIND === 'charger' ? 'Charger' : 'Flanker'}]`) || text.includes('[PERF]')
      || text.includes('damage=')) {
    logs.push(text);
    console.log('[console] ' + text);
  }

  for (const tag of ['Windup', 'Dash', 'Recover', 'Flank', 'Pounce', 'Direct']) {
    if (text.includes(`state=${tag}`)) {
      sawState = true;
      pendingShots.push(shot(tag.toLowerCase()));
    }
  }
});

console.log(`[enemy-probe] kind=${KIND} port=${PORT} headed=${HEADED} out=${OUT}`);
await openGame(
  page,
  `http://127.0.0.1:${PORT}`,
  `?perf=1&autostart=1&enemyKind=${KIND}&enemyLimit=1&roundDuration=${RUN_SECONDS + 30}` +
    `&invincible=${INVINCIBLE ? 1 : 0}&perfSeconds=${RUN_SECONDS}`,
);

await page.waitForTimeout(RUN_SECONDS * 1000);
await shot('final');
await Promise.all(pendingShots);

fs.writeFileSync(path.join(OUT, `${KIND}-console.log`), logs.join('\n'));
fs.writeFileSync(
  path.join(OUT, `${KIND}-summary.json`),
  JSON.stringify({ kind: KIND, headed: HEADED, invincible: INVINCIBLE, runSeconds: RUN_SECONDS, sawState, logs }, null, 2),
);

await context.close();
await browser.close();
await new Promise((resolve) => server.close(resolve));

if (!sawState) {
  console.error(`[enemy-probe] FAIL: no [${KIND}] state logs captured`);
  process.exit(1);
}

console.log(`[enemy-probe] PASS: ${logs.length} log lines, ${shotIndex} screenshots -> ${OUT}`);
