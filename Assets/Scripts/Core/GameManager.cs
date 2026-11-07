using System;
using Survival.Enemy;
using Survival.Player;
using UnityEngine;

namespace Survival.Core
{
    /// <summary>
    /// 唯一能改状态的类：计时、胜负、重开、统一 3-2-1（规范 5.1）。不处理输入、不写分数算法。
    ///
    /// 状态迁移（v1.2）：
    /// Menu → Starting → Playing（开始）；
    /// Paused → Starting → Playing（继续 / 重新开始）；
    /// Result → Starting → Playing（再来一局）；
    /// Playing ↔ Paused；Playing/Paused → Result；
    /// Starting → 来源态（失焦取消）；Playing/Paused/Result → Menu。
    ///
    /// Starting 是内部过渡态：Time.timeScale = 0，只跑 unscaled 倒计时；不推 onStateChange。
    /// 取消回来源态时必须复位 timeScale（Menu=1，Paused/Result=0），否则回到暂停/结算会“解冻”。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        private const float StartCountdownSeconds = 3f;

        // 从后台切回时首帧的 unscaledDeltaTime 可能是一整段隐藏时长（rAF 被浏览器节流），
        // 不夹住会让 3-2-1 一帧走完、等于瞬时开局/恢复（M4.6 教训）。
        private const float MaxCountdownStep = 0.1f;

        [SerializeField] private GameConfig _config;
        [SerializeField] private PlayerController _playerController;
        [SerializeField] private PlayerHealth _playerHealth;
        [SerializeField] private EnemySpawner _enemySpawner;

        [Tooltip("仅编辑器自测用：Play 后自动按普通档开局（仍走 3-2-1）。正常由菜单驱动。")]
        [SerializeField] private bool _autoStart = false;

        private float _elapsed;
        private float _countdownRemaining;
        private int _lastCountdownShown = int.MinValue;
        private GameState _countdownReturnState = GameState.Menu;
        private bool _countdownResetsRound;

        public event Action<GameState> OnStateChanged;
        public event Action<float> OnTimeChanged;

        /// <summary>倒计时显示秒数；只在整数变化时触发，供 StartCountdownPanel 刷新。</summary>
        public event Action<int> OnCountdownChanged;

        /// <summary>
        /// 菜单难度选择变化（玩家点卡或 H5 通过 EnterGame 回填）。Bridge 监听它发 onDifficultyChanged；
        /// MenuPanel 监听它刷新高亮。只在**变化**时触发。
        /// </summary>
        public event Action<Difficulty> OnMenuDifficultyChanged;

        public GameState State { get; private set; } = GameState.Booting;
        public bool LastRoundWon { get; private set; }

        /// <summary>
        /// 菜单当前选中的难度（会话内记忆）。首次启动=普通；H5 可在 EnterGame 里用 lastDifficulty 回填；
        /// 返回菜单沿用上次选择；暂停重开/再来一局不弹选择（用户 2026-11-01 口径）。
        /// </summary>
        public Difficulty MenuDifficulty { get; private set; } = Difficulty.Normal;

        public ScoreService Score { get; private set; }
        public GameConfig Config => _config;

        /// <summary>桥接层读结算所需的受击次数（PlayerHealth 是单一事实来源）。</summary>
        public PlayerHealth Health => _playerHealth;
        public float ElapsedTime => _elapsed;
        public float CountdownRemaining => _countdownRemaining;

        public float RemainingTime =>
            _config != null ? Mathf.Max(0f, _config.RoundDuration - _elapsed) : 0f;

        private void Awake()
        {
            if (_config == null || _playerHealth == null || _enemySpawner == null)
            {
                Debug.LogError("[GameManager] Missing serialized references, component disabled.");
                enabled = false;
                return;
            }

            Score = new ScoreService(_config);
            _playerHealth.OnDied += HandlePlayerDied;
            SetState(GameState.Menu);
        }

        private void Start()
        {
            if (_autoStart)
            {
                StartGame(Difficulty.Normal);
            }
        }

        private void Update()
        {
            if (State == GameState.Starting)
            {
                TickStartCountdown();
                return;
            }

            if (State == GameState.Playing)
            {
                _elapsed += Time.deltaTime;
                // 每帧按 elapsed 纯函数重算；HUD 只在显示分变化时刷新（ScoreService 内部去重）。
                Score.Advance(_elapsed);
                OnTimeChanged?.Invoke(RemainingTime);

                if (_elapsed >= _config.RoundDuration)
                {
                    FinishRound(won: true);
                }
            }
        }

