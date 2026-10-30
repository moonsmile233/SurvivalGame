(function () {
  'use strict';

  // 必须在加载 unity.loader.js 之前注册：Unity 的 .jslib 会直接查这个对象（规范 7.2）。
  window.SurvivalGameBridge = {
    onReady: function (json) {
      forward('onReady', json);
    },
    onStateChange: function (json) {
      forward('onStateChange', json);
    },
    onGameOver: function (json) {
      forward('onGameOver', json);
    }
  };

  function app() {
    return window.SurvivalApp || null;
  }

  function forward(name, json) {
    var target = app();
    if (target && typeof target[name] === 'function') {
      target[name](json);
      return;
    }
    console.warn('[Bridge] SurvivalApp.' + name + ' 未注册，事件已丢弃：' + json);
  }

  function fail(message) {
    console.error('[Bridge]', message);
    var target = app();
    if (target && target.onFatalError) {
      target.onFatalError(message);
    }
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
    if (window.location.protocol === 'file:') {
      var target = app();
      if (target && target.onFileProtocol) {
        target.onFileProtocol();
      }
      return;
    }

    var canvas = document.getElementById('unity-canvas');
    if (!canvas) {
      fail('页面缺少 #unity-canvas');
      return;
    }

    fetch('unity/build-info.json', { cache: 'no-store' })
      .then(function (response) {
        if (!response.ok) {
          throw new Error('读取构建信息失败（HTTP ' + response.status + '）');
        }
        return response.json();
      })
      .then(function (info) {
        var current = app();
        if (current && current.setBuildInfo) current.setBuildInfo(info);

        var config = {
          dataUrl: 'unity/' + info.dataUrl,
          frameworkUrl: 'unity/' + info.frameworkUrl,
          codeUrl: 'unity/' + info.codeUrl,
          companyName: info.companyName,
          productName: info.productName,
          productVersion: info.productVersion,
          // 高分屏上限 2：手机 DPR 3 时按 2 渲染，平衡清晰度与 GPU；桌面上通常就是 1。
          // Unity 的 Canvas 尺寸与 Screen.width 都按这个比例走，HudCanvasScaler 的阈值依赖它。
          devicePixelRatio: Math.min(window.devicePixelRatio || 1, 2)
        };
        if (info.streamingAssetsUrl) {
          config.streamingAssetsUrl = 'unity/' + info.streamingAssetsUrl;
        }

        return loadScript('unity/' + info.loaderUrl).then(function () {
          if (typeof createUnityInstance !== 'function') {
            throw new Error('createUnityInstance 未定义（loader 未生效）');
          }
          return createUnityInstance(canvas, config, function (progress) {
            var instance = app();
            if (instance && instance.onProgress) instance.onProgress(progress);
          });
        }).then(function (instance) {
          var currentApp = app();
          if (currentApp && currentApp.setInstance) currentApp.setInstance(instance);
        });
      })
      .catch(function (error) {
        var message = error && error.message ? error.message : String(error);
        if (/failed to fetch|networkerror|load failed/i.test(message)) {
          message = '无法读取构建信息（网络错误），请确认 web/unity 已构建并可访问';
        }
        fail(message);
      });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();
