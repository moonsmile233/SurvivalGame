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

  // 主流竖屏手机（390×844 / 412×915 / 430×932 / 平板）：16:9 画布是宽度受限，隐藏菜单不会让画面变大，
  // 只会让页面跳。所以 Playing/Paused 保留菜单（卡片禁用、弱化），画布尺寸与 Ready 相同。
  // 320×568、横屏和桌面仍然隐藏菜单，把高度让给画面。
  var TALL_PORTRAIT_QUERY = '(orientation: portrait) and (min-height: 700px)';
  var tallPortrait = window.matchMedia ? window.matchMedia(TALL_PORTRAIT_QUERY) : null;

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
  var btnPauseMenu = document.getElementById('btn-pause-menu');
  var btnResultMenu = document.getElementById('btn-result-menu');
  var btnFullscreen = document.getElementById('btn-fullscreen');
  var btnExitFullscreen = document.getElementById('btn-exit-fullscreen');
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

  var state = {
    page: 'Loading',
    ready: false,
    unityInstance: null,
    difficulty: loadStoredDifficulty(),
    bests: loadBests(),
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

  function selectDifficulty(value) {
    var id = normalizeDifficulty(value);
    if (!id || id === state.difficulty) {
      return;
    }

    state.difficulty = id;
    if (storage) {
      storage.set(DIFFICULTY_KEY, id);
    }
    renderDifficulty();
  }

  /** 菜单（难度区）在哪些状态可见：Loading/Ready/Starting 总是；Playing/Paused 只在主流竖屏手机。 */
  function shouldShowMenu(page) {
    if (page === 'Loading' || page === 'Ready' || page === 'Starting') {
      return true;
    }
    if (page === 'Playing' || page === 'Paused') {
      return !!(tallPortrait && tallPortrait.matches);
    }
    return false; // Result / Error：结算卡优先，避免溢出
  }

  function renderButtons(page) {
    var showStart = page === 'Loading' || page === 'Ready' || page === 'Starting';
    btnStart.hidden = !showStart;
    btnStart.disabled = page !== 'Ready';

    // 难度只在 Ready 可改；进入 Playing 后由 Unity 快照锁定（协议 v2 §2.2）。
    // 高竖屏下菜单仍显示，但卡片禁用 + 弱化，明确“现在不能改”。
    var showMenu = shouldShowMenu(page);
    var menuLocked = showMenu && page !== 'Ready';
    if (difficultySection) {
      difficultySection.hidden = !showMenu;
      difficultySection.classList.toggle('is-locked', menuLocked);
    }
    for (var i = 0; i < difficultyInputs.length; i++) {
      difficultyInputs[i].disabled = page !== 'Ready';
    }

    btnRestart.hidden = page !== 'Result';
    btnRestart.disabled = page !== 'Result';

    // 返回主菜单：Paused（先暂停再返回，防误触）与结算卡各一个入口；Playing 不显示。
    if (btnPauseMenu) {
      btnPauseMenu.hidden = page !== 'Paused';
      btnPauseMenu.disabled = page !== 'Paused';
    }
    if (btnResultMenu) {
      btnResultMenu.hidden = page !== 'Result';
      btnResultMenu.disabled = page !== 'Result';
    }

    // M6 §2：Ready 也允许全屏（CSS 已改成按 body[data-page] 隐藏，Ready 会保留难度区 + 开始）。
    // Result 不进全屏：结算卡在页面里，全屏只剩画面会让玩家卡住；退出按钮单独控制。
    if (btnFullscreen) {
      btnFullscreen.hidden = page !== 'Ready' && page !== 'Starting' &&
        page !== 'Playing' && page !== 'Paused';
    }

    nicknameInput.disabled = page !== 'Ready';
  }

  function setPage(page) {
    state.page = page;
    // CSS 用 body[data-page] 区分「局内全屏只留画面」与「Ready 全屏保留难度/开始」（M6 §2 全屏修复）。
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

    // 键盘事件要落在画布上（Unity 6 从 document 收键盘，但焦点放对才不会被昵称输入框抢走）。
    if (canvas && typeof canvas.focus === 'function') {
      canvas.focus();
    }

    // 顺序不能反：Unity 的 SendMessage 是同步的，onStateChange(Playing) 会在
    // sendMessage() 返回前到达。若先发指令再设 Starting + 兜底计时器，就会把刚到的
    // Playing 覆盖回 Starting，兜底到期再把页面打回 Ready（游戏其实已在跑）。
    setHint('正在开始…');
    setPage('Starting');

    // 兜底只提示、不改状态：Unity 可能已经在跑，只是状态回调没到。
    // 打回 Ready 会让玩家误以为没开始而重复点击；保持 Starting 即可（开始按钮禁用）。
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

  /**
   * 中止本局回主菜单（协议 v2 §2）。Unity 收到后清场回 Ready，**不发 onGameOver**，
   * 所以最高分不会被中止局污染；难度保持上次选择，可直接改档再开始。
   */
  function returnToMenu() {
    if (state.page !== 'Paused' && state.page !== 'Result') {
      return;
    }

    // 与 startGame 同理：SendMessage 是同步的，先写提示再发，避免 Ready 回调被覆盖。
    setHint('正在返回主菜单…');
    if (!sendMessage('ReturnToMenu', '')) {
      setHint('返回指令发送失败，请刷新页面重试');
      return;
    }

    window.setTimeout(function () {
      if (state.page === 'Paused' || state.page === 'Result') {
        setHint('返回无响应，请刷新页面重试');
      }
    }, 1500);
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
        state.gameOverHandled = false;
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

  function updateFullscreenUi() {
    var active = isFullscreen();
    if (btnExitFullscreen) btnExitFullscreen.hidden = !active;
    if (btnFullscreen) btnFullscreen.textContent = active ? '退出全屏' : '全屏';

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

  function exitFullscreen() {
    var exit = document.exitFullscreen || document.webkitExitFullscreen;
    if (!exit) return;
    try {
      var result = exit.call(document);
      if (result && typeof result.catch === 'function') result.catch(function () {});
    } catch (error) {
      // 已经不在全屏，忽略。
    }
  }

  function toggleFullscreen() {
    if (isFullscreen()) exitFullscreen();
    else requestFullscreen();
  }

  // ---------- 初始化 ----------

  if (btnFullscreen) btnFullscreen.addEventListener('click', toggleFullscreen);
  if (btnExitFullscreen) btnExitFullscreen.addEventListener('click', toggleFullscreen);
  document.addEventListener('fullscreenchange', updateFullscreenUi);
  document.addEventListener('webkitfullscreenchange', updateFullscreenUi);
  updateFullscreenUi();

  if (btnStart) btnStart.addEventListener('click', startGame);
  if (btnRestart) btnRestart.addEventListener('click', restartGame);
  if (btnPauseMenu) btnPauseMenu.addEventListener('click', returnToMenu);
  if (btnResultMenu) btnResultMenu.addEventListener('click', returnToMenu);

  for (var inputIndex = 0; inputIndex < difficultyInputs.length; inputIndex++) {
    difficultyInputs[inputIndex].addEventListener('change', function () {
      if (this.checked) selectDifficulty(this.value);
    });
  }

  // 旋转/改窗口后重新判断「高竖屏是否保留菜单」；matchMedia 的 change 覆盖主要场景，resize 兜底。
  function refreshMenuForViewport() {
    renderButtons(state.page);
  }

  if (tallPortrait) {
    if (typeof tallPortrait.addEventListener === 'function') {
      tallPortrait.addEventListener('change', refreshMenuForViewport);
    } else if (typeof tallPortrait.addListener === 'function') {
      tallPortrait.addListener(refreshMenuForViewport);
    }
  }
  window.addEventListener('resize', refreshMenuForViewport);
  window.addEventListener('orientationchange', refreshMenuForViewport);

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
