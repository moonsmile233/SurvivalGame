// M4 E2E smoke：加载 → 昵称 → 开始 → 失焦暂停 → 3-2-1 恢复 → 结算 → 重开 → 刷新最高分。
// 用真实浏览器窗口切换制造 blur/focus（不是改 document.hidden 模拟），见 tools/e2e/README.md。
import { chromium, firefox } from 'playwright';
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');
const webRoot = path.join(repoRoot, 'web');
const evidenceDir = path.join(repoRoot, 'docs', 'evidence');

const BASE_URL = (process.env.BASE_URL || '').trim();
const BROWSER = (process.env.BROWSER || 'chrome').toLowerCase();
// 线上跑国内直连 Netlify 很慢且大文件会被掐断（Chrome 默认不走系统代理时直接 ERR_CONNECTION_CLOSED）。
// 用 E2E_PROXY 显式给浏览器挂代理；只影响测试浏览器，不改游戏/H5 的网络行为。
const E2E_PROXY = (process.env.E2E_PROXY || '').trim();
const PORT = Number(process.env.PORT || 8123);
const HEADLESS = process.env.HEADLESS === '1';
const MOBILE = process.env.MOBILE === '1';
const READY_TIMEOUT = Number(process.env.READY_TIMEOUT || 180000);
const RESULT_TIMEOUT = Number(process.env.RESULT_TIMEOUT || 300000);
const NICKNAME = process.env.NICKNAME || 'E2E测试';
const ROUNDS = Number(process.env.ROUNDS || 3);
// AUTOMOVE=1：开始后让脚本按方形走位，用来跑满 60 秒（自动化胜利回归，不等同人工体验）。
const AUTOMOVE = process.env.AUTOMOVE === '1';

// 证据文件名带上目标（online / local），避免线上跑完后被本地跑覆盖、验收记录指向错误的图。
const targetTag = BASE_URL ? 'online' : 'local';
const tag = `${targetTag}-${BROWSER}-${MOBILE ? 'mobile' : 'desktop'}`;
const RUN_LABEL = process.env.RUN_LABEL || '';
const failures = [];
const consoleErrors = [];

function log(message) {
  console.log(message);
}

function evidence(name) {
  fs.mkdirSync(evidenceDir, { recursive: true });
  return path.join(evidenceDir, name);
}

function check(condition, message) {
  if (condition) {
    log(`PASS  ${message}`);
  } else {
    failures.push(message);
    log(`FAIL  ${message}`);
  }
}

// ---------- 本地静态服务（与线上 Netlify 行为一致：全部 no-store） ----------

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

// ---------- 工具 ----------

async function hintOf(page) {
  return page.evaluate(() => document.getElementById('status-hint')?.textContent || '');
}

async function waitForHint(page, text, timeout) {
  await page.waitForFunction(
    (expected) => (document.getElementById('status-hint')?.textContent || '').includes(expected),
    text,
    { timeout },
  );
}

async function screenshot(page, name) {
  await page.screenshot({ path: evidence(`e2e-${tag}-${name}.png`) });
  log(`      截图 → docs/evidence/e2e-${tag}-${name}.png`);
}

/**
 * 等结算出现。自动化跑的时候浏览器窗口可能被别的窗口抢走焦点，H5 会按设计自动暂停
 * （失焦 → Paused，不自己恢复），这里检测到「已暂停」就重新聚焦，让 3-2-1 走完再继续等。
 */
async function waitForResult(page, timeout) {
  const deadline = Date.now() + timeout;

  while (Date.now() < deadline) {
    if (await page.isVisible('#result')) {
      return true;
    }

    if ((await hintOf(page)).includes('已暂停')) {
      log('      检测到「已暂停」（窗口失焦），重新聚焦等待倒计时');
      await page.bringToFront();
      await page.waitForTimeout(4500);
      continue;
    }

    await page.waitForTimeout(400);
  }

  return false;
}

