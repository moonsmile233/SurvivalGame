using Survival.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Survival.UI
{
    /// <summary>
    /// Unity 主菜单（v1.2 游戏优先）：三档难度 + 开始。
    /// 只改选择并发 StartGame；状态与难度权威在 GameManager/GameConfig。
    /// 难度记忆在会话内（策划裁决 2026-11-01：不用 PlayerPrefs）：standalone 刷新回普通；
    /// H5 会用 EnterGame.lastDifficulty 回填，返回菜单沿用上次选择。菜单不显示最高分/昵称/全屏（用户口径）。
    /// 最高分与昵称只属于 H5 外壳。
    /// </summary>
    public class MenuPanel : MonoBehaviour
    {
        private static readonly Color SelectedColor = new Color(0.42f, 0.30f, 0.88f, 1f);
        private static readonly Color NormalColor = new Color(0.13f, 0.13f, 0.20f, 1f);

        [SerializeField] private GameManager _gameManager;
        [SerializeField] private GameObject _panel;
        [SerializeField] private Button _startButton;

        // 顺序固定 Easy / Normal / Hard，与 Difficulty 枚举下标一致（SceneSetup 生成时保证）。
        [SerializeField] private Button[] _difficultyButtons = new Button[3];
        [SerializeField] private Image[] _difficultyBackgrounds = new Image[3];

        private Difficulty _selected = Difficulty.Normal;

        private void Start()
        {
            if (_gameManager == null || _panel == null || _startButton == null
                || _difficultyButtons == null || _difficultyButtons.Length != 3
                || _difficultyBackgrounds == null || _difficultyBackgrounds.Length != 3)
            {
                Debug.LogError("[MenuPanel] Missing serialized references, component disabled.");
                enabled = false;
                return;
            }

            for (int i = 0; i < _difficultyButtons.Length; i++)
            {
                int index = i;
                if (_difficultyButtons[i] != null)
                {
                    _difficultyButtons[i].onClick.AddListener(() => Select((Difficulty)index));
                }
            }

            _startButton.onClick.AddListener(HandleStart);
            _gameManager.OnStateChanged += HandleStateChanged;
            _gameManager.OnMenuDifficultyChanged += HandleMenuDifficultyChanged;
            HandleStateChanged(_gameManager.State);
        }

        private void OnDestroy()
        {
            if (_gameManager != null)
            {
                _gameManager.OnStateChanged -= HandleStateChanged;
                _gameManager.OnMenuDifficultyChanged -= HandleMenuDifficultyChanged;
            }

            if (_startButton != null)
            {
                _startButton.onClick.RemoveAllListeners();
            }

            if (_difficultyButtons != null)
            {
                foreach (Button button in _difficultyButtons)
                {
                    if (button != null)
                    {
                        button.onClick.RemoveAllListeners();
                    }
                }
            }
        }

        private void HandleStateChanged(GameState state)
        {
            _panel.SetActive(state == GameState.Menu);

            if (state == GameState.Menu)
            {
                // 会话内 / H5 回填的偏好；StartGame 之后也保持同一档。
                _selected = _gameManager.MenuDifficulty;
                ApplySelection();
            }
        }

        private void HandleMenuDifficultyChanged(Difficulty difficulty)
        {
            _selected = difficulty;
            ApplySelection();
        }

        private void Select(Difficulty difficulty)
        {
            // 状态权威在 GameManager；它变化后会回调 HandleMenuDifficultyChanged。
            _gameManager.SetMenuDifficulty(difficulty);
            _selected = difficulty;
            ApplySelection();
        }

        private void ApplySelection()
        {
            for (int i = 0; i < _difficultyBackgrounds.Length; i++)
            {
                if (_difficultyBackgrounds[i] != null)
                {
                    _difficultyBackgrounds[i].color = (int)_selected == i ? SelectedColor : NormalColor;
                }
            }
        }

        private void HandleStart()
        {
            _gameManager.StartGame(_selected);
        }
    }
}
