using Survival.Core;
using Survival.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Survival.UI
{
    /// <summary>
    /// 常驻 HUD：HP 条 / HP 数字 / 剩余时间 / 分数 / 暂停按钮（ticket M2 第 2 节）。
    /// 只订阅事件刷新显示，不改状态机、不写分数算法。文案见 <see cref="UiStrings"/>。
    /// </summary>
    public class HudView : MonoBehaviour
    {
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private PlayerHealth _playerHealth;

        [Tooltip("HUD 根节点；Menu 状态整体隐藏（v1.2 主菜单不显示 HUD）。")]
        [SerializeField] private GameObject _hudRoot;
        [SerializeField] private Image _hpFill;
        [SerializeField] private TMP_Text _hpText;
        [SerializeField] private TMP_Text _timeText;
        [SerializeField] private TMP_Text _scoreText;
        [SerializeField] private Button _pauseButton;

        // 只在整数变化时重建字符串（ticket M2「性能」）。
        private int _lastSecond = int.MinValue;
        private int _lastScore = int.MinValue;
        private int _lastHealth = int.MinValue;

        // 只给 PerfProbe 的 alloc 归因用（?noHud=1）：压住 TMP 文本重建，量化 HUD 的分配占比。
        // 正常游玩恒为 false；只影响显示刷新，不改任何玩法数值。
        private bool _perfSuppressUpdates;

        /// <summary>只给 PerfProbe 用：暂停 HUD 文本刷新（alloc 归因）；正常游玩不调用。</summary>
        public void SetPerfSuppressUpdates(bool value) => _perfSuppressUpdates = value;

        private void Start()
        {
            if (_gameManager == null || _playerHealth == null || _gameManager.Score == null
                || _hudRoot == null)
            {
                Debug.LogError("[HudView] Missing serialized references, component disabled.");
                enabled = false;
                return;
            }

            _gameManager.OnTimeChanged += HandleTimeChanged;
            _gameManager.OnStateChanged += HandleStateChanged;
            _gameManager.Score.OnScoreChanged += HandleScoreChanged;
            _playerHealth.OnHealthChanged += HandleHealthChanged;

            if (_pauseButton != null)
            {
                _pauseButton.onClick.AddListener(_gameManager.PauseGame);
            }

            // 主动读一次当前值，避免晚于事件源初始化时显示空白。
            HandleStateChanged(_gameManager.State);
            HandleHealthChanged(_playerHealth.CurrentHealth, _playerHealth.MaxHealth);
            HandleScoreChanged(_gameManager.Score.DisplayScore);
            HandleTimeChanged(_gameManager.RemainingTime);
        }

        private void OnDestroy()
        {
            if (_gameManager != null)
            {
                _gameManager.OnTimeChanged -= HandleTimeChanged;
                _gameManager.OnStateChanged -= HandleStateChanged;

                if (_gameManager.Score != null)
                {
                    _gameManager.Score.OnScoreChanged -= HandleScoreChanged;
                }
            }

            if (_playerHealth != null)
            {
                _playerHealth.OnHealthChanged -= HandleHealthChanged;
            }

            if (_pauseButton != null)
            {
                _pauseButton.onClick.RemoveAllListeners();
            }
        }

        /// <summary>
        /// 剩余时间向上取整，按标准 mm:ss 显示（规范 6.1）：60 秒 → 01:00，575 秒 → 09:35。
        /// 不能硬编码成 `00:{seconds:00}`：压测会把回合拉到 600 秒，那种写法会显示成 00:575。
        /// </summary>
        private void HandleTimeChanged(float remainingSeconds)
        {
            int seconds = Mathf.CeilToInt(Mathf.Max(0f, remainingSeconds));
            if (seconds == _lastSecond || _perfSuppressUpdates)
            {
                return;
            }

            _lastSecond = seconds;
            int minutes = seconds / 60;
            int remainder = seconds % 60;
            _timeText.text = $"{UiStrings.TimeLabel} {minutes:00}:{remainder:00}";
        }

        private void HandleScoreChanged(int score)
        {
            if (score == _lastScore || _perfSuppressUpdates)
            {
                return;
            }

            _lastScore = score;
            _scoreText.text = $"{UiStrings.ScoreLabel} {score}";
        }

        private void HandleHealthChanged(int current, int max)
        {
            if (current == _lastHealth || _perfSuppressUpdates)
            {
                return;
            }

            _lastHealth = current;
            _hpText.text = $"{UiStrings.HealthLabel} {current}/{max}";
            _hpFill.fillAmount = max > 0 ? (float)current / max : 0f;
        }

        private void HandleStateChanged(GameState state)
        {
            // 菜单与结算不显示 HUD：结算卡在小视口（320 宽）会和大号时间/分数压在一起。
            // 暂停仍保留 HUD（玩家需要确认血量/剩余时间）。
            _hudRoot.SetActive(state != GameState.Menu && state != GameState.Result);

            if (_pauseButton != null)
            {
                _pauseButton.gameObject.SetActive(state == GameState.Playing);
            }
        }
    }
}
