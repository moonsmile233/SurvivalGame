// M6 §7.2 三档难度在真实构建里的端到端验证。
// 需要 web/unity 是带 protocol v2 的构建（H5 用 ?testhook=1 直接发原始载荷）。
//
// 覆盖：
//  A. hard 站立不动 → 死亡；console 出现 damage=14；onGameOver.difficulty=hard；
//     分数 = floor(存活秒) × 10 × 1.6（±1 秒误差）；H5 结算卡显示「困难」
//  B. hard + ?perf=1&invincible=1 站满 60 秒 → 胜利，分数 1160（600×1.6+200）
//  C. v1 载荷（只有昵称）→ Unity 按 normal 开局，onGameOver.difficulty=normal
//  D. 非法 difficulty=bogus → normal + bridge warning
//
// 用法：node difficulty-e2e.mjs
// 证据：docs/evidence/m6-difficulty-*.png / m6-difficulty-e2e.log
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
const PORT = Number(process.env.PORT || 8127);
const BASE_URL = (process.env.BASE_URL || `http://127.0.0.1:${PORT}`).replace(/\/$/, '');
const READY_TIMEOUT = Number(process.env.READY_TIMEOUT || 180000);
const RESULT_TIMEOUT = Number(process.env.RESULT_TIMEOUT || 180000);

const failures = [];
const consoleText = [];

