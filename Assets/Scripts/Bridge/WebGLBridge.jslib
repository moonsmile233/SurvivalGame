mergeInto(LibraryManager.library, {
  // Unity → H5：场景初始化完成
  JsNotifyReady: function () {
    try {
      if (window.SurvivalGameBridge && window.SurvivalGameBridge.onReady) {
        window.SurvivalGameBridge.onReady();
      }
    } catch (e) {
      console.error('[Bridge] onReady failed', e);
    }
  },

  // Unity → H5：回显 H5 发来的消息（C# string 到 JS 是 UTF8 指针）
  JsNotifyPong: function (ptr) {
    try {
      var msg = UTF8ToString(ptr);
      if (window.SurvivalGameBridge && window.SurvivalGameBridge.onPong) {
        window.SurvivalGameBridge.onPong(msg);
      }
    } catch (e) {
      console.error('[Bridge] onPong failed', e);
    }
  }
});
