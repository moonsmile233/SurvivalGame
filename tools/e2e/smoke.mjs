// v1.2 H5 薄壳主流程 smoke。
//
// 覆盖：加载 → Shell（开始可用/全屏隐藏/昵称可编辑）→ EnterGame → 画布菜单开局 →
//       Esc 暂停 → 画布继续 → 短局结算（H5 数据栏 + Unity 结算卡）→ 再来一局 → 暂停返回菜单 →
//       刷新（最高分/昵称保留、最近一局清空）→ console 无未预期 error。
//
// 前提：web/unity 是 v1.2 构建（protocol 3 / 1.2.0）。用 ?perf=1&roundDuration=2 把一局压到 2 秒；
// ?testhook=1 暴露 SurvivalApp.getState/getSentMessages 供断言。线上跑请给 E2E_PROXY。
import { chromium, firefox } from 'playwright';
import { HEADED, chromiumLaunchOptions } from './launch.mjs';
import {
  startServer,
  saveScreenshot,
  saveJson,
  assert,
} from './standalone-common.mjs';
import { getLayout } from './ui-layout.mjs';

const BASE_URL = (process.env.BASE_URL || '').trim();
const BROWSER = (process.env.BROWSER || 'chrome').toLowerCase();
const E2E_PROXY = (process.env.E2E_PROXY || '').trim();
const PORT = Number(process.env.PORT || 8123);
const MOBILE = process.env.MOBILE === '1';
const READY_TIMEOUT = Number(process.env.READY_TIMEOUT || 180000);
const RESULT_TIMEOUT = Number(process.env.RESULT_TIMEOUT || 300000);
const NICKNAME = process.env.NICKNAME || 'E2E测试';
// 5s：给“Playing → Esc 暂停”留出真实机上的操作余量；无头下游戏时间更慢，只会更宽裕。
const ROUND_SECONDS = Number(process.env.ROUND_SECONDS || 5);

const targetTag = BASE_URL ? 'online' : 'local';
const tag = `${targetTag}-${BROWSER}-${MOBILE ? 'mobile' : 'desktop'}`;
const consoleErrors = [];
const steps = [];

function log(message) {
  console.log(message);
}

function ok(message) {
  steps.push(message);
  log('PASS  ' + message);
}

async function hint(page) {
  return (await page.textContent('#status-hint')) || '';
}

async function waitHint(page, text, timeout = 60000) {
  await page.waitForFunction(
    (needle) => (document.getElementById('status-hint')?.textContent || '').includes(needle),
    text,
    { timeout },
  );
}

async function waitPage(page, name, timeout = 30000) {
  await page.waitForFunction(
    (expected) => window.SurvivalApp?.getState?.().page === expected,
    name,
    { timeout },
  );
}

