using System;
using Survival.Enemy;
using Survival.Player;
using UnityEngine;

namespace Survival.Core
{
    /// <summary>
    /// 唯一能改状态的类：计时、胜负、重开（规范 5.1）。
    /// 不处理输入、不写分数算法。
    /// 状态迁移：Ready → Playing → (Won | Lost) → Playing(Restart)；Playing ↔ Paused。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [SerializeField] private GameConfig _config;
        [SerializeField] private PlayerController _playerController;
        [SerializeField] private PlayerHealth _playerHealth;
        [SerializeField] private EnemySpawner _enemySpawner;

        [Tooltip("仅编辑器自测用：Play 后自动进入 Playing。M3 起默认关闭，由 H5 的 StartGame 驱动。")]
        [SerializeField] private bool _autoStart = false;

        private float _elapsed;

        public event Action<GameState> OnStateChanged;
        public event Action<float> OnTimeChanged;

        /// <summary>
        /// 切回页面/窗口时的「延时恢复」请求：PausePanel 收到后走 3-2-1 倒计时再 Resume，
        /// 避免切回来的一瞬间就被围死（M4 第 1.5 节）。只在 Paused 时有效。
        /// </summary>
        public event Action OnResumeCountdownRequested;

        /// <summary>
        /// 倒计时期间再次失焦：取消这次延时恢复，避免在玩家看不见的时候自动继续。
        /// PausePanel 收到后停止倒计时；下次切回重新从 3 开始。
        /// </summary>
        public event Action OnResumeCountdownCancelled;

        public GameState State { get; private set; } = GameState.Booting;
        public ScoreService Score { get; private set; }
        public GameConfig Config => _config;
        public float ElapsedTime => _elapsed;

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
            SetState(GameState.Ready);
        }

        private void Start()
        {
            if (_autoStart)
            {
                StartGame();
            }
        }

        private void Update()
        {
            if (State == GameState.Playing)
            {
                _elapsed += Time.deltaTime;
                Score.SetSurvivedSeconds((int)_elapsed);
                OnTimeChanged?.Invoke(RemainingTime);

                if (_elapsed >= _config.RoundDuration)
                {
                    Win();
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

        /// <summary>编辑器/自测入口：在 Play 之前调用即可恢复 M1 的自动开局行为。</summary>
        public void SetAutoStart(bool enabled)
        {
            _autoStart = enabled;
        }

        /// <summary>Ready → Playing（M3 由 H5 StartGame 调用）。</summary>
        public void StartGame()
        {
            if (State != GameState.Ready)
            {
                return;
            }

            ResetRound();
            SetState(GameState.Playing);
            Debug.Log("[GameManager] state=Playing");
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

        /// <summary>失焦恢复：不立即继续，交给 PausePanel 倒计时（M4 第 1.5 节）。</summary>
        public void RequestCountdownResume()
        {
            if (State != GameState.Paused)
            {
                return;
            }

            OnResumeCountdownRequested?.Invoke();
        }

        /// <summary>倒计时期间再次失焦：取消倒计时，暂停状态保持不变。</summary>
        public void CancelCountdownResume()
        {
            if (State != GameState.Paused)
            {
                return;
            }

            OnResumeCountdownCancelled?.Invoke();
        }

        /// <summary>Paused → Playing。</summary>
        public void ResumeGame()
        {
            if (State != GameState.Paused)
            {
                return;
            }

            Time.timeScale = 1f;
            SetState(GameState.Playing);
            Debug.Log("[GameManager] state=Playing (resume)");
        }

        /// <summary>Won / Lost → Playing，全量重置（不重载场景）。</summary>
        public void RestartGame()
        {
            if (State != GameState.Won && State != GameState.Lost)
            {
                return;
            }

            ResetRound();
            SetState(GameState.Playing);
            Debug.Log("[GameManager] state=Playing (restart)");
        }

        private void ResetRound()
        {
            Time.timeScale = 1f;
            _elapsed = 0f;
            Score.Reset();
            _playerHealth.ResetHealth();

            if (_playerController != null)
            {
                _playerController.ResetToStart();
            }

            _enemySpawner.BeginRound();
            OnTimeChanged?.Invoke(RemainingTime);
        }

        /// <summary>计时到达 60 秒。</summary>
        private void Win()
        {
            if (State != GameState.Playing)
            {
                return;
            }

            _elapsed = _config.RoundDuration;
            Score.SetSurvivedSeconds((int)_elapsed);
            Score.SetWon(true);
            _enemySpawner.StopSpawning();
            Time.timeScale = 0f;
            OnTimeChanged?.Invoke(0f);
            SetState(GameState.Won);
            Debug.Log($"[GameManager] state=Won survived={_elapsed:F1}s score={Score.Score}");
        }

        private void HandlePlayerDied()
        {
            if (State != GameState.Playing)
            {
                return;
            }

            _enemySpawner.StopSpawning();
            Time.timeScale = 0f;
            SetState(GameState.Lost);
            Debug.Log($"[GameManager] state=Lost survived={_elapsed:F1}s score={Score.Score}");
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
