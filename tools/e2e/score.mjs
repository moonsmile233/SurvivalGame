// M6 C6 计分 E2E：分段存活分 + 剩余血量分 + 难度倍率 + 胜利奖励。
//
// 每局用 ?perf=1&autostart=1&invincible=1&difficulty=<d>&roundDuration=<n> 跳过菜单并固定满血，
// 只断言“胜利且未受击”的确定性分值（失败/血量分由 Assets/Editor/ScoreSelfCheck.cs 覆盖）。
//
// 运行：node tools/e2e/score.mjs
// 期望值来自 proposal-M6-内容-实施计划.md §4.4：
//   存活分 10/12/15/20 每秒（0-15/15-30/30-45/45-60，满 60s = 855）
//   最终分 = round((存活分 + 剩余 HP) × 倍率) + 200
import { chromium } from 'playwright';
import {
  openGame,
  prepareContext,
  startServer,
  waitForGameOver,
  saveJson,
  assert,
} from './standalone-common.mjs';

const PORT = Number(process.env.PORT || 8126);
const HEADED = process.env.HEADED === '1';

// survival/health 是乘倍率后取整的明细分量；score 是最终分（先相加再乘、最后加奖励）。
const CASES = [
  { label: 'easy-60s', difficulty: 'easy', seconds: 60, multiplier: 1.0, survival: 855, health: 100, score: 1155 },
  { label: 'normal-2s', difficulty: 'normal', seconds: 2, multiplier: 1.3, survival: 26, health: 130, score: 356 },
  { label: 'normal-15s', difficulty: 'normal', seconds: 15, multiplier: 1.3, survival: 195, health: 130, score: 525 },
  { label: 'normal-30s', difficulty: 'normal', seconds: 30, multiplier: 1.3, survival: 429, health: 130, score: 759 },
  { label: 'normal-45s', difficulty: 'normal', seconds: 45, multiplier: 1.3, survival: 722, health: 130, score: 1052 },
  { label: 'normal-60s', difficulty: 'normal', seconds: 60, multiplier: 1.3, survival: 1112, health: 130, score: 1442 },
  { label: 'hard-60s', difficulty: 'hard', seconds: 60, multiplier: 1.6, survival: 1368, health: 160, score: 1728 },
];

function log(message) {
  console.log('[score] ' + message);
}

async function runCase(browser, baseUrl, testCase) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1 });
  await prepareContext(context);
  const page = await context.newPage();
  const query =
    `?perf=1&autostart=1&invincible=1&difficulty=${testCase.difficulty}` +
    `&roundDuration=${testCase.seconds}`;

  log(`${testCase.label}: loading`);
  await openGame(page, baseUrl, query);
  const envelope = await waitForGameOver(page, (testCase.seconds + 120) * 1000);
  const payload = envelope.payload;
  log(`${testCase.label}: gameOver ` + JSON.stringify(envelope));

  assert(payload.result === 'win', `${testCase.label}: expected win, got ${payload.result}`);
  assert(payload.difficulty === testCase.difficulty, `${testCase.label}: difficulty echo mismatch`);
  assert(payload.survivedSeconds === testCase.seconds, `${testCase.label}: survivedSeconds ${payload.survivedSeconds}`);
  assert(payload.kills === 0, `${testCase.label}: kills must stay 0, got ${payload.kills}`);
  assert(payload.hitCount === 0, `${testCase.label}: invincible round must have hitCount 0, got ${payload.hitCount}`);
  assert(payload.score === testCase.score, `${testCase.label}: score expected ${testCase.score}, got ${payload.score}`);
  // Unity 的 float 序列化成十进制会带尾差（1.3 → 1.2999999523162842），按 1e-4 比较。
  const multiplier = payload.breakdown ? payload.breakdown.multiplier : NaN;
  assert(
    Math.abs(multiplier - testCase.multiplier) < 1e-4,
    `${testCase.label}: multiplier expected ${testCase.multiplier}, got ${multiplier}`,
  );
  assert(
    payload.breakdown.survival === testCase.survival,
    `${testCase.label}: survival expected ${testCase.survival}, got ${payload.breakdown.survival}`,
  );
  assert(
    payload.breakdown.health === testCase.health,
    `${testCase.label}: health expected ${testCase.health}, got ${payload.breakdown.health}`,
  );
  assert(payload.breakdown.winBonus === 200, `${testCase.label}: winBonus must be 200`);

  await context.close();
  return {
    label: testCase.label,
    difficulty: testCase.difficulty,
    seconds: testCase.seconds,
    score: payload.score,
    breakdown: payload.breakdown,
    hitCount: payload.hitCount,
  };
}

async function main() {
  const server = await startServer(PORT);
  const baseUrl = `http://127.0.0.1:${PORT}`;
  log(`static server ${baseUrl}`);

  const browser = await chromium.launch({
    channel: 'chrome',
    headless: !HEADED,
    args: HEADED ? [] : ['--use-angle=swiftshader', '--enable-unsafe-swiftshader'],
  });

  const results = [];
  try {
    for (const testCase of CASES) {
      results.push(await runCase(browser, baseUrl, testCase));
    }

    saveJson('score-e2e-summary.json', { finishedAt: new Date().toISOString(), results });
    log(`全部通过（${results.length} 局）`);
  } finally {
    await browser.close();
    await new Promise((resolve) => server.close(resolve));
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