function check(ok, message) {
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${message}`);
  if (!ok) failures.push(message);
}

const evidence = (name) => {
  fs.mkdirSync(evidenceDir, { recursive: true });
  return path.join(evidenceDir, name);
};

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

async function openPage(context, query, tag) {
  const page = await context.newPage();
  page.on('console', (message) => {
    if (message.type() === 'error' || message.type() === 'warning' || message.type() === 'log') {
      consoleText.push(`[${tag}] ${message.text()}`);
    }
  });
  page.on('pageerror', (error) => consoleText.push(`[${tag}] pageerror ${error}`));

  await page.goto(`${BASE_URL}/${query}`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('#btn-start:not([disabled])', { timeout: READY_TIMEOUT });
  await page.waitForTimeout(200);

  // 包一层 onGameOver，留存 Unity 回传的原始载荷（bridge.js 每次事件都从 window.SurvivalApp 取函数）
  await page.evaluate(() => {
    window.__gameOver = null;
    const original = window.SurvivalApp.onGameOver;
    window.SurvivalApp.onGameOver = function (json) {
      window.__gameOver = json;
      return original(json);
    };
  });

  return page;
}

async function waitResult(page, timeout = RESULT_TIMEOUT) {
  await page.waitForSelector('#result:not([hidden])', { timeout });
  await page.waitForTimeout(300);
  const raw = await page.evaluate(() => window.__gameOver);
  const payload = raw ? JSON.parse(raw).payload : null;
  return payload;
}

async function main() {
  const server = await startServer();
  const browser = await chromium.launch(chromiumLaunchOptions());
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 }, deviceScaleFactor: 1 });

  try {
    // ---------- A: hard 站立不动，伤害与倍率 ----------
    console.log('\n[A] hard 死亡：damage=14 / difficulty=hard / 分数倍率');
    {
      const page = await openPage(context, '', 'A');
      await page.locator('.difficulty-card').nth(2).click();
      await page.click('#btn-start');
      const payload = await waitResult(page);
      const text = await page.evaluate(() => ({
        difficulty: document.querySelector('#result-difficulty')?.textContent,
        best: document.querySelector('#result-best')?.textContent,
        storedHard: window.localStorage.getItem('survival.bestScore.hard'),
        storedNormal: window.localStorage.getItem('survival.bestScore.normal'),
      }));

      check(!!payload, 'A 收到 onGameOver 载荷');
      check(payload?.difficulty === 'hard', `A onGameOver.difficulty=hard（${payload?.difficulty}）`);
      check(payload?.result === 'lose', `A 站立不动应当死亡（${payload?.result}）`);

      const expected = Math.floor(payload?.survivedSeconds || 0) * 10 * 1.6;
      check(
        Math.abs((payload?.score || 0) - expected) <= 16,
        `A 分数按困难倍率结算（score ${payload?.score} ≈ floor(${payload?.survivedSeconds})×16=${Math.round(expected)}）`,
      );
      check(/damage=14/.test(consoleText.join('\n')), 'A console 出现 damage=14（困难伤害倍率生效）');
      check(text.difficulty === '困难', `A 结算卡显示困难（${text.difficulty}）`);
      check(text.storedHard === String(payload?.score), `A 困难最高分写入 bestScore.hard（${text.storedHard}）`);
      check(text.storedNormal === null, `A 不写普通档（normal=${text.storedNormal}）`);

      await page.screenshot({ path: evidence('m6-difficulty-hard-lose.png') });
      console.log('      截图 → docs/evidence/m6-difficulty-hard-lose.png');
      await page.close();
    }

    // ---------- B: hard + 无敌站满 60 秒 = 1160 ----------
    console.log('\n[B] hard 60 秒胜利 = 1160');
    {
      const page = await openPage(context, '?perf=1&invincible=1', 'B');
      await page.locator('.difficulty-card').nth(2).click();
      await page.click('#btn-start');
      const payload = await waitResult(page, 150000);

      check(payload?.result === 'win', `B 60 秒判定胜利（${payload?.result}）`);
      check(payload?.survivedSeconds === 60, `B 存活 60 秒（${payload?.survivedSeconds}）`);
      check(payload?.difficulty === 'hard', `B difficulty=hard（${payload?.difficulty}）`);
      check(payload?.score === 1160, `B 分数 1160 = 600×1.6+200（${payload?.score}）`);
      check(payload?.breakdown?.survival === 960, `B breakdown.survival=960（${payload?.breakdown?.survival}）`);
      check(payload?.breakdown?.winBonus === 200, `B winBonus=200 不乘倍率（${payload?.breakdown?.winBonus}）`);

      await page.screenshot({ path: evidence('m6-difficulty-hard-win.png') });
      console.log('      截图 → docs/evidence/m6-difficulty-hard-win.png');
      await page.close();
    }

    // ---------- C: v1 载荷（只有昵称）→ normal ----------
    console.log('\n[C] v1 载荷（纯昵称）→ normal');
    {
      const page = await openPage(context, '?testhook=1&perf=1&invincible=1&roundDuration=5', 'C');
      await page.evaluate(() => window.SurvivalApp.__sendMessage('StartGame', '{"nickname":"旧协议"}'));
      const payload = await waitResult(page, 60000);

      check(payload?.difficulty === 'normal', `C 缺 difficulty → normal（${payload?.difficulty}）`);
      check(payload?.result === 'win', `C 5 秒压测回合正常结束（${payload?.result}）`);
      await page.close();
    }

    // ---------- D: 非法 difficulty → normal + warning ----------
    console.log('\n[D] 非法 difficulty → normal');
    {
      const page = await openPage(context, '?testhook=1&perf=1&invincible=1&roundDuration=5', 'D');
      await page.evaluate(() =>
        window.SurvivalApp.__sendMessage('StartGame', '{"nickname":"非法","difficulty":"bogus"}'),
      );
      const payload = await waitResult(page, 60000);

      check(payload?.difficulty === 'normal', `D 非法值 → normal（${payload?.difficulty}）`);
      check(/未知难度 'bogus'/.test(consoleText.join('\n')), 'D bridge 打出未知难度 warning');
      await page.close();
    }

    // ---------- E/F: 暂停/结算 → 返回主菜单 → 换档 → 开局 ----------
    console.log('\n[E] 暂停 → 返回主菜单 → 换档 → 开局（中止不计分）');
    {
      // 5 秒回合 + 无敌：暂停、返回、换档都能在几秒内跑完
      const page = await openPage(context, '?perf=1&invincible=1&roundDuration=5', 'EF');
      await page.evaluate(() => {
        window.__gameOverCount = 0;
        const original = window.SurvivalApp.onGameOver;
        window.SurvivalApp.onGameOver = function (json) {
          window.__gameOverCount += 1;
          return original(json);
        };
      });

      // 普通档开一局，立刻暂停
      await page.locator('.difficulty-card').nth(1).click();
      await page.click('#btn-start');
      await page.waitForFunction(
        () => (document.querySelector('#status-hint')?.textContent || '').includes('游戏中'),
        null,
        { timeout: 30000 },
      );
      await page.keyboard.press('p');
      await page.waitForFunction(
        () => (document.querySelector('#status-hint')?.textContent || '').includes('已暂停'),
        null,
        { timeout: 10000 },
      );
      check(await page.isVisible('#btn-pause-menu'), 'E Paused 时「返回主菜单」可见');

      await page.click('#btn-pause-menu');
      await page.waitForFunction(() => document.body.dataset.page === 'Ready', null, { timeout: 15000 });
      await page.waitForTimeout(400);

      const afterReturn = await page.evaluate(() => ({
        menuHidden: document.querySelector('#difficulty')?.hidden,
        startHidden: document.querySelector('#btn-start')?.hidden,
        gameOverCount: window.__gameOverCount,
        normalBest: window.localStorage.getItem('survival.bestScore.normal'),
        frame: (() => {
          const r = document.querySelector('.game-frame')?.getBoundingClientRect();
          return r ? { w: r.width, h: r.height } : null;
        })(),
      }));
      check(afterReturn.menuHidden === false, 'E 返回后难度区可见');
      check(afterReturn.startHidden === false, 'E 返回后开始按钮可见');
      check(afterReturn.gameOverCount === 0, `E 中止局没有 onGameOver（${afterReturn.gameOverCount}）`);
      check(afterReturn.normalBest === null, `E 中止局不写最高分（normal=${afterReturn.normalBest}）`);

      await page.screenshot({ path: evidence('m6-return-menu-ready.png') });
      console.log('      截图 → docs/evidence/m6-return-menu-ready.png');

      // 换到简单档再开一局
      await page.locator('.difficulty-card').nth(0).click();
      await page.click('#btn-start');
      const easyPayload = await waitResult(page, 60000);
      const easyState = await page.evaluate(() => ({
        difficulty: document.querySelector('#result-difficulty')?.textContent,
        easyBest: window.localStorage.getItem('survival.bestScore.easy'),
        normalBest: window.localStorage.getItem('survival.bestScore.normal'),
      }));
      check(easyPayload?.difficulty === 'easy', `E 换档后开局 difficulty=easy（${easyPayload?.difficulty}）`);
      check(easyPayload?.score === 250, `E 简单 5 秒胜利 = 50×1.0+200（${easyPayload?.score}）`);
      check(easyState.difficulty === '简单', `E 结算卡显示简单（${easyState.difficulty}）`);
      check(easyState.easyBest === '250', `E 简单档最高分写入（${easyState.easyBest}）`);
      check(easyState.normalBest === null, `E 普通档仍未被中止局污染（${easyState.normalBest}）`);

      console.log('\n[F] Result → 返回主菜单 → 换档 → 开局 → 暂停/3-2-1');
      check(await page.isVisible('#btn-result-menu'), 'F 结算卡里「返回主菜单」可见');
      await page.click('#btn-result-menu');
      await page.waitForFunction(() => document.body.dataset.page === 'Ready', null, { timeout: 15000 });
      await page.waitForTimeout(300);
      check(await page.isVisible('#difficulty'), 'F 结算返回后难度区可见');

      // 换困难 → 开局 → 暂停 → P 触发 3-2-1 → 恢复
      await page.locator('.difficulty-card').nth(2).click();
      await page.click('#btn-start');
      await page.waitForFunction(
        () => (document.querySelector('#status-hint')?.textContent || '').includes('游戏中'),
        null,
        { timeout: 30000 },
      );
      await page.keyboard.press('p');
      await page.waitForFunction(
        () => (document.querySelector('#status-hint')?.textContent || '').includes('已暂停'),
        null,
        { timeout: 10000 },
      );
      await page.keyboard.press('p');
      await page.waitForTimeout(4200);
      const resumedPage = await page.evaluate(() => document.body.dataset.page);
      check(resumedPage === 'Playing', `F 返回菜单后 3-2-1 仍能恢复（${resumedPage}）`);

      const hardPayload = await waitResult(page, 60000);
      const hardState = await page.evaluate(() => ({
        difficulty: document.querySelector('#result-difficulty')?.textContent,
        hardBest: window.localStorage.getItem('survival.bestScore.hard'),
        easyBest: window.localStorage.getItem('survival.bestScore.easy'),
      }));
      check(hardPayload?.difficulty === 'hard', `F 换困难后 difficulty=hard（${hardPayload?.difficulty}）`);
      check(hardPayload?.score === 280, `F 困难 5 秒胜利 = 50×1.6+200（${hardPayload?.score}）`);
      check(hardState.difficulty === '困难', `F 结算卡显示困难（${hardState.difficulty}）`);
      check(hardState.hardBest === '280', `F 困难档最高分写入（${hardState.hardBest}）`);
      check(hardState.easyBest === '250', `F 简单档最高分保持（${hardState.easyBest}）`);

      await page.screenshot({ path: evidence('m6-return-menu-hard-result.png') });
      console.log('      截图 → docs/evidence/m6-return-menu-hard-result.png');
      await page.close();
    }

    fs.writeFileSync(evidence('m6-difficulty-e2e.log'), consoleText.join('\n'));
    console.log('\nconsole 原文 → docs/evidence/m6-difficulty-e2e.log');
  } finally {
    await browser.close();
    server.close();
  }

  console.log(failures.length === 0 ? '\n全部通过' : `\n${failures.length} 项失败`);
  process.exit(failures.length === 0 ? 0 : 1);
}

main().catch((error) => {
  console.error('[difficulty-e2e] 运行异常：', error);
  fs.writeFileSync(evidence('m6-difficulty-e2e.log'), `${consoleText.join('\n')}\n\n${error?.stack || error}`);
  process.exit(1);
});
