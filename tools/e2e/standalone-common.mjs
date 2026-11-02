// Shared helpers for the v1.2 standalone (no-H5) E2E runs.
//
// Unity WebGL has no DOM buttons, so the tests click the canvas by fractional coordinates
// derived from Assets/Editor/SceneSetup.cs (see ui-layout.mjs) and assert on the events the
// Unity bridge pushes into window.__survivalEvents (the standalone page does not load
// web/js/bridge.js, so the test registers the bridge itself before any page script runs).
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
export const repoRoot = path.resolve(here, '..', '..');
export const webRoot = path.join(repoRoot, 'web');
export const evidenceDir = path.join(repoRoot, 'docs', 'evidence', 'm6-refactor');

const CONTENT_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'application/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.wasm': 'application/wasm',
  '.unityweb': 'application/octet-stream',
  '.png': 'image/png',
};

export function ensureEvidenceDir() {
  fs.mkdirSync(evidenceDir, { recursive: true });
}

/** Minimal static server for web/ (no-store, so a rebuilt Unity package is always picked up). */
export function startServer(port) {
  const server = http.createServer((req, res) => {
    const url = new URL(req.url, 'http://127.0.0.1');
    let filePath = path.join(webRoot, decodeURIComponent(url.pathname));
    if (!filePath.startsWith(webRoot)) {
      res.writeHead(403).end('forbidden');
      return;
    }
    if (url.pathname.endsWith('/')) {
      filePath = path.join(filePath, 'index.html');
    }
    fs.readFile(filePath, (error, data) => {
      if (error) {
        res.writeHead(404).end('not found');
        return;
      }
      res.writeHead(200, {
        'Content-Type': CONTENT_TYPES[path.extname(filePath).toLowerCase()] || 'application/octet-stream',
        'Cache-Control': 'no-store',
      });
      res.end(data);
    });
  });

  return new Promise((resolve) => {
    server.listen(port, '127.0.0.1', () => resolve(server));
  });
}

/** Registers the bridge + fills the viewport with the canvas before Unity boots. */
export async function prepareContext(context) {
  await context.addInitScript(() => {
    window.__survivalEvents = [];
    const push = (type, json) => {
      let payload = null;
      try {
        payload = JSON.parse(json);
      } catch (error) {
        payload = { parseError: String(error), raw: json };
      }
      window.__survivalEvents.push({ type, payload, at: Date.now() });
    };
    window.SurvivalGameBridge = {
      onReady: (json) => push('ready', json),
      onStateChange: (json) => push('stateChange', json),
      onGameOver: (json) => push('gameOver', json),
      onDifficultyChanged: (json) => push('difficultyChanged', json),
    };

    document.addEventListener('DOMContentLoaded', () => {
      // Standalone has no H5 shell; constrain the canvas to the delivery 16:9 stage so the
      // 6-viewport matrix exercises the same layout the embedded page uses (M4.7 baseline).
      // Do not use a full-window portrait canvas here: that is not a delivery target and makes
      // the HUD overlap for reasons unrelated to this phase.
      const style = document.createElement('style');
      style.textContent =
        'html,body{margin:0;height:100%;overflow:hidden;background:#05050a}' +
        '#unity-canvas{width:min(100vw,177.7778vh)!important;height:min(100vh,56.25vw)!important;' +
        'position:fixed;left:50%;top:50%;transform:translate(-50%,-50%);display:block}' +
        // The Unity template's logo/footer/fullscreen overlays are not part of the game (the H5
        // shell has no such chrome). Hide them so screenshots show only the game canvas.
        '#unity-footer,#unity-logo,#unity-logo-title-footer,#unity-build-title,#unity-fullscreen-button{display:none!important}';
      document.head.appendChild(style);
    });
  });
}

export async function openGame(page, baseUrl, query = '') {
  const url = `${baseUrl}/unity/index.html${query}`;
  await page.goto(url, { waitUntil: 'load' });
  await page.waitForFunction(() => (window.__survivalEvents || []).some((e) => e.type === 'ready'), null, {
    timeout: Number(process.env.READY_TIMEOUT || 180000),
  });
  await page.waitForTimeout(500);
}

export function layoutFor(page) {
  return page.evaluate(() => {
    const canvas = document.querySelector('#unity-canvas');
    const rect = canvas.getBoundingClientRect();
    return { width: rect.width, height: rect.height, left: rect.left, top: rect.top };
  });
}

export async function clickFraction(page, point) {
  const rect = await layoutFor(page);
  await page.mouse.click(rect.left + point.fx * rect.width, rect.top + point.fy * rect.height);
}

export async function waitForState(page, state, timeout = 120000) {
  // Check the LATEST state event, not "any in history": the same script pauses/resumes/restarts
  // several times, and a stale `playing` would make the next click race the 3-2-1 countdown.
  await page.waitForFunction(
    (name) => {
      const events = window.__survivalEvents || [];
      for (let i = events.length - 1; i >= 0; i -= 1) {
        if (events[i].type === 'stateChange') {
          return Boolean(events[i].payload) && events[i].payload.state === name;
        }
      }
      return false;
    },
    state,
    { timeout },
  );
}

export async function waitForDifficulty(page, difficulty, timeout = 10000) {
  await page.waitForFunction(
    (name) => (window.__survivalEvents || []).some((e) => e.type === 'difficultyChanged' && e.payload && e.payload.difficulty === name),
    difficulty,
    { timeout },
  );
}

export async function waitForGameOver(page, timeout = 240000) {
  await page.waitForFunction(
    () => (window.__survivalEvents || []).some((e) => e.type === 'gameOver'),
    null,
    { timeout },
  );
  return page.evaluate(() => window.__survivalEvents.find((e) => e.type === 'gameOver').payload);
}

/** Number of onGameOver events so far (the flow plays more than one round). */
export async function gameOverCount(page) {
  return page.evaluate(() => (window.__survivalEvents || []).filter((e) => e.type === 'gameOver').length);
}

/** Waits for a gameOver event newer than `previousCount` (history search would match round 1). */
export async function waitForGameOverAfter(page, previousCount, timeout = 300000) {
  await page.waitForFunction(
    (count) => (window.__survivalEvents || []).filter((e) => e.type === 'gameOver').length > count,
    previousCount,
    { timeout },
  );
  return page.evaluate(() => {
    const events = (window.__survivalEvents || []).filter((e) => e.type === 'gameOver');
    return events[events.length - 1].payload;
  });
}

export async function readEvents(page) {
  return page.evaluate(() => window.__survivalEvents || []);
}

export async function saveScreenshot(page, name) {
  ensureEvidenceDir();
  const target = path.join(evidenceDir, name);
  await page.screenshot({ path: target });
  return target;
}

export function saveJson(name, value) {
  ensureEvidenceDir();
  const target = path.join(evidenceDir, name);
  fs.writeFileSync(target, JSON.stringify(value, null, 2));
  return target;
}

export function assert(condition, message) {
  if (!condition) {
    throw new Error('ASSERT: ' + message);
  }
}
