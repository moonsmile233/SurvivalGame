mergeInto(LibraryManager.library, {
  // Unity → H5：场景首帧就绪。C# string 到 JS 是 UTF8 指针。
  JsNotifyReady: function (ptr) {
    try {
      var json = UTF8ToString(ptr);
      if (window.SurvivalGameBridge && typeof window.SurvivalGameBridge.onReady === 'function') {
        window.SurvivalGameBridge.onReady(json);
      } else {
        console.warn('[Bridge] onReady: 页面未注册 window.SurvivalGameBridge.onReady');
      }
    } catch (e) {
      console.error('[Bridge] onReady failed', e);
    }
  },

  // Unity → H5：状态变化（调试 / H5 驱动 UI）
  JsNotifyStateChange: function (ptr) {
    try {
      var json = UTF8ToString(ptr);
      if (window.SurvivalGameBridge && typeof window.SurvivalGameBridge.onStateChange === 'function') {
        window.SurvivalGameBridge.onStateChange(json);
      } else {
        console.warn('[Bridge] onStateChange: 页面未注册 window.SurvivalGameBridge.onStateChange');
      }
    } catch (e) {
      console.error('[Bridge] onStateChange failed', e);
    }
  },

  // Unity → H5：一局结束（Unity 侧每局只发一次）
  JsNotifyGameOver: function (ptr) {
    try {
      var json = UTF8ToString(ptr);
      if (window.SurvivalGameBridge && typeof window.SurvivalGameBridge.onGameOver === 'function') {
        window.SurvivalGameBridge.onGameOver(json);
      } else {
        console.warn('[Bridge] onGameOver: 页面未注册 window.SurvivalGameBridge.onGameOver');
      }
    } catch (e) {
      console.error('[Bridge] onGameOver failed', e);
    }
  },

  // 只给压测探针（PerfProbe，?perf=1）用：WASM 线性内存字节数。
  // Unity 托管堆就在这块内存里，手机上这是最早撞到的内存上限；取不到就返回 0，不影响其他指标。
  JsGetWasmHeapBytes: function () {
    try {
      return typeof HEAPU8 !== 'undefined' && HEAPU8 ? HEAPU8.length : 0;
    } catch (e) {
      return 0;
    }
  }
});
