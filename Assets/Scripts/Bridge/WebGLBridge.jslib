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
  }
});
