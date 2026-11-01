// M6 §3.4 压测：敌人上限 20/30/40 × 桌面 Chrome（真 GPU、有头窗口）。
// 只测「现状」：不改玩法，靠 Unity 侧的 PerfProbe（?perf=1）覆盖上限、延长回合、让玩家不受伤，
// 采集 2 秒窗口的 fps/p95/GC 分配/WASM 内存，结束时打 [PERF-SUMMARY]。
//
// 为什么必须有头：无头 Chrome 走 SwiftShader 软件渲染，帧率不能代表真机；窗口若被遮挡，
// Chrome 会节流，所以这里关掉 occlusion 相关特性，并把窗口移到屏幕外（不打断使用者）。
//
// 用法：
//   node perf.mjs                       # 20,30,40，连续跑
//   CAPS=20 node perf.mjs               # 只跑一个
//   BASE_URL=http://192.168.1.100:8000 node perf.mjs   # 打已有静态服务（默认自己起 8123）
//   PERF_SECONDS=90 node perf.mjs       # 每组采集时长（含填满阶段）
import { chromium } from 'playwright';
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');
const webRoot = path.join(repoRoot, 'web');
const evidenceDir = path.join(repoRoot, 'docs', 'evidence');

const BASE_URL = (process.env.BASE_URL || '').trim();
const PORT = Number(process.env.PORT || 8123);
const BROWSER = (process.env.BROWSER || 'chrome').toLowerCase();
const CAPS = (process.env.CAPS || '20,30,40')
  .split(',')
  .map((value) => Number(value.trim()))
  .filter((value) => Number.isFinite(value) && value > 0);
const PERF_SECONDS = Number(process.env.PERF_SECONDS || 90);
// 同一参数重复跑（比如对比屏幕内/屏幕外）时加个标签，避免后一次覆盖前一次的 JSON。
const RUN_LABEL = (process.env.RUN_LABEL || '').trim();
// 额外 URL 参数（例如 overlay=1 验证真机叠层），格式同 query string。
const EXTRA_QUERY = (process.env.EXTRA_QUERY || '').trim();
const ROUND_SECONDS = Number(process.env.ROUND_SECONDS || 600);
const READY_TIMEOUT = Number(process.env.READY_TIMEOUT || 180000);
const SUMMARY_TIMEOUT = Number(process.env.SUMMARY_TIMEOUT || 240000);

// 帧率采样比 2 秒窗口更细没必要；keep 一致性：所有上限用同一套参数。
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

// 禁止后台/遮挡节流：性能结论必须来自真实渲染，否则数字无效。
// 不禁 v-sync：玩家看到的就是 vsync 后的帧率，测出来的 p95 才有意义。
// 窗口放到屏幕外（与 tools/e2e/launch.mjs 的 HEADED 约定一致）：用户在用电脑时不能弹窗打扰。
// 屏幕外可能被 Chrome 判成遮挡 → 必须用同一 cap 对比屏幕内数字，掉得多就停，不要退回可见窗口。
const PERF_ARGS = [
  '--disable-backgrounding-occluded-windows',
  '--disable-renderer-backgrounding',
  '--disable-background-timer-throttling',
  '--disable-features=CalculateNativeWinOcclusion',
  '--window-position=-32000,-32000',
];

function parseConsoleLine(text) {
  if (text.startsWith('[PERF-SUMMARY] ')) {
    return { kind: 'summary', data: safeJson(text.slice('[PERF-SUMMARY] '.length)) };
  }
  if (text.startsWith('[PERF] ') && text.includes('"t":')) {
    return { kind: 'window', data: safeJson(text.slice('[PERF] '.length)) };
  }
  return null;
}

