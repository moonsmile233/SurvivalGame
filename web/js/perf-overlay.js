// M6 压测用页面叠层：只在 URL 同时带 ?perf=1&overlay=1 时生效，普通玩家长什么样都不变。
// 目的：真机（Honor 90 Pro 等）看不到浏览器 console，把 Unity 的 [PERF]/[PERF-SUMMARY] 行
// 拦截下来，用大号 HTML 叠层显示 FPS / p95 / 同屏数量 / 分配 / 内存，直接截图即可当证据。
//
// 注意：必须在 bridge.js 加载 unity.loader.js 之前执行（index.html 里放在 bridge.js 前面），
// 否则拿不到 Unity 的 console 输出。
(function () {
  'use strict';

  var params = new URLSearchParams(window.location.search);
  if (params.get('perf') !== '1' || params.get('overlay') !== '1') {
    return;
  }

  var box = document.createElement('div');
  box.style.cssText = [
    'position:fixed',
    'left:8px',
    'top:8px',
    'z-index:2147483647',
    'background:rgba(0,0,0,.78)',
    'color:#6f6',
    'padding:10px 12px',
    'font:600 15px/1.55 ui-monospace,SFMono-Regular,Consolas,monospace',
    'white-space:pre',
    'border-radius:6px',
    'pointer-events:none',
    'text-shadow:0 0 2px rgba(0,0,0,.9)',
    'max-width:94vw'
  ].join(';');
  box.textContent = 'PERF 等待 Unity 采样…';
  document.body.appendChild(box);

  var last = null;
  var summary = null;

  function render() {
    var lines = [];
    if (last) {
      lines.push('t=' + last.t + 's   alive ' + last.alive + '   gc ' + last.gc);
      lines.push('fps ' + last.fps + '   p95 ' + last.p95 + 'ms   max ' + last.max + 'ms');
      lines.push('alloc ' + last.alloc + ' KB/s');
      lines.push('heap ' + last.heap + ' MB   wasm ' + last.wasm + ' MB');
    } else {
      lines.push('等待采样…（进游戏后才有）');
    }

    if (summary) {
      lines.push('--- DONE 可截图 ---');
      lines.push('limit ' + summary.limit + '  peak ' + summary.peakAlive + '  capAt ' + summary.capAt + 's');
      lines.push('稳态 ' + summary.steadySec + 's: fps ' + summary.steadyFps
        + '  p95 ' + summary.steadyP95ms + 'ms  p99 ' + summary.steadyP99ms + 'ms');
      lines.push('稳态 alloc ' + summary.steadyAllocKBps + ' KB/s  wasm ' + summary.wasmMB + ' MB');
    }

    box.textContent = lines.join('\n');
  }

  function handle(text) {
    var line = String(text);
    if (line.indexOf('[PERF] ') === 0) {
      try {
        var w = JSON.parse(line.slice(7));
        last = {
          t: w.t,
          alive: w.alive,
          fps: w.fps,
          p95: w.p95ms,
          max: w.maxms,
          alloc: w.allocKBps,
          heap: (w.heapKB / 1024).toFixed(1),
          wasm: w.wasmMB,
          gc: w.gc
        };
        render();
      } catch (error) {
        // 被截断/格式变化时忽略，不影响游戏。
      }
    } else if (line.indexOf('[PERF-SUMMARY] ') === 0) {
      try {
        summary = JSON.parse(line.slice(15));
        render();
      } catch (error) {
        // 同上
      }
    }
  }

  var originalLog = console.log;
  console.log = function () {
    try {
      if (arguments.length > 0) {
        handle(arguments[0]);
      }
    } catch (error) {
      // 叠层异常不能影响游戏
    }
    return originalLog.apply(console, arguments);
  };
})();
