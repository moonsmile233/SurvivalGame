(function () {
  'use strict';

  var DEFAULT_NICKNAME = '玩家';
  var MAX_NICKNAME = 16;
  // 旧版（v1.0.0）单一最高分：只在迁移时读，迁完即删（见 loadBests）。
  var LEGACY_BEST_KEY = 'bestScore';
  var NICKNAME_KEY = 'nickname';
  var DIFFICULTY_KEY = 'difficulty';
  var PROTOCOL_VERSION = 2;

  // 难度是 H5 的概念，Unity 只认 easy/normal/hard 三个字符串（协议 v2 §2.2）。
  // 卡面倍率文案与 Unity 的 DifficultyProfile 是两处，改数值时必须同时改这里和资产。
  var DIFFICULTIES = ['easy', 'normal', 'hard'];
  var DIFFICULTY_META = {
    easy: { name: '简单', bestKey: 'bestScore.easy' },
    normal: { name: '普通', bestKey: 'bestScore.normal' },
    hard: { name: '困难', bestKey: 'bestScore.hard' }
  };
  var DEFAULT_DIFFICULTY = 'normal';

  // 开始/重新开始的 3-2-1（H5 自己倒数）：3 秒后才发指令，给手指离开屏幕/看清难度的机会。
  var COUNTDOWN_SECONDS = 3;

  // 点开始后的兜底时限。正常首帧响应 <100ms（SendMessage 同步）；9 秒只覆盖
  // 「指令真的丢了 / Unity 卡住」的情况，不能短到把慢机器上的正常启动误判。
  var START_RESPONSE_TIMEOUT_MS = 9000;

  var storage = window.SurvivalStorage;
  var canvas = document.getElementById('unity-canvas');
  var overlay = document.getElementById('overlay');
  var overlayText = document.getElementById('overlay-text');
  var errorCard = document.getElementById('error-card');
  var errorText = document.getElementById('error-text');
  var btnRetry = document.getElementById('btn-retry');
  var btnStart = document.getElementById('btn-start');
  var btnRestart = document.getElementById('btn-restart');
  var btnFullscreen = document.getElementById('btn-fullscreen');
  var fullscreenHint = document.getElementById('fullscreen-hint');
  var nicknameInput = document.getElementById('nickname');
  var statusHint = document.getElementById('status-hint');
  var resultEl = document.getElementById('result');
  var resultTitle = document.getElementById('result-title');
  var resultScore = document.getElementById('result-score');
  var resultBest = document.getElementById('result-best');
  var resultNickname = document.getElementById('result-nickname');
  var resultSurvived = document.getElementById('result-survived');
  var resultRecord = document.getElementById('result-record');
  var resultDifficulty = document.getElementById('result-difficulty');
  var versionEl = document.getElementById('version');
  var buildTimeEl = document.getElementById('build-time');
  var difficultySection = document.getElementById('difficulty');
  var difficultyInputs = Array.prototype.slice.call(document.querySelectorAll('.difficulty-input'));
  var bestLabel = document.getElementById('best-label');
  var bestValue = document.getElementById('best-value');
  var countdownEl = document.getElementById('countdown');
  var countdownValueEl = document.getElementById('countdown-value');

  var state = {
    page: 'Loading',
    ready: false,
    unityInstance: null,
    difficulty: loadStoredDifficulty(),
    bests: loadBests(),
    gameOverHandled: false,
    startTimeout: null,
    // ReturnToMenu 已发出、等待 Unity 回 Ready：期间忽略重复点击，防止连发中止。
    returnPending: false
  };

  // 开始/重新开始的 3-2-1。pending: 'start' | 'restart' | null；数值只用于显示。
  var countdown = {
    pending: null,
    value: 0,
    timer: null
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

  // 操作提示按输入设备区分（和 CSS 的 pointer: coarse 保持一致），不要在桌面提示里写「点暂停」。
  function isCoarsePointer() {
    return !!(window.matchMedia && window.matchMedia('(pointer: coarse)').matches);
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

  function normalizeDifficulty(value) {
    return DIFFICULTIES.indexOf(value) >= 0 ? value : null;
  }

  function loadStoredDifficulty() {
    var saved = storage ? normalizeDifficulty(storage.get(DIFFICULTY_KEY)) : null;
    return saved || DEFAULT_DIFFICULTY;
  }

  /**
   * 分档最高分 + 旧键迁移（M6 §2/§4）。
   * 迁移只在 bestScore.normal 不存在且旧值有效时跑一次；**写入成功才删旧键**，
   * 写失败（隐私模式/配额）保留旧键，下次加载再试。不换算：v1.0.0 的分数没有难度倍率，
   * 按原值迁到普通档，避免旧纪录凭空变高。
   */
  function loadBests() {
    var bests = { easy: 0, normal: 0, hard: 0 };
    if (!storage) {
      return bests;
    }

    var legacy = storage.getInt(LEGACY_BEST_KEY, -1);
    if (legacy >= 0 && storage.get(DIFFICULTY_META.normal.bestKey) === null) {
      if (storage.set(DIFFICULTY_META.normal.bestKey, legacy)) {
        storage.remove(LEGACY_BEST_KEY);
        console.log('[App] 旧最高分已迁移到普通档：' + legacy);
      }
    }

    for (var i = 0; i < DIFFICULTIES.length; i++) {
      var id = DIFFICULTIES[i];
      bests[id] = storage.getInt(DIFFICULTY_META[id].bestKey, 0);
    }

    return bests;
  }

  function renderDifficulty() {
    for (var i = 0; i < difficultyInputs.length; i++) {
      var input = difficultyInputs[i];
      var selected = input.value === state.difficulty;
      input.checked = selected;
      if (input.parentElement) {
        input.parentElement.classList.toggle('is-selected', selected);
      }
    }

    var meta = DIFFICULTY_META[state.difficulty];
    if (bestLabel) bestLabel.textContent = meta.name;
    if (bestValue) bestValue.textContent = String(state.bests[state.difficulty]);
  }

  function applyDifficulty(id) {
    if (!id) {
      return;
    }

    state.difficulty = id;
    if (storage) {
      storage.set(DIFFICULTY_KEY, id);
    }
    renderDifficulty();
  }

  /**
   * 点难度卡（常驻可点，规则见 M6 §2）：
   * - Ready：只选择，不发指令；
   * - Starting（H5 倒计时中）：取消倒计时、回 Ready、选中该档；
   * - Playing/Paused/Result：中止本局（ReturnToMenu，不计分）→ 回 Ready 并选中该档。
   *   点已选中的那张也执行（等同「回到该档的开始界面」），所以不能用 change 事件。
   */
  function onDifficultyPicked(value) {
    var id = normalizeDifficulty(value);
    if (!id || state.page === 'Loading' || state.page === 'Error') {
      // 还没就绪：不允许改档，把 radio 的视觉状态回弹到当前选择。
      renderDifficulty();
      return;
    }

    // 倒计时中点卡：先取消，绝不后台开局。
    if (countdown.pending) {
      cancelCountdown();
      if (state.page === 'Starting') {
        setPage('Ready');
      }
    }

    if (state.page === 'Ready') {
      applyDifficulty(id);
      return;
    }

    if (state.page === 'Playing' || state.page === 'Paused' || state.page === 'Result') {
      if (state.returnPending) {
        return;
      }

      // 先更新选择：最高分行立即切到新档，Unity 回 Ready 时菜单已是正确状态。
      applyDifficulty(id);
      state.returnPending = true;
      setHint('正在返回主菜单…');

      if (!sendMessage('ReturnToMenu', '')) {
        state.returnPending = false;
        setHint('返回指令发送失败，请刷新页面重试');
      }
      return;
    }
  }

  function renderButtons(page) {
    var showStart = page === 'Loading' || page === 'Ready' || page === 'Starting';
    btnStart.hidden = !showStart;
    btnStart.disabled = page !== 'Ready';

    // 难度区常驻（用户要求“键常驻”）：所有状态可见、可点，点击行为由 onDifficultyPicked 决定。
    if (difficultySection) {
      difficultySection.hidden = false;
    }
    for (var i = 0; i < difficultyInputs.length; i++) {
      difficultyInputs[i].disabled = false;
    }

    btnRestart.hidden = page !== 'Result';
    btnRestart.disabled = page !== 'Result' || !!countdown.pending;

    // 主界面也能进全屏（L1.1 用户口径）：Ready/Starting 全屏保留难度区 + 开始，否则进全屏没法开局。
    // 「全屏无网页按钮」只作用于游戏中的 Playing/Paused；Result 不提供全屏（结算卡会卡在页面外）。
    if (btnFullscreen) {
      btnFullscreen.hidden = page !== 'Ready' && page !== 'Starting' &&
        page !== 'Playing' && page !== 'Paused';
    }

    nicknameInput.disabled = page !== 'Ready';
  }

  function setPage(page) {
    state.page = page;
    // CSS 用 body[data-page] 区分状态；全屏按钮只在 Playing/Paused 显示（L1 口径）。
    document.body.dataset.page = page;
    // 回 Ready（返回主菜单）也要收起结算卡；否则难度区回去了、旧成绩卡还挂在下面。
    if (page === 'Playing' || page === 'Ready') {
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

  // ---------- 开始/重开的 3-2-1 ----------

  function renderCountdown() {
    if (!countdownEl) {
      return;
    }
    countdownEl.hidden = countdown.pending === null;
    if (countdownValueEl) {
      countdownValueEl.textContent = String(Math.max(1, countdown.value));
    }
  }

  function cancelCountdown() {
    if (countdown.timer !== null) {
      window.clearInterval(countdown.timer);
      countdown.timer = null;
    }
    countdown.pending = null;
    countdown.value = 0;
    renderCountdown();
    if (state.page === 'Result') {
      btnRestart.disabled = false;
    }
  }

  /**
   * 3→2→1（每秒一跳），结束后才发指令。倒计时期间：
   * - 点难度卡 → cancelCountdown（Starting 回 Ready；Result 继续走中止流程）；
   * - 页面隐藏/失焦 → cancelCountdown，绝不后台开局。
   */
  function startCountdown(kind) {
    if (countdown.pending) {
      return;
    }

    countdown.pending = kind;
    countdown.value = COUNTDOWN_SECONDS;
    renderCountdown();

    countdown.timer = window.setInterval(function () {
      countdown.value -= 1;
      if (countdown.value > 0) {
        renderCountdown();
        return;
      }

      var pending = countdown.pending;
      cancelCountdown();
      if (pending === 'start') {
        sendStartCommand();
      } else if (pending === 'restart') {
        sendRestartCommand();
      }
    }, 1000);
  }

  // ---------- 用户操作 ----------

  function beginStartCountdown() {
    if (!state.ready || state.page !== 'Ready') {
      return;
    }

    var nickname = normalizeNickname(nicknameInput.value);
    nicknameInput.value = nickname;

    if (storage) {
      storage.set(NICKNAME_KEY, nickname);
      saveHintIfNeeded();
    }

    // 键盘事件要落在画布上（Unity 6 从 document 收键盘，但焦点放对才不会被昵称输入框抢走）。
    if (canvas && typeof canvas.focus === 'function') {
      canvas.focus();
    }

    setPage('Starting');
    setHint('3 秒后开始 · 点难度卡可取消');
    startCountdown('start');
  }

  function sendStartCommand() {
    var nickname = normalizeNickname(nicknameInput.value);

    // 顺序不能反：Unity 的 SendMessage 是同步的，onStateChange(Playing) 会在
    // sendMessage() 返回前到达。先设 Starting + 兜底计时器，再由状态回调清除。
    setHint('正在开始…');
    if (state.page !== 'Starting') {
      setPage('Starting');
    }

    // 兜底只提示、不改状态：Unity 可能已经在跑，只是状态回调没到。
    window.clearTimeout(state.startTimeout);
    state.startTimeout = window.setTimeout(function () {
      if (state.page === 'Starting') {
        setHint('启动较慢，若已在游戏中请直接玩；未开始请刷新');
      }
    }, START_RESPONSE_TIMEOUT_MS);

    if (!sendMessage('StartGame', JSON.stringify({ nickname: nickname, difficulty: state.difficulty }))) {
      window.clearTimeout(state.startTimeout);
      setPage('Ready');
      setHint('开始指令发送失败，请刷新页面重试');
    }
  }

  function beginRestartCountdown() {
    if (state.page !== 'Result' || countdown.pending) {
      return;
    }

    btnRestart.disabled = true;
    setHint('3 秒后重新开始 · 点难度卡可返回主菜单');

    // 结算卡可能在视口底部、画布已被滚走；倒计时画在画布上，先把它滚回来。
    var frame = document.querySelector('.game-frame');
    if (frame && typeof frame.scrollIntoView === 'function') {
      frame.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    startCountdown('restart');
  }

  function sendRestartCommand() {
    if (!sendMessage('RestartGame', '')) {
      btnRestart.disabled = false;
      setHint('重新开始指令发送失败，请刷新页面重试');
      return;
    }

    // 正常由 onStateChange(Playing) 驱动；失败时恢复按钮。
    window.setTimeout(function () {
      if (state.page === 'Result') {
        btnRestart.disabled = false;
        setHint('重新开始无响应，请再试一次');
      }
    }, 1500);
  }

  /** 页面隐藏/失焦时取消倒计时：不允许在玩家看不见的时候自动开局。 */
  function cancelCountdownBecauseHidden() {
    if (!countdown.pending) {
      return;
    }

    var kind = countdown.pending;
    cancelCountdown();
    if (kind === 'start') {
      setPage('Ready');
      setHint('已取消开始（页面切走了）');
    } else if (kind === 'restart') {
      btnRestart.disabled = false;
      setHint('已取消重新开始（页面切走了）');
    }
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
    // 旧 Unity（protocol 1）不会读 difficulty，开局会按 normal；只告警，不阻塞（协议 v2 §2.2 兼容矩阵）。
    if (message && message.protocol !== undefined && message.protocol !== PROTOCOL_VERSION) {
      console.warn('[App] 协议版本不一致：' + message.protocol + '（期望 ' + PROTOCOL_VERSION + '）');
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
        cancelCountdown();
        state.gameOverHandled = false;
        state.returnPending = false;
        setPage('Playing');
        // 开始按钮消失后页面不能一片空白，否则像卡死（M4 第 1.5 节）。
        setHint('游戏中 · 暂停按钮在游戏内右上角');
        break;

      case 'Paused':
        setPage('Paused');
        // M4.6：失焦切回保持暂停，只有玩家点「继续」/按 P 才开始 3-2-1；文案不能暗示自动恢复。
        setHint(isCoarsePointer()
          ? '已暂停 · 点「继续」，3 秒后开始'
          : '已暂停 · 点「继续」或按 P，3 秒后开始');
        break;

      case 'Ready':
        // 只有 ReturnToMenu 会让 Unity 主动回到 Ready；清掉「正在返回主菜单…」提示。
        state.returnPending = false;
        cancelCountdown();
        setPage('Ready');
        setHint('');
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
    // 以 Unity 回传的实际档位为准（非法/缺省已被回落为 normal）；旧 Unity 没有该字段时用本局选择。
    var roundDifficulty = normalizeDifficulty(payload.difficulty) || state.difficulty;
    var previousBest = state.bests[roundDifficulty];
    var isNewRecord = score > previousBest;

    if (isNewRecord) {
      state.bests[roundDifficulty] = score;
      if (storage && !storage.set(DIFFICULTY_META[roundDifficulty].bestKey, score)) {
        setHint('本次成绩无法保存（浏览器存储不可用）');
      }
    }

    function showResultCard() {
      resultTitle.textContent = won ? '胜利' : '失败';
      resultScore.textContent = String(score);
      resultBest.textContent = String(state.bests[roundDifficulty]);
      if (resultDifficulty) resultDifficulty.textContent = DIFFICULTY_META[roundDifficulty].name;
      resultNickname.textContent = nickname;
      resultSurvived.textContent = survived.toFixed(1) + ' 秒';
      resultRecord.hidden = !isNewRecord;
      resultEl.hidden = false;

      // 结算卡已包含「重新开始」按钮。等两帧让卡片完成布局（直接调用时高度还是旧的，
      // 滚不到按钮），再只在底部超出视口时对齐卡片底部；短横屏/320 小屏都能看到按钮。
      if (typeof resultEl.scrollIntoView === 'function') {
        requestAnimationFrame(() => {
          requestAnimationFrame(() => {
            if (resultEl.getBoundingClientRect().bottom > window.innerHeight) {
              resultEl.scrollIntoView({ behavior: 'smooth', block: 'end' });
            }
          });
        });
      }

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

      console.log('[App] gameOver score=' + score + ' difficulty=' + roundDifficulty +
        ' best=' + state.bests[roundDifficulty] + ' newRecord=' + isNewRecord);
    }

    // 全屏下 H5 结算卡被 CSS 隐藏；先退出全屏，等退出完成再显示结算，避免玩家只看到游戏画面。
    if (isFullscreen()) {
      var exit = document.exitFullscreen || document.webkitExitFullscreen;
      if (exit) {
        try {
          var pendingExit = exit.call(document);
          if (pendingExit && typeof pendingExit.finally === 'function') {
            pendingExit.finally(showResultCard);
            return;
          }
        } catch (error) {
          // 退出失败就按普通流程显示，状态不丢。
        }
      }
    }

    showResultCard();
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

  // ---------- 全屏（H5，M4.5 §1） ----------

  var fullscreenHintTimer = null;

  function isFullscreen() {
    return !!(document.fullscreenElement || document.webkitFullscreenElement);
  }

  function showFullscreenHint(text) {
    if (!fullscreenHint) return;
    fullscreenHint.hidden = false;
    fullscreenHint.textContent = text;
    window.clearTimeout(fullscreenHintTimer);
    fullscreenHintTimer = window.setTimeout(function () {
      fullscreenHint.hidden = true;
      fullscreenHint.textContent = '';
    }, 5000);
  }

  // 全屏里不放任何网页端按钮（L1）：进入后按钮随 .controls 一起隐藏，退出靠 Esc / 系统手势；
  // 这里只处理横屏锁定与页面恢复（退出全屏后 CSS 自动恢复外壳，状态仍由 body[data-page] 驱动）。
  function updateFullscreenUi() {
    var active = isFullscreen();

    if (active) {
      // 横屏锁定是 best-effort：桌面/部分浏览器会 reject，失败就保留 H5 竖屏提示。
      try {
        if (screen.orientation && typeof screen.orientation.lock === 'function') {
          var lock = screen.orientation.lock('landscape');
          if (lock && typeof lock.catch === 'function') lock.catch(function () {});
        }
      } catch (error) {
        // 不支持 orientation.lock 的浏览器直接忽略。
      }
    } else if (screen.orientation && typeof screen.orientation.unlock === 'function') {
      try { screen.orientation.unlock(); } catch (error) { /* 忽略 */ }
    }
  }

  function requestFullscreen() {
    var element = document.documentElement;
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

  // ---------- 画布滚轮穿透（M6 §1.3） ----------

  /**
   * Unity WebGL 运行时会在画布上注册 wheel 回调并 `preventDefault()`（引擎行为，不是本页 CSS），
   * 页面在画布上永远滚不动；而画布占了大半屏，鼠标多半悬在画布上。这里自己接管滚轮：
   * `preventDefault()` 后 `scrollBy`，浏览器默认滚动被拦掉，所以不会双重滚动。
   * - 监听挂 document 的捕获阶段（不在画布本身上）：同一元素上的监听按注册顺序执行，
   *   Unity 在自己初始化的过程中给画布注册，谁先谁后不受我们控制；捕获阶段一定早于目标监听。
   * - 全屏：`html:fullscreen` 已 `overflow:hidden`，页面本就不滚，直接放行（不吞 Unity 的事件）。
   * - Ctrl+滚轮：留给浏览器缩放。
   * - deltaMode：0=像素、1=行（取 16px/行，浏览器常见行高）、2=页（按视口高度）。
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

  // ---------- 初始化 ----------

  // 只负责进入全屏；退出交给浏览器的 Esc / 系统手势，不在页面里提供按钮（L1）。
  if (btnFullscreen) btnFullscreen.addEventListener('click', requestFullscreen);
  document.addEventListener('fullscreenchange', updateFullscreenUi);
  document.addEventListener('webkitfullscreenchange', updateFullscreenUi);
  updateFullscreenUi();

  // 必须非 passive 才能 preventDefault；挂在 document 捕获阶段，保证早于 Unity 在画布上
  // 注册的回调执行（详见 onCanvasWheel 注释）。Unity 的回调随后仍会收到事件。
  if (canvas) {
    document.addEventListener('wheel', onCanvasWheel, { passive: false, capture: true });
  }

  if (btnStart) btnStart.addEventListener('click', beginStartCountdown);
  if (btnRestart) btnRestart.addEventListener('click', beginRestartCountdown);

  // 用 click 而不是 change：点「已选中」的卡也要触发（change 不会）。
  // 点击 label 会激活内部 radio，事件冒泡到 input，所以监听 input 的 click 恰好一次。
  for (var inputIndex = 0; inputIndex < difficultyInputs.length; inputIndex++) {
    difficultyInputs[inputIndex].addEventListener('click', function () {
      onDifficultyPicked(this.value);
    });

    // 键盘方向键只改 checked、不发 click：只在 Ready 接受选择，其余状态回弹，
    // 避免在局内用方向键误触发中止（中止只能通过鼠标/触摸点击卡片）。
    difficultyInputs[inputIndex].addEventListener('change', function () {
      if (state.page === 'Ready') {
        applyDifficulty(this.value);
      } else {
        renderDifficulty();
      }
    });
  }

  if (btnRetry) {
    btnRetry.addEventListener('click', function () {
      window.location.reload();
    });
  }

  // 失焦 / 标签页隐藏 → Unity 自动暂停；切回**不瞬时恢复**，由 Unity 暂停面板走 3-2-1 倒计时
  // （M4 第 1.5 节）。blur/focus 与 visibilitychange 都要监听：桌面 Alt-Tab 只发前两个，
  // 移动端切 App 通常只有 visibilitychange。
  // 同一次切换会同时触发 blur/focus 和 visibilitychange，H5 侧先按「最后发出的值」去重；
  // Unity 侧还有 _pausedByVisibility 标记兜底，两层都幂等（ticket 1.5）。
  var lastVisibilitySent = null;

  function syncVisibility(hidden) {
    if (!state.ready) return;

    // 倒计时中切走：直接取消，不能在后台把局开起来（哪怕 Unity 没收到暂停指令）。
    if (hidden) {
      cancelCountdownBecauseHidden();
    }

    if (hidden === lastVisibilitySent) return;

    lastVisibilitySent = hidden;
    if (!sendMessage('SetPausedByVisibility', hidden ? 'true' : 'false')) {
      lastVisibilitySent = null;
    }
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

  if (storage && !storage.isAvailable()) {
    setHint('当前浏览器无法保存成绩，刷新后会丢失');
  }

  document.body.dataset.page = state.page;
  renderDifficulty();
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

  // E2E 专用（?testhook=1）：暴露原始 SendMessage，用来验证 Unity 对「非法 difficulty」和
  // 「v1 纯昵称载荷」的回落。正常页面（没有参数）不挂这个全局，调试入口不会进线上。
  if (/[?&]testhook=1(?:&|$)/.test(window.location.search)) {
    window.SurvivalApp.__sendMessage = sendMessage;
  }
})();