function startAutoMove(page) {
  // 大矩形巡逻：沿长边跑到边、再换向，尽量把敌人甩在身后（玩家 3.2 > 敌人 2.2）。
  // 八方向小圈巡逻（每段 1.8s）：不做 180° 掉头，避免在角落被追上；
  // 地图 36×20，绕圈半径约 7 单位，始终朝远离追击方向移动。
  const legs = [
    ['ArrowRight'],
    ['ArrowRight', 'ArrowDown'],
    ['ArrowDown'],
    ['ArrowDown', 'ArrowLeft'],
    ['ArrowLeft'],
    ['ArrowLeft', 'ArrowUp'],
    ['ArrowUp'],
    ['ArrowUp', 'ArrowRight'],
  ];
  const legMs = 1800;
  let stopped = false;
  let index = 0;

  const loop = (async () => {
    while (!stopped) {
      const keys = legs[index++ % legs.length];
      for (const key of keys) {
        await page.keyboard.down(key);
      }
      await page.waitForTimeout(legMs);
      for (const key of keys) {
        await page.keyboard.up(key);
      }
      await page.waitForTimeout(60);
    }
  })();

  return async () => {
    stopped = true;
    try {
      await loop;
    } catch {
      // 页面关闭/重载时按键循环会抛错，忽略即可。
    }
  };
}

async function touchDrag(page) {
  // 只有 Chromium 支持 CDP；用真实触摸事件拖动画面中心。
  const box = await page.locator('#unity-canvas').boundingBox();
  if (!box) {
    return false;
  }

  const client = await page.context().newCDPSession(page);
  const startX = Math.round(box.x + box.width / 2);
  const startY = Math.round(box.y + box.height / 2);

  await client.send('Input.dispatchTouchEvent', {
    type: 'touchStart',
    touchPoints: [{ x: startX, y: startY, id: 1 }],
  });

  for (let step = 1; step <= 12; step++) {
    await client.send('Input.dispatchTouchEvent', {
      type: 'touchMove',
      touchPoints: [{ x: startX + step * 8, y: startY, id: 1 }],
    });
    await page.waitForTimeout(25);
  }

  await client.send('Input.dispatchTouchEvent', { type: 'touchEnd', touchPoints: [] });
  return true;
}

// M4.6：暂停后只能由玩家主动恢复。桌面按 P；手机点 Unity 暂停面板的「继续」
// （按钮锚点 0.5/0.32，uGUI y 向上 → 画布内屏幕 y = 0.68；HUD 参考分辨率切换不影响比例）。
async function resumeFromPause(page) {
  if (MOBILE && BROWSER !== 'firefox') {
    const box = await page.locator('#unity-canvas').boundingBox();
    if (!box) {
      return false;
    }

    await page.touchscreen.tap(box.x + box.width * 0.5, box.y + box.height * 0.68);
    return true;
  }

  await page.keyboard.press('p');
  return true;
}

// ---------- 主流程 ----------

