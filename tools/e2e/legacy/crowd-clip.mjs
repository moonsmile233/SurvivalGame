// M6 拥挤手感重做：桌面证据录屏 + 指标（40 只）。
// 场景：
//   idle  玩家站定，40 只围住后录 10 秒（invincible=1，避免站着被打死）
//   pass  玩家用键盘以 3.2 u/s 横穿人堆（验证不被挡、不被推；playerPeakUps ≈3.2）
//   edge  玩家贴地图边缘站定 10 秒（验证边界没有堆叠/回收抖动）
//   hit   普通 URL（无 invincible）：站定等接触伤害 → 高速横穿再确认一次伤害（验证停距 0.70 仍稳定接触）
//
// 用法：
//   node crowd-clip.mjs                  # 四个场景全跑（约 5 分钟）
//   SCENES=idle node crowd-clip.mjs      # 只跑一个/几个
//   BASE_URL=http://127.0.0.1:8000 node crowd-clip.mjs
//
// 说明：Playwright 的 recordVideo 从 context 创建录到关闭，所以视频包含加载/填满阶段；
// 关键片段在结尾（cap 到顶后），指标里记录了各窗口的 t，方便定位。没有 ffmpeg，不做裁剪。
import { chromium } from 'playwright';
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');
const webRoot = path.join(repoRoot, 'web');
const evidenceDir = path.join(repoRoot, 'docs', 'evidence');
const videoTmpDir = path.join(evidenceDir, '.video-tmp');

const BASE_URL = (process.env.BASE_URL || '').trim();
const PORT = Number(process.env.PORT || 8123);
const BROWSER = (process.env.BROWSER || 'chrome').toLowerCase();
const CAP = Number(process.env.CAP || 40);
const ROUND_SECONDS = Number(process.env.ROUND_SECONDS || 600);
const SCENES = (process.env.SCENES || 'idle,pass,edge,hit')
  .split(',')
  .map((value) => value.trim())
  .filter(Boolean);

const READY_TIMEOUT = Number(process.env.READY_TIMEOUT || 180000);
const CAP_TIMEOUT = Number(process.env.CAP_TIMEOUT || 120000);

const PERF_ARGS = [
  '--disable-backgrounding-occluded-windows',
  '--disable-renderer-backgrounding',
  '--disable-background-timer-throttling',
  '--disable-features=CalculateNativeWinOcclusion',
  '--window-position=-32000,-32000',
];

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.otf': 'font/otf',
  '.wasm': 'application/wasm',
  '.unityweb': 'application/octet-stream',
  '.webm': 'video/webm',
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

  return new Promise((resolve) => {
    server.listen(PORT, '127.0.0.1', () => resolve(server));
  });
}

async function readBuildInfo(baseUrl) {
  try {
    const response = await fetch(new URL('/unity/build-info.json', baseUrl));
    return response.ok ? await response.json() : null;
  } catch {
    return null;
  }
}