        private void OnDestroy()
        {
            if (_playerHealth != null)
            {
                _playerHealth.OnDied -= HandlePlayerDied;
            }
        }

        /// <summary>
        /// 设置菜单选中难度（H5 EnterGame 回填 / 菜单点卡）。只在值变化时触发
        /// <see cref="OnMenuDifficultyChanged"/>；Bridge 在回填时会抑制回声，避免与 H5 形成循环。
        /// </summary>
        public void SetMenuDifficulty(Difficulty difficulty)
        {
            if (MenuDifficulty == difficulty)
            {
                return;
            }

            MenuDifficulty = difficulty;
            OnMenuDifficultyChanged?.Invoke(difficulty);
        }

        /// <summary>
        /// Menu → Starting。难度**只在这里设置一次**并写入 GameConfig 的运行时字段（不写回资产）；
        /// 局内锁定，RestartGame 沿用。倒计时结束才真正 ResetRound + Playing。
        /// </summary>
        public void StartGame(Difficulty difficulty)
        {
            if (State != GameState.Menu)
            {
                return;
            }

            _config.SetActiveDifficulty(difficulty);
            SetMenuDifficulty(difficulty);
            ValidateDifficultyProfile();
            LastRoundWon = false;
            BeginStartCountdown(GameState.Menu, resetRoundOnFinish: true);
        }

        /// <summary>
        /// 测试/压测专用：跳过 3-2-1 直接 ResetRound → Playing。
        /// 只给 PerfProbe 与编辑器自动化用；正常 UI 一律走 <see cref="StartGame(Difficulty)"/>，
        /// 否则会绕过“所有进 Playing 的路径都要 3-2-1”的验收口径（策划裁决 2026-11-01 B）。
        /// **不修改 MenuDifficulty**：压测不经过菜单，不能把玩家代存的难度偏好写成 normal。
        /// </summary>
        public void StartGameImmediate(Difficulty difficulty)
        {
            if (State != GameState.Menu)
            {
                return;
            }

            _config.SetActiveDifficulty(difficulty);
            ValidateDifficultyProfile();
            LastRoundWon = false;
            ResetRound();
            SetState(GameState.Playing);
            Debug.Log($"[GameManager] state=Playing (immediate) difficulty={_config.ActiveDifficulty}");
        }

        /// <summary>Paused → Starting → Playing（玩家主动恢复；失焦切回不会走到这里）。</summary>
        public void RequestResumeCountdown()
        {
            if (State != GameState.Paused)
            {
                return;
            }

            BeginStartCountdown(GameState.Paused, resetRoundOnFinish: false);
        }

        /// <summary>
        /// 取消 3-2-1 回到来源态（失焦 / 旧 H5 的 SetPausedByVisibility）。
        /// 回 Menu 解冻（timeScale=1），回 Paused/Result 保持冻结（策划裁决 1）。
        /// </summary>
        public void CancelStartCountdown()
        {
            if (State != GameState.Starting)
            {
                return;
            }

            Time.timeScale = _countdownReturnState == GameState.Menu ? 1f : 0f;
            _countdownRemaining = 0f;
            GameState origin = _countdownReturnState;
            SetState(origin);
            Debug.Log($"[GameManager] state={origin} (countdown cancelled)");
        }

        /// <summary>Playing → Paused，统一 Time.timeScale = 0。</summary>
        public void PauseGame()
        {
            if (State != GameState.Playing)
            {
                return;
            }

            Time.timeScale = 0f;
            SetState(GameState.Paused);
            Debug.Log("[GameManager] state=Paused");
        }

        /// <summary>
        /// 中止本局并回到 Menu。允许从 Playing / Paused / Result 进入；Starting 会先取消倒计时。
        /// **不结算、不发 onGameOver**——桥接层只在 Result 发结算，所以中止局不会写最高分。
        /// 停在菜单时不能继续刷怪：ResetRound(beginRound:false) 只清场，不调用 BeginRound。
        /// </summary>
        public void ReturnToMenu()
        {
            if (State == GameState.Starting)
            {
                CancelStartCountdown();
            }

            if (State == GameState.Menu)
            {
                return;
            }

            _enemySpawner.StopSpawning();
            ResetRound(beginRound: false);
            LastRoundWon = false;
            SetState(GameState.Menu);
            Debug.Log("[GameManager] state=Menu (return to menu)");
        }

