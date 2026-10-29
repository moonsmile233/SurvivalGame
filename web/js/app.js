(function () {
  'use strict';

  var overlay = document.getElementById('overlay');
  var overlayText = document.getElementById('overlay-text');
  var btnPing = document.getElementById('btn-ping');
  var bridgeLog = document.getElementById('bridge-log');
  var versionEl = document.getElementById('version');

  var app = {
    unityInstance: null,

    showOverlay: function (text) {
      if (!overlay) return;
      if (text === null) {
        overlay.hidden = true;
        return;
      }
      overlay.hidden = false;
      if (overlayText) overlayText.textContent = text;
    },

    onProgress: function (progress) {
      if (overlayText) overlayText.textContent = 'Loading... ' + Math.round(progress * 100) + '%';
    },

    onReady: function () {
      this.showOverlay('Unity Ready');
      if (btnPing) btnPing.disabled = false;
    },

    onPong: function (message) {
      if (bridgeLog) bridgeLog.textContent = 'Pong: ' + message;
    },

    onError: function (message) {
      this.showOverlay('加载失败：' + message);
      console.error('[App]', message);
    },

    ping: function () {
      if (!this.unityInstance || typeof this.unityInstance.SendMessage !== 'function') {
        console.warn('[App] unityInstance 尚未就绪，无法发送消息');
        return;
      }
      this.unityInstance.SendMessage('WebGLBridge', 'Ping', 'hello-from-h5');
    },

    setVersion: function (info) {
      if (!versionEl) return;
      var name = (info && info.productName) || 'SurvivalGame';
      var version = (info && info.productVersion) || '?';
      versionEl.textContent = name + ' v' + version;
    }
  };

  if (btnPing) {
    btnPing.addEventListener('click', function () {
      app.ping();
    });
  }

  window.SurvivalApp = app;
})();
