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

        [Tooltip("M1 自测用：Play 后自动进入 Playing。M3 接 H5 后应关闭，由 StartGame 驱动。")]
        [SerializeField] private bool _autoStart = true;

        private float _elapsed;

        public event Action<GameState> OnStateChanged;
        public event Action<float> OnTimeChanged;

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

            // M1 临时调试键：M2 起由暂停面板 / H5 调用 PauseGame / ResumeGame 替代。
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (State == GameState.Playing)
                {
                    PauseGame();
                }
                else if (State == GameState.Paused)
                {
                    ResumeGame();
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

        /// <summary>Ready → Playing（M1 的 Play 入口；M3 由 H5 StartGame 调用）。</summary>
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
