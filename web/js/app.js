(function () {
  'use strict';

  var DEFAULT_NICKNAME = '玩家';
  var MAX_NICKNAME = 16;
  var BEST_KEY = 'bestScore';
  var NICKNAME_KEY = 'nickname';

  // 点开始后的兜底时限。正常首帧响应 <100ms（SendMessage 同步）；9 秒只覆盖
  // 「指令真的丢了 / Unity 卡住」的情况，不能短到把慢机器上的正常启动误判。
  var START_RESPONSE_TIMEOUT_MS = 9000;

  var storage = window.SurvivalStorage;
  var overlay = document.getElementById('overlay');
  var overlayText = document.getElementById('overlay-text');
  var errorCard = document.getElementById('error-card');
  var errorText = document.getElementById('error-text');
  var btnRetry = document.getElementById('btn-retry');
  var btnStart = document.getElementById('btn-start');
  var btnRestart = document.getElementById('btn-restart');
  var nicknameInput = document.getElementById('nickname');
  var statusHint = document.getElementById('status-hint');
  var resultEl = document.getElementById('result');
  var resultTitle = document.getElementById('result-title');
  var resultScore = document.getElementById('result-score');
  var resultBest = document.getElementById('result-best');
  var resultNickname = document.getElementById('result-nickname');
  var resultSurvived = document.getElementById('result-survived');
  var resultRecord = document.getElementById('result-record');
  var versionEl = document.getElementById('version');
  var buildTimeEl = document.getElementById('build-time');

  var state = {
    page: 'Loading',
    ready: false,
    unityInstance: null,
    best: storage ? storage.getInt(BEST_KEY, 0) : 0,
    gameOverHandled: false,
    startTimeout: null
  };

  // ---------- 通用 ----------

  function showOverlay(text) {
    if (!overlay) return;
    overlay.hidden = false;
    if (overlayText) overlayText.textContent = text;
  }

  function hideOverlay() {
    if (overlay) overlay.hidden = true;
  }

  function setHint(text) {
    if (statusHint) statusHint.textContent = text || '';
  }

  function parseJson(json) {
    if (typeof json !== 'string' || json === '') {
      return null;
    }
    try {
      return JSON.parse(json);
    } catch (error) {
      console.warn('[App] JSON 解析失败，忽略该事件：' + error);
      return null;
    }
  }

  /** 昵称：去首尾空白、按码点截断到 16、空则「玩家」；渲染一律走 textContent。 */
  function normalizeNickname(value) {
    var name = String(value === null || value === undefined ? '' : value).replace(/\s+/g, ' ').trim();
    var chars = Array.from(name);
    if (chars.length > MAX_NICKNAME) {
      chars = chars.slice(0, MAX_NICKNAME);
    }
    return chars.length > 0 ? chars.join('') : DEFAULT_NICKNAME;
  }

  function renderButtons(page) {
    var showStart = page === 'Loading' || page === 'Ready' || page === 'Starting';
    btnStart.hidden = !showStart;
    btnStart.disabled = page !== 'Ready';

    btnRestart.hidden = page !== 'Result';
    btnRestart.disabled = page !== 'Result';

    nicknameInput.disabled = page !== 'Ready';
  }

  function setPage(page) {
    state.page = page;
    if (page === 'Playing') {
      resultEl.hidden = true;
    }
    renderButtons(page);
  }

  function sendMessage(method, arg) {
    var instance = state.unityInstance;
    if (!instance || typeof instance.SendMessage !== 'function') {
      console.warn('[App] unityInstance 未就绪，无法发送 ' + method);
      return false;
    }

    try {
      instance.SendMessage('WebGLBridge', method, arg || '');
      return true;
    } catch (error) {
      console.error('[App] SendMessage 失败：' + method, error);
      return false;
    }
  }

  function saveHintIfNeeded() {
    if (storage && !storage.isAvailable()) {
      setHint('当前浏览器无法保存成绩，刷新后会丢失');
    }
  }

  // ---------- 用户操作 ----------

  function startGame() {
    if (!state.ready || state.page !== 'Ready') {
      return;
    }

    var nickname = normalizeNickname(nicknameInput.value);
    nicknameInput.value = nickname;

    if (storage) {
      storage.set(NICKNAME_KEY, nickname);
      saveHintIfNeeded();
    }

    // 顺序不能反：Unity 的 SendMessage 是同步的，onStateChange(Playing) 会在
    // sendMessage() 返回前到达。若先发指令再设 Starting + 兜底计时器，就会把刚到的
    // Playing 覆盖回 Starting，兜底到期再把页面打回 Ready（游戏其实已在跑）。
    setHint('准备中…');
    setPage('Starting');

    // 兜底只提示、不改状态：Unity 可能已经在跑，只是状态回调没到。
    // 打回 Ready 会让玩家误以为没开始而重复点击；保持 Starting 即可（开始按钮禁用）。
    window.clearTimeout(state.startTimeout);
    state.startTimeout = window.setTimeout(function () {
      if (state.page === 'Starting') {
        setHint('启动较慢，若已在游戏中请直接玩；未开始请刷新');
      }
    }, START_RESPONSE_TIMEOUT_MS);

    if (!sendMessage('StartGame', JSON.stringify({ nickname: nickname }))) {
      window.clearTimeout(state.startTimeout);
      setPage('Ready');
      setHint('开始指令发送失败，请刷新页面重试');
    }
  }

  function restartGame() {
    if (state.page !== 'Result') {
      return;
    }

    if (!sendMessage('RestartGame', '')) {
      setHint('重新开始指令发送失败，请刷新页面重试');
      return;
    }

    btnRestart.disabled = true;

    // 正常由 onStateChange(Playing) 驱动；失败时恢复按钮。
    window.setTimeout(function () {
      if (state.page === 'Result') {
        btnRestart.disabled = false;
        setHint('重新开始无响应，请再试一次');
      }
    }, 1500);
  }

  // ---------- Unity → H5 事件（由 bridge.js 转发） ----------

  function onProgress(progress) {
    if (typeof progress !== 'number') return;
    var percent = Math.max(0, Math.min(100, Math.round(progress * 100)));
    showOverlay('加载中… ' + percent + '%');
  }

  function onReady(json) {
    if (state.ready) {
      return;
    }

    var message = parseJson(json);
    if (message && message.protocol !== undefined && message.protocol !== 1) {
      console.warn('[App] 协议版本不一致：' + message.protocol);
    }

    state.ready = true;
    hideOverlay();
    setPage('Ready');

    var savedNickname = storage ? storage.get(NICKNAME_KEY) : null;
    if (savedNickname) {
      nicknameInput.value = savedNickname;
    }

    saveHintIfNeeded();
    console.log('[App] Unity ready');
  }

  function onStateChange(json) {
    var message = parseJson(json);
    if (!message || typeof message.state !== 'string') {
      console.warn('[App] onStateChange 载荷无效，已忽略', json);
      return;
    }

    switch (message.state) {
      case 'Playing':
        window.clearTimeout(state.startTimeout);
        state.gameOverHandled = false;
        setPage('Playing');
        setHint('');
        break;

      case 'Paused':
        setPage('Paused');
        setHint('已暂停');
        break;

      case 'Ready':
        setPage('Ready');
        break;

      case 'Won':
      case 'Lost':
        // 结算以 onGameOver 为准（带分数与明细）。
        break;

      default:
        console.log('[App] 未知状态：' + message.state);
    }
  }

  function onGameOver(json) {
    if (state.gameOverHandled) {
      console.warn('[App] 收到重复的 onGameOver，已忽略');
      return;
    }

    var message = parseJson(json);
    var payload = message && message.payload;
    if (!payload || typeof payload !== 'object') {
      console.warn('[App] onGameOver 载荷无效，已忽略', json);
      return;
    }

    state.gameOverHandled = true;

    var won = payload.result === 'win';
    var score = Number(payload.score);
    score = isFinite(score) && score > 0 ? Math.round(score) : 0;

    var survived = Number(payload.survivedSeconds);
    survived = isFinite(survived) && survived > 0 ? survived : 0;

    var nickname = normalizeNickname(payload.nickname);
    var previousBest = state.best;
    var isNewRecord = score > previousBest;

    if (isNewRecord) {
      state.best = score;
      if (storage && !storage.set(BEST_KEY, score)) {
        setHint('本次成绩无法保存（浏览器存储不可用）');
      }
    }

    resultTitle.textContent = won ? '胜利' : '失败';
    resultScore.textContent = String(score);
    resultBest.textContent = String(state.best);
    resultNickname.textContent = nickname;
    resultSurvived.textContent = survived.toFixed(1) + ' 秒';
    resultRecord.hidden = !isNewRecord;
    resultEl.hidden = false;

    nicknameInput.value = nickname;
    setPage('Result');
    hideOverlay();

    if (storage && !storage.isAvailable()) {
      setHint('本次成绩无法保存（浏览器存储不可用）');
    } else if (isNewRecord) {
      setHint('新纪录已保存');
    } else {
      setHint('');
    }

    console.log('[App] gameOver score=' + score + ' best=' + state.best + ' newRecord=' + isNewRecord);
  }

  function onFatalError(message) {
    setPage('Error');
    showOverlay('加载失败');
    if (overlayText) overlayText.textContent = '加载失败';
    if (errorCard) errorCard.hidden = false;
    if (errorText) errorText.textContent = message || '未知错误';
    if (btnStart) btnStart.hidden = true;
    console.error('[App] ' + message);
  }

  function onFileProtocol() {
    setPage('Error');
    showOverlay('请用本地服务器打开此页面');
    if (overlayText) {
      overlayText.textContent = '请用本地服务器打开此页面：在 web 目录运行 python -m http.server 8000，再访问 http://localhost:8000';
    }
    if (errorCard) errorCard.hidden = false;
    if (errorText) errorText.textContent = 'file:// 方式无法加载 Unity WebGL（浏览器跨域限制）。';
    if (btnRetry) btnRetry.hidden = true;
    if (btnStart) btnStart.hidden = true;
  }

  function setBuildInfo(info) {
    var version = (info && info.productVersion) || '?';
    if (versionEl) versionEl.textContent = 'v' + version;

    var builtAt = info && info.builtAt;
    if (buildTimeEl) {
      buildTimeEl.textContent = builtAt ? formatBuildTime(builtAt) : '构建时间未知';
    }
  }

  function formatBuildTime(iso) {
    var date = new Date(iso);
    if (isNaN(date.getTime())) {
      return iso;
    }
    var pad = function (value) { return value < 10 ? '0' + value : String(value); };
    return date.getFullYear() + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate()) +
      ' ' + pad(date.getHours()) + ':' + pad(date.getMinutes());
  }

  function setInstance(instance) {
    state.unityInstance = instance;
  }

  // ---------- 初始化 ----------

  if (btnStart) btnStart.addEventListener('click', startGame);
  if (btnRestart) btnRestart.addEventListener('click', restartGame);

  if (btnRetry) {
    btnRetry.addEventListener('click', function () {
      window.location.reload();
    });
  }

  // 标签页隐藏自动暂停；恢复时 Unity 只恢复「因隐藏而暂停」的那一次。
  document.addEventListener('visibilitychange', function () {
    if (!state.ready) return;
    sendMessage('SetPausedByVisibility', document.hidden ? 'true' : 'false');
  });

  if (storage && !storage.isAvailable()) {
    setHint('当前浏览器无法保存成绩，刷新后会丢失');
  }

  renderButtons(state.page);

  window.SurvivalApp = {
    onProgress: onProgress,
    onReady: onReady,
    onStateChange: onStateChange,
    onGameOver: onGameOver,
    onFatalError: onFatalError,
    onFileProtocol: onFileProtocol,
    setBuildInfo: setBuildInfo,
    setInstance: setInstance
  };
})();