function safeJson(text) {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function round3(value) {
  return Math.round(value * 1000) / 1000;
}

/** 等秒数；每秒检查一次 H5 状态，若被失焦暂停就按 P 恢复（3-2-1 约 4.5s）。 */
async function waitSeconds(page, seconds) {
  let remaining = seconds;
  while (remaining > 0) {
    await page.waitForTimeout(1000);
    remaining -= 1;
    const hint = await page.textContent('#status-hint').catch(() => '');
    if (hint && hint.includes('已暂停')) {
      await page.keyboard.press('p');
      await page.waitForTimeout(4500);
    }
  }
}

async function waitForCap(page, windows) {
  const deadline = Date.now() + CAP_TIMEOUT;
  while (Date.now() < deadline) {
    const latest = windows[windows.length - 1];
    if (latest && latest.alive >= CAP) {
      return true;
    }

    await page.waitForTimeout(1000);
    const hint = await page.textContent('#status-hint').catch(() => '');
    if (hint && hint.includes('已暂停')) {
      await page.keyboard.press('p');
      await page.waitForTimeout(4500);
    }
  }

  return false;
}

async function runScene(browser, baseUrl, scene, buildInfo) {
  const query = new URLSearchParams({
    perf: '1',
    enemyLimit: String(CAP),
    roundDuration: String(ROUND_SECONDS),
    perfSeconds: '600',
  });
  if (scene !== 'hit') {
    query.set('invincible', '1');
  }

  const url = `${baseUrl}/?${query.toString()}`;
  const recordDir = path.join(videoTmpDir, scene);
  fs.mkdirSync(recordDir, { recursive: true });

  const context = await browser.newContext({
    viewport: { width: 1280, height: 800 },
    deviceScaleFactor: 1,
    recordVideo: { dir: recordDir, size: { width: 1280, height: 800 } },
  });
  const page = await context.newPage();
  const video = page.video();

  const crowdLines = [];
  const windows = [];
  const damageLines = [];
  const consoleErrors = [];

  page.on('console', (message) => {
    const text = message.text();
    if (message.type() === 'error') {
      consoleErrors.push(`[console] ${text}`);
    }
    if (text.startsWith('[PERF-CROWD] ')) {
      crowdLines.push(safeJson(text.slice('[PERF-CROWD] '.length)));
    } else if (text.startsWith('[PERF] ') && text.includes('"t":')) {
      windows.push(safeJson(text.slice('[PERF] '.length)));
    } else if (text.includes('[PlayerHealth] damage=')) {
      damageLines.push(text);
    }
  });
  page.on('pageerror', (error) => consoleErrors.push(`[pageerror] ${error}`));

  console.log(`\n=== ${scene} ===`);
  console.log(`URL: ${url}`);
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('#btn-start:not([disabled])', { timeout: READY_TIMEOUT });
  await page.bringToFront();
  await page.fill('#nickname', `crowd-${scene}`);
  await page.click('#btn-start');
  await page.waitForFunction(
    () => (document.getElementById('status-hint')?.textContent || '').includes('游戏中'),
    null,
    { timeout: 20000 },
  );

  let capReached = false;
  if (scene !== 'hit') {
    capReached = await waitForCap(page, windows);
    console.log(`cap ${CAP}：${capReached ? '已到顶' : '未在时限内到顶'}`);
    await waitSeconds(page, 5); // 让站位/挤动稳定下来再录

    if (scene === 'pass' || scene === 'edge') {
      const holdSeconds = scene === 'edge' ? 6.0 : 1.8;
      await page.bringToFront();
      await page.keyboard.down('d');
      await page.waitForTimeout(Math.round(holdSeconds * 1000));
      await page.keyboard.up('d');
      if (scene === 'edge') {
        // 到地图边缘后等敌人重新围上来（旧的一批会被视野外回收）。
        await waitSeconds(page, 15);
      }
    }
  } else {
    // 接触伤害专项：不无敌，站着等第一次掉血（约 20~40 秒），再高速横穿确认还能再吃到一次。
    const start = Date.now();
    while (damageLines.length === 0 && Date.now() - start < 60000) {
      await page.waitForTimeout(1000);
      const hint = await page.textContent('#status-hint').catch(() => '');
      if (hint && hint.includes('已暂停')) {
        await page.keyboard.press('p');
        await page.waitForTimeout(4500);
      }
    }

    console.log(`首次掉血：${damageLines.length > 0 ? '有' : '60 秒内没有'}`);
    await page.waitForTimeout(1500);
    await page.keyboard.down('d');
    await page.waitForTimeout(1800);
    await page.keyboard.up('d');
    await page.waitForTimeout(1500);
  }

  if (scene === 'idle') {
    await page.screenshot({ path: path.join(evidenceDir, `m6-crowd-${CAP}-idle-a.png`) });
    await waitSeconds(page, 5);
  } else {
    await waitSeconds(page, 2);
  }

  // pass/hit 的挤动窗口可能还没结束：等一条新的 [PERF-CROWD]（最多 12 秒），
  // 这样 metrics.json 里才有玩家速度峰值/横向位移。
  if (scene === 'pass' || scene === 'hit') {
    const before = crowdLines.length;
    const deadline = Date.now() + 12000;
    while (crowdLines.length === before && Date.now() < deadline) {
      await page.waitForTimeout(500);
    }
  }

  await page.screenshot({ path: path.join(evidenceDir, `m6-crowd-${CAP}-${scene}-end.png`) });
  if (scene === 'idle') {
    // 10 秒窗口的最后一段：再等 5 秒截第二张，交给人工看“有没有动”。
    await waitSeconds(page, 5);
    await page.screenshot({ path: path.join(evidenceDir, `m6-crowd-${CAP}-idle-b.png`) });
  }

  await page.close();
  await context.close();

  let videoPath = null;
  try {
    const source = await video.path();
    const target = path.join(evidenceDir, `m6-crowd-${CAP}-${scene}.webm`);
    fs.rmSync(target, { force: true });
    fs.renameSync(source, target);
    videoPath = path.relative(repoRoot, target).split(path.sep).join('/');
  } catch (error) {
    console.log(`      ⚠ 视频保存失败：${error.message}`);
  }

  const last = crowdLines[crowdLines.length - 1] || {};
  const startPosition = crowdLines.length > 0
    ? { x: crowdLines[0].playerX, y: crowdLines[0].playerY }
    : null;
  const endPosition = last.playerX !== undefined ? { x: last.playerX, y: last.playerY } : null;
  // [PERF] 的 2 秒窗口也带玩家峰值速度：即使玩家中途死亡、10 秒挤动窗口没来得及结算，也有证据。
  const playerPeakFromWindows = windows.reduce((max, row) => Math.max(max, row.playerUps || 0), 0);

  const record = {
    scene,
    url,
    build: buildInfo,
    capReached,
    crowd: crowdLines,
    windows,
    damageLines,
    consoleErrors,
    playerStart: startPosition,
    playerEnd: endPosition,
    video: videoPath,
    screenshots: fs.existsSync(path.join(evidenceDir, `m6-crowd-${CAP}-${scene}-end.png`))
      ? `docs/evidence/m6-crowd-${CAP}-${scene}-end.png`
      : null,
    summary: {
      blockedN: last.blockedN ?? null,
      blockedRatio: last.blockedRatio ?? null,
      unblockedN: last.unblockedN ?? null,
      unblockedNetMax: last.unblockedNetMax !== undefined ? round3(last.unblockedNetMax) : null,
      yieldN: last.yieldN ?? null,
      yieldNetMax: last.yieldNetMax !== undefined ? round3(last.yieldNetMax) : null,
      maxFrameDisp: last.maxFrameDisp ?? null,
      minPairDist: last.minPairDist ?? null,
      swaps: last.swaps ?? null,
      playerPeakUps: Math.max(last.playerPeakUps ?? 0, playerPeakFromWindows),
      playerEnd: endPosition,
      damageCount: damageLines.length,
    },
  };

  console.log(
    `结果 ${scene}: minPair=${record.summary.minPairDist ?? '-'}  maxFrameDisp=${record.summary.maxFrameDisp ?? '-'}  ` +
      `blockedRatio=${record.summary.blockedRatio ?? '-'}  unblockedNetMax=${record.summary.unblockedNetMax ?? '-'}  ` +
      `swaps=${record.summary.swaps ?? '-'}  playerPeak=${record.summary.playerPeakUps ?? '-'}  ` +
      `damage=${record.summary.damageCount}`,
  );
  return record;
}

async function main() {
  let server = null;
  let browser = null;

  try {
    let baseUrl = BASE_URL;
    if (!baseUrl) {
      server = await startServer();
      baseUrl = `http://127.0.0.1:${PORT}`;
      console.log(`本地静态服务：${baseUrl}（root=${webRoot}）`);
    } else {
      console.log(`目标地址：${baseUrl}`);
    }

    const buildInfo = await readBuildInfo(baseUrl);
    console.log(`构建：${JSON.stringify(buildInfo)}`);

    browser = await chromium.launch({
      channel: BROWSER === 'edge' ? 'msedge' : 'chrome',
      headless: false,
      args: PERF_ARGS,
    });

    const records = [];
    for (const scene of SCENES) {
      records.push(await runScene(browser, baseUrl, scene, buildInfo));
    }

    fs.mkdirSync(evidenceDir, { recursive: true });
    const jsonPath = path.join(evidenceDir, `m6-crowd-${CAP}-metrics.json`);
    fs.writeFileSync(jsonPath, JSON.stringify({ build: buildInfo, cap: CAP, scenes: records }, null, 2));
    console.log(`\n指标 → ${path.relative(repoRoot, jsonPath).split(path.sep).join('/')}`);
    process.exitCode = 0;
  } finally {
    if (browser) {
      await browser.close();
    }
    if (server) {
      await new Promise((resolve) => server.close(resolve));
    }
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
