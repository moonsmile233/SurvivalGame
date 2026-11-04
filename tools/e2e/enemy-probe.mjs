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
// MOVE=circle：每 0.9s 换一个方向（W→D→S→A）绕圈，用于黑盒触发 Flanker 的 6s Direct 超时。
const MOVE = (process.env.MOVE || '').toLowerCase();
// FLANK_TIMEOUT：perf-only 覆盖侧绕超时（秒），黑盒压不到 6s 时用来验证 Direct 分支。
const FLANK_TIMEOUT = (process.env.FLANK_TIMEOUT || '').trim();
// DIFFICULTY=easy|normal|hard：perf 探针按档验证速度/上限（默认 Normal）。
const DIFFICULTY = (process.env.DIFFICULTY || '').trim();
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
const seenStateShots = new Set();
let shotIndex = 0;
let sawState = false;
let sawFlankEngage = false;
let sawFlankSide = false;
let sawFlankBack = false;

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

  // 每个状态只拍首帧；Flank 出生点通常在视野外不拍，改按 dist/bearing 拍可见的侧翼/背后。
  for (const tag of ['Windup', 'Dash', 'Recover', 'Flank', 'Pounce', 'Direct']) {
    if (text.includes(`state=${tag}`)) {
      sawState = true;
      if (tag !== 'Flank' && !seenStateShots.has(tag)) {
        seenStateShots.add(tag);
        pendingShots.push(shot(tag.toLowerCase()));
      }
    }
  }

  if (text.includes('state=Flank')) {
    // 第一次进入 4.5 交战距离时补一张（此时敌人才进画面）。
    const distanceMatch = text.match(/dist=([\d.]+)/);
    if (!sawFlankEngage && distanceMatch && Number(distanceMatch[1]) <= 4.5) {
      sawFlankEngage = true;
      pendingShots.push(shot('flank-engage'));
    }

    // 可见的侧后证据：dist<2.5 且 |bearing| 50~90° 拍侧翼、>110° 拍背后。
    const bearingMatch = text.match(/bearing=(-?[\d.]+)/);
    if (distanceMatch && bearingMatch) {
      const distance = Number(distanceMatch[1]);
      const bearing = Math.abs(Number(bearingMatch[1]));
      if (!sawFlankSide && distance < 2.5 && bearing >= 50 && bearing <= 90) {
        sawFlankSide = true;
        pendingShots.push(shot('flank-side'));
      }

      if (!sawFlankBack && distance < 2.5 && bearing > 110) {
        sawFlankBack = true;
        pendingShots.push(shot('flank-back'));
      }
    }
  }
});

console.log(`[enemy-probe] kind=${KIND} port=${PORT} headed=${HEADED} out=${OUT}`);
await openGame(
  page,
  `http://127.0.0.1:${PORT}`,
  `?perf=1&autostart=1&enemyKind=${KIND}&enemyLimit=1&roundDuration=${RUN_SECONDS + 30}` +
    `&invincible=${INVINCIBLE ? 1 : 0}&perfSeconds=${RUN_SECONDS}` +
    (FLANK_TIMEOUT ? `&flankTimeout=${FLANK_TIMEOUT}` : '') +
    (DIFFICULTY ? `&difficulty=${DIFFICULTY}` : ''),
);

// 黑盒驱动玩家：按住 W/D/S/A 循环绕圈，让 Flanker 的 side-back 目标点持续移动，
// 距离稳定在 (1.2, 4.5] 时就会走满 6s 触发 Direct（不需要新增玩法参数）。
let moveTask = null;
if (MOVE === 'circle') {
  moveTask = (async () => {
    const keys = ['w', 'd', 's', 'a'];
    const deadline = Date.now() + RUN_SECONDS * 1000;
    let i = 0;
    while (Date.now() < deadline) {
      const key = keys[i % keys.length];
      await page.keyboard.down(key);
      await page.waitForTimeout(900);
      await page.keyboard.up(key);
      i++;
    }
  })();
}

await page.waitForTimeout(RUN_SECONDS * 1000);
if (moveTask) {
  await moveTask;
}
await shot('final');
await Promise.all(pendingShots);

fs.writeFileSync(path.join(OUT, `${KIND}-console.log`), logs.join('\n'));
fs.writeFileSync(
  path.join(OUT, `${KIND}-summary.json`),
  JSON.stringify({ kind: KIND, headed: HEADED, invincible: INVINCIBLE, move: MOVE, runSeconds: RUN_SECONDS, sawState, logs }, null, 2),
);

await context.close();
await browser.close();
await new Promise((resolve) => server.close(resolve));

if (!sawState) {
  console.error(`[enemy-probe] FAIL: no [${KIND}] state logs captured`);
  process.exit(1);
}

console.log(`[enemy-probe] PASS: ${logs.length} log lines, ${shotIndex} screenshots -> ${OUT}`);