async function main() {
  let server = null;
  let browser = null;
  let page = null;

  try {
    let baseUrl = BASE_URL;
    if (!baseUrl) {
      server = await startServer();
      baseUrl = `http://127.0.0.1:${PORT}`;
      log(`本地静态服务：${baseUrl}（root=${webRoot}）`);
    } else {
      log(`目标地址：${baseUrl}`);
    }

    const launchProxy = E2E_PROXY ? { proxy: { server: E2E_PROXY } } : {};
    if (BROWSER === 'firefox') {
      browser = await firefox.launch({ headless: HEADLESS, ...launchProxy });
    } else {
      browser = await chromium.launch({
        headless: HEADLESS,
        channel: BROWSER === 'edge' ? 'msedge' : 'chrome',
        ...launchProxy,
      });
    }

    const context = await browser.newContext({
      viewport: MOBILE ? { width: 390, height: 844 } : { width: 1280, height: 800 },
      // 真机 DPR 是 2～3；Unity 侧 DPR 上限 2，HUD 阈值 Screen.width<1000 依赖这个比例。
      deviceScaleFactor: MOBILE ? 2 : 1,
      hasTouch: MOBILE,
      isMobile: MOBILE && BROWSER !== 'firefox',
    });

    page = await context.newPage();
    page.on('console', (message) => {
      if (message.type() === 'error') {
        consoleErrors.push(`[console] ${message.text()}`);
      }
    });
    page.on('pageerror', (error) => consoleErrors.push(`[pageerror] ${error}`));

    log(`\n[1/8] 打开页面等待 onReady（${BROWSER}${MOBILE ? ' / 390×844' : ''}）`);
    await page.goto(baseUrl, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('#btn-start:not([disabled])', { timeout: READY_TIMEOUT });
    check(await page.isVisible('#btn-start'), 'onReady 后开始按钮可见且可用');
    await screenshot(page, 'ready');

    // 昵称 + 开始
    log('\n[2/8] 填昵称并开始');
    await page.fill('#nickname', NICKNAME);
    if (MOBILE && BROWSER !== 'firefox') {
      await page.tap('#btn-start');
    } else {
      await page.click('#btn-start');
    }

    await waitForHint(page, '游戏中', 20000);
    check((await hintOf(page)).includes('游戏中'), 'H5 显示「游戏中」');
    check(await page.isHidden('#btn-start'), '开始按钮已隐藏');
    await page.waitForTimeout(800);
    await screenshot(page, 'playing');

    let stopAutoMove = null;
    if (AUTOMOVE) {
      if (ROUNDS > 1) {
        log('      AUTOMOVE 只用于第 1 局；建议同时设 ROUNDS=1');
      }
      log('      AUTOMOVE：方形走位（自动化，不等同人工体验）');
      stopAutoMove = startAutoMove(page);
    }

    if (MOBILE && BROWSER !== 'firefox') {
      log('\n[3/8] 触摸拖动（CDP 真实触摸事件）');
      const dragged = await touchDrag(page);
      check(dragged, '能在画布上完成一次触摸拖动');
      const scrollY = await page.evaluate(() => window.scrollY);
      check(scrollY === 0, `拖动时页面不滚动（scrollY=${scrollY}）`);
    } else {
      // M4 §1.7 + M4.6：桌面 Esc 暂停、P 主动恢复；恢复必须先走 3-2-1，不再立即 Playing。
      // 全屏下 Esc 可能被浏览器吞掉，P 的兜底靠人工在全屏里另测。
      // 键盘事件走 document 级监听，不经过 H5 绑定。
      log('\n[3/8] 桌面键盘 Esc 暂停 / P 恢复（3-2-1）');
      await page.keyboard.press('Escape');
      await waitForHint(page, '已暂停', 5000);
      check((await hintOf(page)).includes('已暂停'), '按 Esc 暂停后 H5 显示「已暂停」');
      await screenshot(page, 'hotkey-paused');

      await page.keyboard.press('p');
      await page.waitForTimeout(600);
      check((await hintOf(page)).includes('已暂停'), '按 P 后先停 3-2-1，H5 仍是「已暂停」');
      await screenshot(page, 'hotkey-countdown');

      // 倒计时中重复按 P 应被忽略（PausePanel.StartCountdown 在 _countingDown 时直接返回）。
      await page.keyboard.press('p');
      await page.waitForTimeout(300);
      check((await hintOf(page)).includes('已暂停'), '倒计时中再按 P 不会立刻恢复');

      await waitForHint(page, '游戏中', 20000);
      check((await hintOf(page)).includes('游戏中'), '3-2-1 结束后恢复「游戏中」');
      await screenshot(page, 'hotkey-resumed');
    }

    // 真实窗口失焦：另开一个浏览器窗口并聚焦它（不是改 document.hidden）。
    // Playwright 默认开启「焦点模拟」，document.hasFocus() 永远是 true、blur 不触发；
    // 这里对当前文档显式关掉（CDP），才能拿到真实的 window.blur。
    // Firefox 没有这个开关，失焦用例只在 Chrome/Edge 跑（人工门禁另测）。
    if (BROWSER === 'firefox') {
      log('\n[4/8] Firefox 跳过失焦自动化（Playwright 无焦点模拟开关）');
    } else {
      log('\n[4/8] 真实失焦 → 暂停 → 切回仍暂停 → 主动恢复 3-2-1');
      // 先把游戏窗口提到最前，否则窗口本来就没焦点，后面再开一个窗口也不会触发 blur。
      await page.bringToFront();
      await page.waitForTimeout(400);

      const cdp = await context.newCDPSession(page);
      await cdp.send('Emulation.setFocusEmulationEnabled', { enabled: false });

      const otherContext = await browser.newContext();
      const otherPage = await otherContext.newPage();
      await otherPage.goto('about:blank');
      await otherPage.bringToFront();

      await waitForHint(page, '已暂停', 15000);
      check((await hintOf(page)).includes('已暂停'), '失焦后 H5 显示「已暂停」');
      await screenshot(page, 'paused');

      await otherPage.close();
      await otherContext.close();
      await page.bringToFront();

      // M4.6：切回只清标记，保持 Paused，不自动倒数、不自动恢复。
      await page.waitForTimeout(1500);
      check((await hintOf(page)).includes('已暂停'), '切回后仍保持「已暂停」，不自动恢复');
      await screenshot(page, 'focus-return-paused');

      // 玩家主动恢复：桌面按 P、手机点 Unity 暂停面板的「继续」→ 3-2-1 → Playing。
      check(await resumeFromPause(page), '能触发玩家主动恢复');
      await page.waitForTimeout(800);
      await screenshot(page, 'countdown');

      await waitForHint(page, '游戏中', 20000);
      check((await hintOf(page)).includes('游戏中'), '主动恢复的 3-2-1 结束后进入「游戏中」');
      await screenshot(page, 'resumed');

      // 失焦用例已验证完，后面批量跑局时重新打开焦点模拟，避免自动化窗口被抢焦点导致整局暂停。
      await cdp.send('Emulation.setFocusEmulationEnabled', { enabled: true });
    }

    // 结算
    log('\n[5/8] 等待一局结束（不操作，约 20~40 秒）');
    if (!(await waitForResult(page, RESULT_TIMEOUT))) {
      throw new Error(`等待结算超时（${RESULT_TIMEOUT}ms）`);
    }
    // M4.6 §2：结算卡片顶部对齐（block:'start'），重新开始按钮要和明细同屏。
    await page.waitForTimeout(700);
    await screenshot(page, 'result');
    const restartBox = await page.locator('#btn-restart').boundingBox();
    const viewport = page.viewportSize();
    check(
      !!restartBox && !!viewport && restartBox.y >= 0 && restartBox.y + restartBox.height <= viewport.height,
      `结算后「重新开始」在首屏可见（y=${restartBox ? Math.round(restartBox.y) : 'null'}）`
    );

    if (stopAutoMove) {
      await stopAutoMove();
      stopAutoMove = null;
    }

    const title = (await page.textContent('#result-title'))?.trim() || '';
    const score = Number(await page.textContent('#result-score'));
    const best = Number(await page.textContent('#result-best'));
    const name = (await page.textContent('#result-nickname'))?.trim() || '';
    const survived = (await page.textContent('#result-survived'))?.trim() || '';

    check(['胜利', '失败'].includes(title), `结果标题正确（${title}）`);
    check(Number.isFinite(score) && score >= 0 && score <= 800, `本次得分合理（${score}）`);
    check(Number.isFinite(best) && best >= score, `最高分不低于本次（${best} >= ${score}）`);
    check(name === NICKNAME, `结算昵称是玩家昵称（${name}）`);
    check(/^\d+(\.\d+)? 秒$/.test(survived), `存活秒数格式正确（${survived}）`);
    check(await page.isHidden('#unity-canvas') === false, '游戏画布仍在页面上（H5 未替换画布）');

    // 连续重开（第 1 局已结束，再打 ROUNDS-1 局；每局只验证状态与分数不累加）。
    let maxScore = score;
    for (let round = 2; round <= ROUNDS; round++) {
      log(`\n[6/8] 第 ${round} 局：点「重新开始」`);
      if (MOBILE && BROWSER !== 'firefox') {
        await page.tap('#btn-restart');
      } else {
        await page.click('#btn-restart');
      }

      await waitForHint(page, '游戏中', 20000);
      check(await page.isHidden('#result'), `第 ${round} 局开始后结算卡片隐藏`);
      if (!(await waitForResult(page, RESULT_TIMEOUT))) {
        throw new Error(`第 ${round} 局等待结算超时（${RESULT_TIMEOUT}ms）`);
      }

      const roundScore = Number(await page.textContent('#result-score'));
      check(Number.isFinite(roundScore) && roundScore >= 0 && roundScore <= 800,
        `第 ${round} 局得分合理且未累加（${roundScore}）`);
      maxScore = Math.max(maxScore, roundScore);
    }

    await screenshot(page, `result-round${ROUNDS}`);

    // 刷新后昵称 / 最高分仍在
    log('\n[7/8] 刷新页面校验本地存储');
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.waitForSelector('#btn-start:not([disabled])', { timeout: READY_TIMEOUT });

    const savedNickname = await page.inputValue('#nickname');
    // 最高分走 web/js/storage.js 的封装（localStorage key 带 `survival.` 前缀），不要直接读裸 key。
    const savedBest = await page.evaluate(() => {
      try {
        return window.SurvivalStorage ? window.SurvivalStorage.getInt('bestScore', 0) : NaN;
      } catch {
        return NaN;
      }
    });

    check(savedNickname === NICKNAME, `刷新后昵称仍在（${savedNickname}）`);
    check(Number.isFinite(savedBest) && savedBest >= maxScore,
      `刷新后最高分仍在（${savedBest} >= ${maxScore}）`);

    // console 检查
    log('\n[8/8] console 检查');
    const noise = /INVALID_ENUM|GL_INVALID_OPERATION|favicon|AudioContext|WebGL: CONTEXT_LOST/i;
    const realErrors = consoleErrors.filter((line) => !noise.test(line));
    if (consoleErrors.length > 0) {
      fs.writeFileSync(evidence(`e2e-${tag}-console.log`), consoleErrors.join('\n'));
    }
    check(realErrors.length === 0, `console 无未预期的 error（${realErrors.length}）`);

    const summary = {
      browser: BROWSER,
      mobile: MOBILE,
      baseUrl,
      finishedAt: new Date().toISOString(),
      rounds: ROUNDS,
      maxScore,
      failures,
    };
    const summaryName = RUN_LABEL ? `e2e-${tag}-summary-${RUN_LABEL}.json` : `e2e-${tag}-summary.json`;
    fs.writeFileSync(evidence(summaryName), JSON.stringify(summary, null, 2));
  } catch (error) {
    // 失败现场：在浏览器还开着的时候截图 + 落 console/页面文本，方便判断是加载失败还是断言失败。
    if (page) {
      try {
        await page.screenshot({ path: evidence(`e2e-${tag}-failure.png`) });
        const bodyText = await page.locator('body').innerText();
        fs.writeFileSync(
          evidence(`e2e-${tag}-failure.log`),
          `${consoleErrors.join('\n')}\n\n--- body ---\n${bodyText.slice(0, 4000)}\n\n--- error ---\n${error?.stack || error}`,
        );
        log(`      失败现场 → docs/evidence/e2e-${tag}-failure.png / .log`);
      } catch (snapshotError) {
        log(`      失败现场记录失败：${snapshotError}`);
      }
    }
    throw error;
  } finally {
    if (browser) {
      await browser.close();
    }
    if (server) {
      await new Promise((resolve) => server.close(resolve));
    }
  }

  if (failures.length > 0) {
    log(`\n${failures.length} 项失败：`);
    for (const failure of failures) {
      log(`  - ${failure}`);
    }
    process.exitCode = 1;
  } else {
    log('\n全部通过');
  }
}

main().catch((error) => {
  console.error('[e2e] 运行异常：', error);
  process.exitCode = 1;
});
