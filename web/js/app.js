(function () {
  'use strict';

  // v1.2 薄壳：H5 只负责昵称、进入游戏、数据面板、全屏入口。
  // 难度/开局/暂停/重开/结算全部在 Unity 画布内；H5 不再发任何局内指令（协议 v3）。
  var PROTOCOL_VERSION = 3;
  var DEFAULT_NICKNAME = '玩家';
  var MAX_NICKNAME = 16;
  var DEFAULT_DIFFICULTY = 'normal';
  var DIFFICULTIES = ['easy', 'normal', 'hard'];
  var DIFFICULTY_NAMES = { easy: '简单', normal: '普通', hard: '困难' };

  var NICKNAME_KEY = 'nickname';
  var BEST_KEY = 'bestScore';
  var BEST_MIGRATED_KEY = 'bestScoreMigrated';
  var DIFFICULTY_KEY = 'difficulty';

  var storage = window.SurvivalStorage;
  var canvas = document.getElementById('unity-canvas');
  var gameFrame = document.getElementById('game-frame');
  var overlay = document.getElementById('overlay');
  var overlayText = document.getElementById('overlay-text');
  var errorCard = document.getElementById('error-card');
  var errorText = document.getElementById('error-text');
  var btnRetry = document.getElementById('btn-retry');
  var btnStart = document.getElementById('btn-start');
  var btnFullscreen = document.getElementById('btn-fullscreen');
  var fullscreenHint = document.getElementById('fullscreen-hint');
  var fsToast = document.getElementById('fs-toast');
  var nicknameInput = document.getElementById('nickname');
  var statusHint = document.getElementById('status-hint');
  var dataBest = document.getElementById('data-best');
  var dataLast = document.getElementById('data-last');
  var versionEl = document.getElementById('version');

  var state = {
    page: 'Loading', // Loading | Shell | Entered | GameOver | Error
    ready: false,
    entered: false,
    enterSent: false,
    instance: null,
    nickname: loadStoredNickname(),
    best: loadBest(),
    difficulty: loadStoredDifficulty(),
    lastRound: null,
    lastUnityState: null,
    sentMessages: []
  };

  // ---------- 存储 / 迁移 ----------

  /**
   * 单一最高分 + 显式迁移标记。
   * v1.0 就用同名 survival.bestScore，v1.1 用 bestScore.{easy,normal,hard}；
   * 只有“没有迁移标记”时才读旧键（把当前同名键也算进去），之后永不回读。
   */
  function loadBest() {
    if (!storage) {
      return 0;
    }

    if (storage.get(BEST_MIGRATED_KEY) === '1') {
      return storage.getInt(BEST_KEY, 0);
    }

    var best = Math.max(
      storage.getInt(BEST_KEY, 0),
      storage.getInt('bestScore.easy', 0),
      storage.getInt('bestScore.normal', 0),
      storage.getInt('bestScore.hard', 0)
    );

    storage.set(BEST_KEY, best);
    storage.set(BEST_MIGRATED_KEY, '1');
    return best;
  }

  function loadStoredNickname() {
    if (!storage) {
      return DEFAULT_NICKNAME;
    }
    return normalizeNickname(storage.get(NICKNAME_KEY));
  }

  function loadStoredDifficulty() {
    if (!storage) {
      return DEFAULT_DIFFICULTY;
    }
    var value = storage.get(DIFFICULTY_KEY);
    return DIFFICULTIES.indexOf(value) >= 0 ? value : DEFAULT_DIFFICULTY;
  }

  function saveNickname(value) {
    state.nickname = value;
    if (storage) {
      storage.set(NICKNAME_KEY, value);
    }
  }

  function saveDifficulty(value) {
    state.difficulty = value;
    if (storage) {
      storage.set(DIFFICULTY_KEY, value);
    }
  }

  // ---------- 通用 ----------

  function normalizeNickname(value) {
    var name = String(value === null || value === undefined ? '' : value).replace(/\s+/g, ' ').trim();
    var chars = Array.from(name);
    if (chars.length > MAX_NICKNAME) {
      chars = chars.slice(0, MAX_NICKNAME);
    }
    return chars.length > 0 ? chars.join('') : DEFAULT_NICKNAME;
  }

  function normalizeDifficulty(value) {
    return DIFFICULTIES.indexOf(value) >= 0 ? value : DEFAULT_DIFFICULTY;
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

  function setHint(text) {
    if (statusHint) {
      statusHint.textContent = text || '';
    }
  }

  function isCoarsePointer() {
    return !!(window.matchMedia && window.matchMedia('(pointer: coarse)').matches);
  }

  function isEnteredLayout() {
    return state.page === 'Entered' || state.page === 'GameOver';
  }

  // ---------- 渲染 ----------

  function setPage(page) {
    state.page = page;
    render();
  }

  function render() {
    var entered = isEnteredLayout();

    if (btnStart) {
      btnStart.hidden = entered;
      btnStart.disabled = state.page !== 'Shell';
    }

    if (btnFullscreen) {
      // 全屏入口只在进入游戏后出现：Shell 阶段给全屏会陷入“全屏内没有开始按钮”的矛盾。
      btnFullscreen.hidden = !entered;
    }

    if (nicknameInput) {
      nicknameInput.disabled = state.page !== 'Shell';
      if (state.page === 'Shell') {
        nicknameInput.value = state.nickname;
      }
    }

    if (overlay) {
      overlay.hidden = entered;
    }

    if (overlayText) {
      if (state.page === 'Loading') {
        overlayText.textContent = '加载中…';
      } else if (state.page === 'Shell') {
        // 明确告诉玩家还要点网页按钮；只写「游戏已就绪」会被误认为已经进游戏。
        overlayText.textContent = '游戏已就绪 · 点击下方「开始游戏」进入';
      } else if (state.page === 'Error') {
        overlayText.textContent = '加载失败';
      }
    }

    if (errorCard) {
      errorCard.hidden = state.page !== 'Error';
    }

    renderData();
  }

  function renderData() {
    if (dataBest) {
      dataBest.textContent = String(state.best);
    }

    if (dataLast) {
      if (!state.lastRound) {
        dataLast.textContent = '—';
        dataLast.removeAttribute('title');
        dataLast.removeAttribute('aria-label');
      } else {
        var round = state.lastRound;
        // 数据栏按 mock 只显示「难度 分数」；胜负/存活放 title/aria，不上屏重复 Unity 结算。
        dataLast.textContent = DIFFICULTY_NAMES[round.difficulty] + ' ' + round.score;
        var full = (round.result === 'win' ? '胜利' : '失败') + ' · 存活 ' + round.survived.toFixed(1) + ' 秒';
        dataLast.title = full;
        dataLast.setAttribute('aria-label', full);
      }
    }
  }

  function showError(message) {
    state.ready = false;
    if (errorText) {
      errorText.textContent = message || '未知错误';
    }
    setPage('Error');
    setHint('');
    console.error('[App] ' + message);
  }

  // ---------- Unity → H5 ----------

  function onProgress(progress) {
    if (state.page !== 'Loading' || !overlayText) {
      return;
    }
    var percent = Math.max(0, Math.min(100, Math.round(progress * 100)));
    overlayText.textContent = '加载中… ' + percent + '%';
  }

  function onReady(json) {
    // 已经进入 Error（例如版本不匹配）后不再被迟到的 onReady 改写状态。
    if (state.page === 'Error') {
      return;
    }

    var message = parseJson(json);
    if (!message || typeof message.protocol !== 'number') {
      showError('Unity 未返回有效协议版本，请刷新重试');
      return;
    }

    // 新 H5 配旧 Unity 会进入一个没有菜单的壳：必须显式中止，而不是只 warning。
    if (message.protocol !== PROTOCOL_VERSION) {
      showError('版本不匹配（Unity ' + message.protocol + ' / 页面 ' + PROTOCOL_VERSION + '），请刷新重试');
      return;
    }

    state.ready = true;
    state.nickname = loadStoredNickname();
    state.difficulty = loadStoredDifficulty();

    // 事件可能早于 onReady（?perf=1&autostart=1 或极慢首帧）：Unity 已在局内时不能把页面退回 Shell，
    // 否则遮罩会盖住正在运行的游戏、状态文案也被覆盖。
    var pending = state.lastUnityState;
    if (pending === 'playing' || pending === 'paused' || pending === 'result') {
      state.entered = true;
      state.enterSent = true;
      if (state.page !== 'GameOver') {
        setPage('Entered');
      }
      setHint(hintForState(pending, true));
    } else {
      setPage('Shell');
      setHint('点击「开始游戏」进入；难度在游戏内选择');
    }

    if (storage && !storage.isAvailable()) {
      setHint('当前浏览器无法保存成绩，刷新后会丢失');
    }

    console.log('[App] Unity ready (protocol 3)');
  }

  /** 状态文案的唯一映射（onStateChange 与 onReady 的乱序补偿共用）。 */
  function hintForState(name, entered) {
    switch (name) {
      case 'menu':
        return entered ? '游戏菜单 · 在画面内选择难度和开始' : '点击「开始游戏」进入';
      case 'playing':
        return '游戏中 · 暂停按钮在游戏内右上角';
      case 'paused':
        return '已暂停 · 在画面内点「继续」';
      case 'result':
        return '本局结束 · 在画面内选择再来一局或返回菜单';
      default:
        return '';
    }
  }

  function onStateChange(json) {
    if (state.page === 'Error') {
      return;
    }

    var message = parseJson(json);
    if (!message || typeof message.state !== 'string') {
      console.warn('[App] onStateChange 载荷无效，已忽略', json);
      return;
    }

    if (message.protocol !== PROTOCOL_VERSION) {
      console.warn('[App] onStateChange 协议不匹配（' + message.protocol + '），已丢弃');
      return;
    }

    state.lastUnityState = message.state;

    // 只做状态文案，不控制布局（壳的进入/退出只由 EnterGame 与刷新决定）。
    var hint = hintForState(message.state, state.entered);
    if (!hint) {
      console.log('[App] 未知状态：' + message.state);
      return;
    }

    setHint(hint);
    if (state.page === 'GameOver' && (message.state === 'menu' || message.state === 'playing')) {
      setPage('Entered');
    }
  }

  function onDifficultyChanged(json) {
    if (state.page === 'Error') {
      return;
    }

    var message = parseJson(json);
    if (!message || typeof message.difficulty !== 'string') {
      console.warn('[App] onDifficultyChanged 载荷无效，已忽略', json);
      return;
    }
    if (message.protocol !== PROTOCOL_VERSION) {
      console.warn('[App] onDifficultyChanged 协议不匹配（' + message.protocol + '），已丢弃');
      return;
    }
    saveDifficulty(normalizeDifficulty(message.difficulty));
    console.log('[App] difficulty changed: ' + state.difficulty);
  }

  function onGameOver(json) {
    if (state.page === 'Error') {
      return;
    }

    var message = parseJson(json);
    if (message && message.protocol !== PROTOCOL_VERSION) {
      // 版本不匹配的结算不能写最高分（只丢弃并 warn，不改变页面状态）。
      console.warn('[App] onGameOver 协议不匹配（' + message.protocol + '），已丢弃');
      return;
    }

    var payload = message && message.payload;
    if (!payload || typeof payload !== 'object') {
      console.warn('[App] onGameOver 载荷无效，已忽略', json);
      return;
    }

    var score = Number(payload.score);
    score = isFinite(score) && score > 0 ? Math.round(score) : 0;

    var survived = Number(payload.survivedSeconds);
    survived = isFinite(survived) && survived > 0 ? survived : 0;

    var difficulty = normalizeDifficulty(payload.difficulty || state.difficulty);
    var newRecord = score > state.best;

    if (newRecord) {
      state.best = score;
      if (storage) {
        storage.set(BEST_KEY, state.best);
      }
    }

    // 最近一局只在内存里（刷新清空）；完整信息放 title/aria，不上屏。
    state.lastRound = {
      score: score,
      difficulty: difficulty,
      result: payload.result === 'win' ? 'win' : 'lose',
      survived: survived
    };

    setPage('GameOver');
    setHint(newRecord ? '本局结束 · 新纪录' : '本局结束 · 在画面内选择再来一局或返回菜单');
    console.log('[App] gameOver score=' + score + ' difficulty=' + difficulty + ' best=' + state.best + ' newRecord=' + newRecord);
  }

  function onFatalError(message) {
    showError(message || '加载失败');
  }

  function onFileProtocol() {
    showError('请通过本地 HTTP 服务打开本页（不能直接双击 HTML 文件）');
  }

  // ---------- H5 → Unity ----------

  function sendMessage(method, payload) {
    if (!state.instance || typeof state.instance.SendMessage !== 'function') {
      console.warn('[App] Unity 实例未就绪，无法发送：' + method);
      return false;
    }
    try {
      state.instance.SendMessage('WebGLBridge', method, payload);
      state.sentMessages.push({ method: method, payload: payload });
      return true;
    } catch (error) {
      console.warn('[App] SendMessage 失败：' + method + ' ' + error);
      return false;
    }
  }

  function beginEnter() {
    if (!state.ready || state.entered || state.enterSent) {
      return;
    }

    if (!state.instance) {
      // onReady 理论上晚于 createUnityInstance 完成；这里兜住极端时序，避免静默失败。
      setHint('游戏尚未就绪，请稍后重试');
      return;
    }

    var nickname = normalizeNickname(nicknameInput ? nicknameInput.value : state.nickname);
    saveNickname(nickname);
    if (nicknameInput) {
      nicknameInput.value = nickname;
    }

    // 先设页面状态再发送：SendMessage 是同步的，反序会被刚到的状态回调覆盖。
    state.entered = true;
    state.enterSent = true;
    setPage('Entered');
    setHint('游戏菜单 · 在画面内选择难度和开始');

    var payload = JSON.stringify({
      nickname: nickname,
      lastDifficulty: normalizeDifficulty(state.difficulty)
    });

    if (!sendMessage('EnterGame', payload)) {
      state.entered = false;
      state.enterSent = false;
      setPage('Shell');
      setHint('进入失败，请刷新页面重试');
    }
  }

  // ---------- 全屏 ----------

  var fullscreenHintTimer = null;
  var fsToastTimer = null;

  function isFullscreen() {
    return !!(document.fullscreenElement || document.webkitFullscreenElement);
  }

  function showFullscreenHint(text) {
    if (!fullscreenHint) {
      return;
    }
    fullscreenHint.hidden = false;
    fullscreenHint.textContent = text;
    window.clearTimeout(fullscreenHintTimer);
    fullscreenHintTimer = window.setTimeout(function () {
      fullscreenHint.hidden = true;
      fullscreenHint.textContent = '';
    }, 5000);
  }

  function showFsToast() {
    if (!fsToast) {
      return;
    }
    fsToast.textContent = isCoarsePointer() ? '手势返回退出全屏' : '按 Esc 退出全屏';
    fsToast.hidden = false;
    window.clearTimeout(fsToastTimer);
    fsToastTimer = window.setTimeout(function () {
      fsToast.hidden = true;
    }, 2500);
  }

  function updateFullscreenUi() {
    var active = isFullscreen();

    if (active) {
      showFsToast();
      try {
        if (screen.orientation && typeof screen.orientation.lock === 'function') {
          var lock = screen.orientation.lock('landscape');
          if (lock && typeof lock.catch === 'function') {
            lock.catch(function () {});
          }
        }
      } catch (error) {
        // 不支持 orientation.lock 的浏览器直接忽略。
      }
    } else {
      if (fsToast) {
        fsToast.hidden = true;
      }
      if (screen.orientation && typeof screen.orientation.unlock === 'function') {
        try {
          screen.orientation.unlock();
        } catch (error) {
          // 忽略
        }
      }
    }
  }

  function requestFullscreen() {
    if (!isEnteredLayout()) {
      return;
    }

    var element = gameFrame || document.documentElement;
    var request = element.requestFullscreen || element.webkitRequestFullscreen;
    if (!request) {
      showFullscreenHint('当前浏览器不支持网页全屏，请横屏或添加到主屏幕');
      return;
    }

    var result;
    try {
      result = request.call(element);
    } catch (error) {
      showFullscreenHint('当前浏览器不支持网页全屏，请横屏或添加到主屏幕');
      return;
    }

    if (result && typeof result.catch === 'function') {
      result.catch(function () {
        showFullscreenHint('当前浏览器不支持网页全屏，请横屏或添加到主屏幕');
      });
    }
  }

  // ---------- 画布滚轮穿透 ----------

  /**
   * Unity WebGL 运行时会在画布上注册 wheel 回调并 preventDefault（引擎行为），页面在画布上滚不动。
   * 这里在 document 捕获阶段接管：preventDefault 后 scrollBy，全屏/Ctrl+滚轮放行。
   * 监听挂 document 而不是画布，避免与 Unity 自己注册的监听抢顺序。
   */
  function onCanvasWheel(event) {
    if (event.target !== canvas || event.defaultPrevented || isFullscreen() || event.ctrlKey) {
      return;
    }

    var pixels;
    if (event.deltaMode === 1) {
      pixels = event.deltaY * 16;
    } else if (event.deltaMode === 2) {
      pixels = event.deltaY * window.innerHeight;
    } else {
      pixels = event.deltaY;
    }

    if (!pixels) {
      return;
    }

    event.preventDefault();
    window.scrollBy(0, pixels);
  }

  // ---------- 可见性（环境信号，不是局内指令） ----------

  var lastVisibilitySent = null;

  function syncVisibility(hidden) {
    if (!state.entered || hidden === lastVisibilitySent) {
      return;
    }
    lastVisibilitySent = hidden;
    if (!sendMessage('SetPausedByVisibility', hidden ? 'true' : 'false')) {
      lastVisibilitySent = null;
    }
  }

  // ---------- 构建信息 ----------

  function setBuildInfo(info) {
    if (versionEl) {
      versionEl.textContent = 'v' + ((info && info.productVersion) || '-');
    }
  }

  function setInstance(instance) {
    state.instance = instance;
  }

  // ---------- 初始化 ----------

  if (btnStart) {
    btnStart.addEventListener('click', beginEnter);
  }

  if (btnFullscreen) {
    btnFullscreen.addEventListener('click', requestFullscreen);
  }

  if (btnRetry) {
    btnRetry.addEventListener('click', function () {
      window.location.reload();
    });
  }

  // 昵称只由输入框展示（Shell 可编辑、进入后只读）；数据栏不再重复一行。

  document.addEventListener('fullscreenchange', updateFullscreenUi);
  document.addEventListener('webkitfullscreenchange', updateFullscreenUi);

  if (canvas) {
    document.addEventListener('wheel', onCanvasWheel, { passive: false, capture: true });
  }

  window.addEventListener('blur', function () {
    syncVisibility(true);
  });

  window.addEventListener('focus', function () {
    syncVisibility(false);
  });

  document.addEventListener('visibilitychange', function () {
    syncVisibility(document.hidden);
  });

  render();

  window.SurvivalApp = {
    onProgress: onProgress,
    onReady: onReady,
    onStateChange: onStateChange,
    onGameOver: onGameOver,
    onDifficultyChanged: onDifficultyChanged,
    onFatalError: onFatalError,
    onFileProtocol: onFileProtocol,
    setBuildInfo: setBuildInfo,
    setInstance: setInstance
  };

  // E2E 专用（?testhook=1）：暴露已发送消息与状态，用于断言“只发一次 EnterGame、不发旧指令”。
  if (/[?&]testhook=1(?:&|$)/.test(window.location.search)) {
    window.SurvivalApp.getSentMessages = function () {
      return state.sentMessages.slice();
    };
    window.SurvivalApp.getState = function () {
      return {
        page: state.page,
        ready: state.ready,
        entered: state.entered,
        best: state.best,
        difficulty: state.difficulty
      };
    };
  }
})();