function safeJson(text) {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

function round1(value) {
  return Math.round(value * 10) / 10;
}

async function readBuildInfo(baseUrl) {
  try {
    const response = await fetch(new URL('/unity/build-info.json', baseUrl));
    return response.ok ? await response.json() : null;
  } catch {
    return null;
  }
}

async function runCap(browser, baseUrl, cap, buildInfo) {
  const query = new URLSearchParams({
    perf: '1',
    enemyLimit: String(cap),
    roundDuration: String(ROUND_SECONDS),
    invincible: '1',
    perfSeconds: String(PERF_SECONDS),
  });
  if (EXTRA_QUERY) {
    new URLSearchParams(EXTRA_QUERY).forEach((value, key) => query.set(key, value));
  }
  const url = `${baseUrl}/?${query.toString()}`;

  const context = await browser.newContext({
    viewport: { width: 1280, height: 800 },
    deviceScaleFactor: 1,
  });
  const page = await context.newPage();

  const windows = [];
  const consoleErrors = [];
  let summary = null;
  let lastPerfAt = Date.now();

  page.on('console', (message) => {
    const text = message.text();
    if (message.type() === 'error') {
      consoleErrors.push(`[console] ${text}`);
    }
    const parsed = parseConsoleLine(text);
    if (!parsed) {
      return;
    }
    lastPerfAt = Date.now();
    if (parsed.kind === 'summary' && parsed.data) {
      summary = parsed.data;
    } else if (parsed.kind === 'window' && parsed.data) {
      windows.push(parsed.data);
    }
  });
  page.on('pageerror', (error) => consoleErrors.push(`[pageerror] ${error}`));

  console.log(`\n=== cap ${cap} ===`);
  console.log(`URL: ${url}`);
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('#btn-start:not([disabled])', { timeout: READY_TIMEOUT });
  await page.bringToFront();

  await page.fill('#nickname', `perf${cap}`);
  await page.click('#btn-start');
  await page.waitForFunction(
    () => (document.getElementById('status-hint')?.textContent || '').includes('游戏中'),
    null,
    { timeout: 20000 },
  );

  const startedAt = new Date();
  console.log(`开始采样：${startedAt.toISOString()}（约 ${PERF_SECONDS}s，窗口在屏幕外）`);

  // 看门狗：Unity 只有在 Playing 才采样。窗口被抢焦点会暂停（H5 设计如此），这里重新聚焦。
  const deadline = Date.now() + SUMMARY_TIMEOUT;
  while (!summary && Date.now() < deadline) {
    await page.waitForTimeout(1000);
    if (Date.now() - lastPerfAt > 6000) {
      console.log('      ⚠ 6 秒没有新采样（可能被失焦暂停），重新聚焦');
      await page.bringToFront();
      const hint = await page.textContent('#status-hint').catch(() => '');
      if (hint && hint.includes('已暂停')) {
        // 暂停恢复必须由玩家主动触发：桌面按 P，再走 3-2-1。
        await page.keyboard.press('p');
        await page.waitForTimeout(4500);
      }
      lastPerfAt = Date.now();
    }
  }

  const finishedAt = new Date();
  const suffix = RUN_LABEL ? `-${RUN_LABEL}` : '';
  const screenshotName = `perf-local-${BROWSER}-desktop-cap${cap}${suffix}.png`;
  const screenshotPath = path.join(evidenceDir, screenshotName);
  fs.mkdirSync(evidenceDir, { recursive: true });
  await page.screenshot({ path: screenshotPath });

  // 拥挤验收（M6 2026-11-01 重做后）：再等 5 秒拍第二张。敌群应该持续有小幅挤动，
  // **不再要求两帧像素一致**；这两张只作为“有没有动/有没有整块跳变”的辅助证据，
  // 主判定看 [PERF-CROWD] 的 lateralOver03 / swaps / minPairDist。
  await page.waitForTimeout(5000);
  const screenshotBName = `perf-local-${BROWSER}-desktop-cap${cap}${suffix}-b.png`;
  const screenshotBPath = path.join(evidenceDir, screenshotBName);
  await page.screenshot({ path: screenshotBPath });

  // 窗口序列里最后 20 秒（到顶后）作为独立复算，避免只信 Unity 侧 summary。
  const capAt = summary?.capAt ?? null;
  const tail = windows.filter((row) => row.t > (summary?.t ?? 0) - 20);
  const steadyFps = tail.length
    ? round1(tail.reduce((sum, row) => sum + row.fps, 0) / tail.length)
    : null;
  const steadyP95 = tail.length
    ? round1(tail.reduce((sum, row) => sum + row.p95ms, 0) / tail.length)
    : null;

  const record = {
    target: BASE_URL || 'local',
    browser: BROWSER,
    device: 'desktop',
    cap,
    url,
    build: buildInfo,
    startedAt: startedAt.toISOString(),
    finishedAt: finishedAt.toISOString(),
    durationSeconds: Math.round((finishedAt - startedAt) / 100) / 10,
    capAt,
    windows,
    summary,
    tail20s: { samples: tail.length, avgFps: steadyFps, avgP95ms: steadyP95 },
    consoleErrors,
    screenshot: path.relative(repoRoot, screenshotPath).split(path.sep).join('/'),
    screenshotB: path.relative(repoRoot, screenshotBPath).split(path.sep).join('/'),
  };

  const jsonPath = path.join(evidenceDir, `perf-local-${BROWSER}-desktop-cap${cap}${suffix}.json`);
  fs.writeFileSync(jsonPath, JSON.stringify(record, null, 2));

  const s = summary || {};
  console.log(
    `结果 cap=${cap}: 到顶 ${s.capAt ?? '-'}s  ` +
      `稳态 ${s.steadyFps ?? '-'} FPS / p95 ${s.steadyP95ms ?? '-'}ms / p99 ${s.steadyP99ms ?? '-'}ms / ` +
      `alloc ${s.steadyAllocKBps ?? '-'} KB/s / heap ${((s.heapKB ?? 0) / 1024).toFixed(1)}MB / ` +
      `wasm ${s.wasmMB ?? '-'}MB / 群体位移 ${s.steadyMotionUps ?? '-'} u/s（前环 ${s.steadySettled ?? '-'}）`,
  );
  console.log(`      证据 → docs/evidence/perf-local-${BROWSER}-desktop-cap${cap}${suffix}.json`);

  await context.close();
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
      headless: false, // 必须真渲染；无头 SwiftShader 的数据不算
      args: PERF_ARGS,
    });

    const records = [];
    for (const cap of CAPS) {
      records.push(await runCap(browser, baseUrl, cap, buildInfo));
    }

    const lines = records.map((record) => {
      const s = record.summary || {};
      return [
        record.cap,
        s.capAt ?? '-',
        s.steadyAvgAlive ?? '-',
        s.steadyFps ?? '-',
        s.steadyP95ms ?? '-',
        s.steadyMaxMs ?? '-',
        s.steadyAllocKBps ?? '-',
        s.totalAllocMB ?? '-',
        ((s.heapKB ?? 0) / 1024).toFixed(1),
        s.wasmMB ?? '-',
        s.steadyMotionUps ?? '-',
      ].join(' | ');
    });
    console.log('\n上限 | 到顶s | 稳态敌人数 | FPS | p95ms | maxms | allocKB/s | 总分配MB | heapMB | wasmMB | 挤动u/s');
    for (const line of lines) {
      console.log(line);
    }

    const ok = records.every((record) => record.summary && record.summary.capReached);
    if (!ok) {
      console.log('\n⚠ 有上限没在采集期内摸到，看 [PERF] 序列或调大 PERF_SECONDS。');
    }
    process.exitCode = ok ? 0 : 2;
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
