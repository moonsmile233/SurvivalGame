using Survival.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Survival.UI
{
    /// <summary>
    /// 暂停菜单（v1.2）：只在 Paused 显示；继续 / 重新开始 / 返回菜单。
    /// 3-2-1 已移到 GameManager + StartCountdownPanel 统一处理（菜单开始、暂停继续、重开、再来一局共用），
    /// 本面板不再计时、不碰 Time.timeScale。
    /// </summary>
    public class PausePanel : MonoBehaviour
    {
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private GameObject _panel;
        [SerializeField] private Button _resumeButton;
        [SerializeField] private Button _restartButton;
        [SerializeField] private Button _menuButton;

        private void Start()
        {
            if (_gameManager == null || _panel == null || _resumeButton == null
                || _restartButton == null || _menuButton == null)
            {
                Debug.LogError("[PausePanel] Missing serialized references, component disabled.");
                enabled = false;
                return;
            }

            _gameManager.OnStateChanged += HandleStateChanged;
            _resumeButton.onClick.AddListener(_gameManager.RequestResumeCountdown);
            _restartButton.onClick.AddListener(_gameManager.RestartGame);
            _menuButton.onClick.AddListener(_gameManager.ReturnToMenu);
            HandleStateChanged(_gameManager.State);
        }

        private void OnDestroy()
        {
            if (_gameManager != null)
            {
                _gameManager.OnStateChanged -= HandleStateChanged;
            }

            if (_resumeButton != null)
            {
                _resumeButton.onClick.RemoveAllListeners();
            }

            if (_restartButton != null)
            {
                _restartButton.onClick.RemoveAllListeners();
            }

            if (_menuButton != null)
            {
                _menuButton.onClick.RemoveAllListeners();
            }
        }

        private void HandleStateChanged(GameState state)
        {
            _panel.SetActive(state == GameState.Paused);
        }
    }
}