async function waitReady(page) {
  await page.waitForFunction(
    () => window.SurvivalApp?.getState?.().ready === true,
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

async function clickCanvas(page, point) {
  const box = await page.evaluate(() => {
    const rect = document.querySelector('#unity-canvas').getBoundingClientRect();
    return { left: rect.left, top: rect.top, width: rect.width, height: rect.height };
  });
  await page.mouse.click(box.left + point.fx * box.width, box.top + point.fy * box.height);
}

async function canvasLayout(page) {
  const box = await page.evaluate(() => {
    const rect = document.querySelector('#unity-canvas').getBoundingClientRect();
    return { width: rect.width, height: rect.height };
  });
  return getLayout(box.width, box.height);
}

async function main() {
  let server = null;
  let browser = null;
  let page = null;

  try {
    let baseUrl = BASE_URL;
    if (!baseUrl) {
      server = await startServer(PORT);
      baseUrl = `http://127.0.0.1:${PORT}`;
      log(`本地静态服务：${baseUrl}`);
    } else {
      log(`目标地址：${baseUrl}`);
    }

    const launchProxy = E2E_PROXY ? { proxy: { server: E2E_PROXY } } : {};
    if (BROWSER === 'firefox') {
      browser = await firefox.launch({ headless: !HEADED, ...launchProxy });
    } else {
      browser = await chromium.launch(
        chromiumLaunchOptions({
          channel: BROWSER === 'edge' ? 'msedge' : 'chrome',
          ...launchProxy,
        }),
      );
    }

    const context = await browser.newContext({
      viewport: MOBILE ? { width: 390, height: 844 } : { width: 1280, height: 800 },
      deviceScaleFactor: MOBILE ? 2 : 1,
      hasTouch: MOBILE,
      isMobile: MOBILE && BROWSER !== 'firefox',
    });

    page = await context.newPage();
    page.on('console', (message) => {
      if (message.type() === 'error') {
        consoleErrors.push('[console] ' + message.text());
      }
    });
    page.on('pageerror', (error) => consoleErrors.push('[pageerror] ' + error.message));

    const url = `${baseUrl.replace(/\/+$/, '')}/index.html?testhook=1&perf=1&roundDuration=${ROUND_SECONDS}`;
    log(`打开 ${url}`);
    await page.goto(url, { waitUntil: 'domcontentloaded' });
    await waitReady(page);

    // ---------- Shell ----------
    await waitPage(page, 'Shell');
    assert(await page.$eval('#btn-start', (el) => !el.disabled), '开始按钮在 onReady 后可用');
    assert(await page.$eval('#btn-fullscreen', (el) => el.hidden), 'Shell 隐藏全屏按钮');
    assert(!(await page.$eval('#nickname', (el) => el.disabled)), 'Shell 昵称可编辑');
    assert((await page.textContent('#overlay-text')).includes('游戏已就绪'), '遮罩显示「游戏已就绪」');
    assert(await page.$eval('#overlay', (el) => !el.hidden), 'Shell 遮罩仍挡住画布');
    ok('Shell：开始可用 / 全屏隐藏 / 昵称可编辑 / 遮罩挡住画布');
    await saveScreenshot(page, `smoke-${tag}-shell.png`);

    // 薄壳：旧 H5 的局内控件必须不存在。
    const staleControls = await page.locator('#result, #btn-restart, .difficulty-card').count();
    assert(staleControls === 0, '页面不得残留旧 H5 局内控件，实际 ' + staleControls);
    ok('薄壳：无旧 H5 结算卡/重开/难度卡 DOM');

    // ---------- 进入 ----------
    await page.fill('#nickname', NICKNAME);
    await page.click('#btn-start');
    await waitPage(page, 'Entered');
    assert(await page.$eval('#overlay', (el) => el.hidden), 'EnterGame 后遮罩隐藏');
    assert(await page.$eval('#btn-start', (el) => el.hidden), '进入后开始按钮隐藏');
    assert(!(await page.$eval('#btn-fullscreen', (el) => el.hidden)), '进入后显示全屏按钮');
    assert(await page.$eval('#nickname', (el) => el.disabled), '进入后昵称只读');
    const sent = await sentMessages(page);
    assert(sent.filter((m) => m.method === 'EnterGame').length === 1, '只发一次 EnterGame');
    for (const method of ['StartGame', 'PauseGame', 'ResumeGame', 'RestartGame', 'ReturnToMenu']) {
      assert(!sent.some((m) => m.method === method), '不得发送旧指令 ' + method);
    }
    ok('Entered：遮罩隐藏 / 开始隐藏 / 全屏出现 / 昵称只读 / 只发一次 EnterGame');
    await saveScreenshot(page, `smoke-${tag}-entered.png`);

    // ---------- 画布菜单开局 ----------
    let layout = await canvasLayout(page);
    await clickCanvas(page, layout.menuStart);
    await waitHint(page, '游戏中', 60000);
    ok('画布菜单「开始游戏」→ 3-2-1 → Playing');
    await saveScreenshot(page, `smoke-${tag}-playing.png`);

    // ---------- 暂停 / 继续 ----------
    await page.keyboard.press('Escape');
    await waitHint(page, '已暂停', 30000);
    layout = await canvasLayout(page);
    await clickCanvas(page, layout.pauseResume);
    await waitHint(page, '游戏中', 60000);
    ok('Esc 暂停 → 画布「继续」→ 3-2-1 → Playing');

    // ---------- 结算 ----------
    await page.waitForFunction(() => window.SurvivalApp?.getState?.().page === 'GameOver', null, {
      timeout: RESULT_TIMEOUT,
    });
    const afterRound = await appState(page);
    const last = (await page.textContent('#data-last')) || '';
    assert(last.trim() !== '—', 'H5 数据栏显示最近一局');
    assert(/^(简单|普通|困难) \d+$/.test(last.trim()), '最近一局格式应为「难度 分数」，实际 ' + last);
    assert(afterRound.best > 0, '最高分已写入，实际 ' + afterRound.best);
    ok(`结算：最近一局「${last.trim()}」/ 最高分 ${afterRound.best}`);
    await saveScreenshot(page, `smoke-${tag}-gameover.png`);

    // ---------- 再来一局 → 暂停返回菜单 ----------
    layout = await canvasLayout(page);
    await clickCanvas(page, layout.resultAgain);
    await waitHint(page, '游戏中', 60000);
    await page.keyboard.press('Escape');
    await waitHint(page, '已暂停', 30000);
    layout = await canvasLayout(page);
    await clickCanvas(page, layout.pauseMenu);
    await waitHint(page, '游戏菜单', 30000);
    ok('再来一局 → Playing → 暂停 → 返回菜单');

    // ---------- 刷新 ----------
    const bestBeforeReload = afterRound.best;
    await page.reload({ waitUntil: 'domcontentloaded' });
    await waitReady(page);
    await waitPage(page, 'Shell');
    const reloaded = await appState(page);
    assert(reloaded.best === bestBeforeReload, '刷新后最高分保留');
    assert((await page.textContent('#data-last')).trim() === '—', '刷新后最近一局清空');
    assert((await page.inputValue('#nickname')) === NICKNAME, '刷新后昵称保留');
    ok('刷新：最高分/昵称保留，最近一局清空');
    await saveScreenshot(page, `smoke-${tag}-reloaded.png`);

    // ---------- console ----------
    const noise = /INVALID_ENUM|GL_INVALID_OPERATION|favicon|AudioContext|WebGL: CONTEXT_LOST|UnityCache/i;
    const realErrors = consoleErrors.filter((line) => !noise.test(line));
    assert(realErrors.length === 0, 'console 无未预期 error：' + realErrors.join(' | '));
    ok('console 无未预期 error');

    saveJson(`smoke-${tag}-summary.json`, {
      browser: BROWSER,
      mobile: MOBILE,
      baseUrl,
      roundSeconds: ROUND_SECONDS,
      finishedAt: new Date().toISOString(),
      best: reloaded.best,
      lastRound: last.trim(),
      steps,
      consoleErrors,
    });
    log('\n全部通过');
  } catch (error) {
    if (page) {
      try {
        await saveScreenshot(page, `smoke-${tag}-failure.png`);
        const body = await page.locator('body').innerText();
        log('失败现场：docs/evidence/m6-refactor/smoke-' + tag + '-failure.png');
        log('--- body ---\n' + body.slice(0, 2000));
      } catch (snapshotError) {
        log('失败现场记录失败：' + snapshotError);
      }
    }
    throw error;
  } finally {
    if (browser) await browser.close();
    if (server) await new Promise((resolve) => server.close(resolve));
  }
}

main().catch((error) => {
  console.error('[smoke] 运行异常：', error);
  process.exitCode = 1;
});