        /// <summary>
        /// Paused / Result → Starting → Playing，倒计时结束才全量重置（不重载场景）。
        /// 不在进入 Starting 时重置：取消倒计时要能原样回到暂停/结算（分数不能被清零）。
        /// </summary>
        public void RestartGame()
        {
            if (State != GameState.Paused && State != GameState.Result)
            {
                return;
            }

            // 不在这里清 LastRoundWon：倒计时可能被失焦取消并回到 Result，
            // 结算卡必须保持原来的胜负；真正的清零在倒计时结束的 ResetRound。
            BeginStartCountdown(State, resetRoundOnFinish: true);
        }

        /// <summary>
        /// GameConfig 的档位数组按枚举下标取（协议 v2 §2.2）。数组顺序被改错时数值会静默错档，
        /// 所以在开局时校验一次（每局一次，不进热路径）。
        /// </summary>
        private void ValidateDifficultyProfile()
        {
            DifficultyProfile profile = _config.ActiveProfile;
            if (profile != null && profile.Id != _config.ActiveDifficulty)
            {
                Debug.LogWarning(
                    $"[GameManager] 难度档案顺序不一致：active={_config.ActiveDifficulty} profile.id={profile.Id}，按数组位置生效");
            }
        }

        private void BeginStartCountdown(GameState returnState, bool resetRoundOnFinish)
        {
            _countdownReturnState = returnState;
            _countdownResetsRound = resetRoundOnFinish;
            _countdownRemaining = StartCountdownSeconds;

            // 冻结世界：计量/刷怪/敌人移动都用 Time.deltaTime，timeScale=0 时自然停住。
            Time.timeScale = 0f;
            SetState(GameState.Starting);

            // 立刻推一次数字，UI 不用等第一帧。
            _lastCountdownShown = Mathf.CeilToInt(StartCountdownSeconds);
            OnCountdownChanged?.Invoke(_lastCountdownShown);
            Debug.Log($"[GameManager] state=Starting return={returnState} reset={resetRoundOnFinish}");
        }

        private void TickStartCountdown()
        {
            _countdownRemaining -= Mathf.Min(Time.unscaledDeltaTime, MaxCountdownStep);
            int shown = Mathf.CeilToInt(Mathf.Max(0f, _countdownRemaining));

            if (shown != _lastCountdownShown)
            {
                _lastCountdownShown = shown;
                OnCountdownChanged?.Invoke(shown);
            }

            if (_countdownRemaining <= 0f)
            {
                BeginPlaying();
            }
        }

        private void BeginPlaying()
        {
            _countdownRemaining = 0f;

            if (_countdownResetsRound)
            {
                ResetRound();
            }
            else
            {
                Time.timeScale = 1f;
                OnTimeChanged?.Invoke(RemainingTime);
            }

            SetState(GameState.Playing);
            Debug.Log($"[GameManager] state=Playing difficulty={_config.ActiveDifficulty}");
        }

        /// <summary>
        /// 清空本局数据并复位玩家。`beginRound: false` 只清场、不开始刷怪（返回菜单用），
        /// 避免 BeginRound 的“begin difficulty”日志与瞬时刷怪窗口。
        /// </summary>
        private void ResetRound(bool beginRound = true)
        {
            Time.timeScale = 1f;
            _elapsed = 0f;
            LastRoundWon = false;
            Score.Reset();
            _playerHealth.ResetHealth();

            if (_playerController != null)
            {
                _playerController.ResetToStart();
            }

            if (beginRound)
            {
                _enemySpawner.BeginRound();
            }
            else
            {
                _enemySpawner.ClearEnemies();
            }

            OnTimeChanged?.Invoke(RemainingTime);
        }

        private void HandlePlayerDied()
        {
            FinishRound(won: false);
        }

        /// <summary>Playing → Result：停刷、冻结、记胜负；分数由 ScoreService 保持。</summary>
        private void FinishRound(bool won)
        {
            if (State != GameState.Playing)
            {
                return;
            }

            LastRoundWon = won;

            if (won)
            {
                // 胜利的存活分固定按满回合计算，不依赖最后一帧的浮点（M6 §4.1）。
                _elapsed = _config.RoundDuration;
            }

            Score.Finish(_elapsed, _playerHealth.CurrentHealth, won);

            if (won)
            {
                OnTimeChanged?.Invoke(0f);
            }

            _enemySpawner.StopSpawning();
            Time.timeScale = 0f;
            SetState(GameState.Result);
            Debug.Log(
                $"[GameManager] state=Result won={won} survived={_elapsed:F1}s score={Score.FinalScore} " +
                $"hit={_playerHealth.HitCount} hp={_playerHealth.CurrentHealth}");
        }

        private void SetState(GameState next)
        {
            if (State == next)
            {
                return;
            }

            State = next;
            OnStateChanged?.Invoke(next);
        }
    }
}
