using Survival.Core;
using TMPro;
using UnityEngine;

namespace Survival.UI
{
    /// <summary>
    /// 统一 3-2-1 倒计时显示（v1.2）：菜单开始、暂停继续、暂停重开、结算再来一局都经 GameManager 的
    /// Starting 状态，本面板只画遮罩 + 数字。
    /// 不自己计时、不碰 timeScale/状态；倒计时中被取消（失焦）由 GameManager 处理。
    /// </summary>
    public class StartCountdownPanel : MonoBehaviour
    {
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private GameObject _panel;
        [SerializeField] private TMP_Text _countdownText;

        private void Start()
        {
            if (_gameManager == null || _panel == null || _countdownText == null)
            {
                Debug.LogError("[StartCountdownPanel] Missing serialized references, component disabled.");
                enabled = false;
                return;
            }

            _gameManager.OnStateChanged += HandleStateChanged;
            _gameManager.OnCountdownChanged += HandleCountdownChanged;
            HandleStateChanged(_gameManager.State);
        }

        private void OnDestroy()
        {
            if (_gameManager != null)
            {
                _gameManager.OnStateChanged -= HandleStateChanged;
                _gameManager.OnCountdownChanged -= HandleCountdownChanged;
            }
        }

        private void HandleStateChanged(GameState state)
        {
            bool starting = state == GameState.Starting;
            _panel.SetActive(starting);

            if (starting)
            {
                UpdateText(Mathf.CeilToInt(_gameManager.CountdownRemaining));
            }
        }

        private void HandleCountdownChanged(int seconds)
        {
            if (_gameManager.State != GameState.Starting)
            {
                return;
            }

            UpdateText(seconds);
        }

        private void UpdateText(int seconds)
        {
            _countdownText.text = Mathf.Max(0, seconds).ToString();
        }
    }
}
