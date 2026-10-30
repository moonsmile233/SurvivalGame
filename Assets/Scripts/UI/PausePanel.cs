using Survival.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Survival.UI
{
    /// <summary>
    /// 暂停面板：只在 Paused 显示；RESUME 调 GameManager.ResumeGame（ticket M2 第 2 节）。
    /// 不直接改 Time.timeScale。
    /// </summary>
    public class PausePanel : MonoBehaviour
    {
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private GameObject _panel;
        [SerializeField] private Button _resumeButton;

        private void Start()
        {
            if (_gameManager == null || _panel == null || _resumeButton == null)
            {
                Debug.LogError("[PausePanel] Missing serialized references, component disabled.");
                enabled = false;
                return;
            }

            _gameManager.OnStateChanged += HandleStateChanged;
            _resumeButton.onClick.AddListener(_gameManager.ResumeGame);
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
        }

        private void HandleStateChanged(GameState state)
        {
            _panel.SetActive(state == GameState.Paused);
        }
    }
}
