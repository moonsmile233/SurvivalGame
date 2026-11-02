using Survival.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Survival.UI
{
    /// <summary>
    /// 结算面板（v1.2）：Result 状态显示本局得分 / 难度 / 存活 / 倍率 + 再来一局 / 返回菜单。
    /// **始终显示**（不再有 H5 嵌入模式抑制）：结算归 Unity，H5 只展示最高分/最近一局。
    /// 不显示昵称与分数明细（策划裁决 2026-11-01：按 mock，明细字段保留在 ScoreService，不上屏）。
    /// 不写分数算法，数值全部读 ScoreService / GameConfig。
    /// </summary>
    public class ResultPanel : MonoBehaviour
    {
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private GameObject _panel;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _scoreText;
        [SerializeField] private TMP_Text _infoText;
        [SerializeField] private Button _againButton;
        [SerializeField] private Button _menuButton;

        private void Start()
        {
            if (_gameManager == null || _panel == null || _titleText == null
                || _scoreText == null || _infoText == null || _againButton == null || _menuButton == null)
            {
                Debug.LogError("[ResultPanel] Missing serialized references, component disabled.");
                enabled = false;
                return;
            }

            _gameManager.OnStateChanged += HandleStateChanged;
            _againButton.onClick.AddListener(_gameManager.RestartGame);
            _menuButton.onClick.AddListener(_gameManager.ReturnToMenu);
            HandleStateChanged(_gameManager.State);
        }

        private void OnDestroy()
        {
            if (_gameManager != null)
            {
                _gameManager.OnStateChanged -= HandleStateChanged;
            }

            if (_againButton != null)
            {
                _againButton.onClick.RemoveAllListeners();
            }

            if (_menuButton != null)
            {
                _menuButton.onClick.RemoveAllListeners();
            }
        }

        private void HandleStateChanged(GameState state)
        {
            bool show = state == GameState.Result;
            _panel.SetActive(show);

            if (!show || _gameManager.Score == null)
            {
                return;
            }

            bool won = _gameManager.LastRoundWon;
            ScoreService score = _gameManager.Score;
            GameConfig config = _gameManager.Config;

            _titleText.text = won ? UiStrings.VictoryTitle : UiStrings.DefeatTitle;
            _scoreText.text = $"{UiStrings.RoundScoreLabel} {score.Score}";

            int survived = Mathf.RoundToInt(
                Mathf.Min(_gameManager.ElapsedTime, config != null ? config.RoundDuration : _gameManager.ElapsedTime));
            float multiplier = config != null ? config.ScoreMultiplier : 1f;

            _infoText.text =
                $"{UiStrings.DifficultyLabel} {DifficultyName(config != null ? config.ActiveDifficulty : Difficulty.Normal)}" +
                $"{UiStrings.InfoSeparator}{UiStrings.SurvivedLabel} {survived} {UiStrings.SecondsSuffix}" +
                $"{UiStrings.InfoSeparator}{UiStrings.MultiplierLabel} {UiStrings.TimesSign}{multiplier:0.0}";
        }

        /// <summary>档位中文名只在 Unity UI 用；H5 不显示 Unity 的难度文案。</summary>
        private static string DifficultyName(Difficulty difficulty)
        {
            switch (difficulty)
            {
                case Difficulty.Easy:
                    return UiStrings.DifficultyEasy;
                case Difficulty.Hard:
                    return UiStrings.DifficultyHard;
                default:
                    return UiStrings.DifficultyNormal;
            }
        }
    }
}
