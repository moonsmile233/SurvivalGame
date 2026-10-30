using Survival.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Survival.UI
{
    /// <summary>
    /// 暂停面板：只在 Paused 显示；「继续」调 GameManager.ResumeGame（ticket M2 第 2 节）。
    /// M4 起增加 3-2-1 倒计时：切回页面时由 GameManager.OnResumeCountdownRequested 触发，
    /// 倒计时结束才继续，避免切回来瞬间被敌人围死（M4 第 1.5 节）。
    /// 不直接改 Time.timeScale；暂停时 timeScale = 0，倒计时必须用 Time.unscaledDeltaTime。
    /// </summary>
    public class PausePanel : MonoBehaviour
    {
        private const float CountdownSeconds = 3f;

        // 从后台切回时首帧的 unscaledDeltaTime 可能是一整段隐藏时长（rAF 被浏览器节流），
        // 不夹住会让 3-2-1 一帧走完、等于瞬时恢复。
        private const float MaxCountdownStep = 0.1f;

        [SerializeField] private GameManager _gameManager;
        [SerializeField] private GameObject _panel;
        [SerializeField] private Button _resumeButton;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _countdownText;

        private float _countdown;
        private int _lastShown = int.MinValue;
        private bool _countingDown;

        private void Start()
        {
            if (_gameManager == null || _panel == null || _resumeButton == null
                || _titleText == null || _countdownText == null)
            {
                Debug.LogError("[PausePanel] Missing serialized references, component disabled.");
                enabled = false;
                return;
            }

            _gameManager.OnStateChanged += HandleStateChanged;
            _gameManager.OnResumeCountdownRequested += StartCountdown;
            _gameManager.OnResumeCountdownCancelled += StopCountdown;
            _resumeButton.onClick.AddListener(_gameManager.ResumeGame);
            HandleStateChanged(_gameManager.State);
        }

        private void OnDestroy()
        {
            if (_gameManager != null)
            {
                _gameManager.OnStateChanged -= HandleStateChanged;
                _gameManager.OnResumeCountdownRequested -= StartCountdown;
                _gameManager.OnResumeCountdownCancelled -= StopCountdown;
            }

            if (_resumeButton != null)
            {
                _resumeButton.onClick.RemoveAllListeners();
            }
        }

        private void Update()
        {
            if (!_countingDown)
            {
                return;
            }

            // 暂停状态下 timeScale = 0，Time.deltaTime 恒为 0；必须用 unscaled。
            _countdown -= Mathf.Min(Time.unscaledDeltaTime, MaxCountdownStep);
            int shown = Mathf.CeilToInt(_countdown);

            if (shown <= 0)
            {
                StopCountdown();
                _gameManager.ResumeGame();
                return;
            }

            if (shown != _lastShown)
            {
                _lastShown = shown;
                _countdownText.text = shown.ToString();
            }
        }

        private void HandleStateChanged(GameState state)
        {
            bool paused = state == GameState.Paused;
            _panel.SetActive(paused);

            // 任何离开 Paused 的路径（继续按钮 / H5 指令 / 重开）都要中断倒计时。
            if (!paused)
            {
                StopCountdown();
            }
        }

        private void StartCountdown()
        {
            if (_gameManager.State != GameState.Paused || _countingDown)
            {
                return;
            }

            _countingDown = true;
            _countdown = CountdownSeconds;
            _lastShown = int.MinValue;

            // 窄屏（手机）上大号倒计时会和「已暂停」叠在一起，倒计时期间隐藏标题。
            _titleText.gameObject.SetActive(false);
            _countdownText.gameObject.SetActive(true);
            _resumeButton.gameObject.SetActive(false);
            _countdownText.text = Mathf.CeilToInt(CountdownSeconds).ToString();
            _lastShown = Mathf.CeilToInt(CountdownSeconds);
        }

        private void StopCountdown()
        {
            _countingDown = false;

            if (_countdownText != null)
            {
                _countdownText.gameObject.SetActive(false);
            }

            if (_resumeButton != null)
            {
                _resumeButton.gameObject.SetActive(true);
            }

            if (_titleText != null)
            {
                _titleText.gameObject.SetActive(true);
            }
        }
    }
}
