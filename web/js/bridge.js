(function () {
  'use strict';

  // 必须在 Unity 加载之前注册：Unity 的 .jslib 会直接查这个对象。
  window.SurvivalGameBridge = {
    onReady: function () {
      if (window.SurvivalApp) window.SurvivalApp.onReady();
    },
    onPong: function (message) {
      if (window.SurvivalApp) window.SurvivalApp.onPong(message);
    }
  };

  function app() {
    return window.SurvivalApp || null;
  }

  function fail(message) {
    console.error('[Bridge]', message);
    if (app()) app().onError(message);
  }

  function loadScript(src) {
    return new Promise(function (resolve, reject) {
      var script = document.createElement('script');
      script.src = src;
      script.onload = function () { resolve(); };
      script.onerror = function () { reject(new Error('loader 加载失败：' + src)); };
      document.body.appendChild(script);
    });
  }

  function boot() {
    var canvas = document.getElementById('unity-canvas');
    if (!canvas) {
      fail('页面缺少 #unity-canvas');
      return;
    }

    fetch('unity/build-info.json', { cache: 'no-store' })
      .then(function (response) {
        if (!response.ok) throw new Error('build-info.json HTTP ' + response.status);
        return response.json();
      })
      .then(function (info) {
        if (app() && app().setVersion) app().setVersion(info);

        var config = {
          dataUrl: 'unity/' + info.dataUrl,
          frameworkUrl: 'unity/' + info.frameworkUrl,
          codeUrl: 'unity/' + info.codeUrl,
          companyName: info.companyName,
          productName: info.productName,
          productVersion: info.productVersion
        };
        if (info.streamingAssetsUrl) {
          config.streamingAssetsUrl = 'unity/' + info.streamingAssetsUrl;
        }

        return loadScript('unity/' + info.loaderUrl).then(function () {
          if (typeof createUnityInstance !== 'function') {
            throw new Error('createUnityInstance 未定义（loader 未生效）');
          }
          return createUnityInstance(canvas, config, function (progress) {
            if (app() && app().onProgress) app().onProgress(progress);
          });
        }).then(function (instance) {
          if (app()) app().unityInstance = instance;
        });
      })
      .catch(function (error) {
        fail(error && error.message ? error.message : String(error));
      });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();
