using Survival.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Survival.UI
{
    /// <summary>
    /// 结算面板：Won / Lost 时显示胜负、总分、明细；「重新开始」调 GameManager.RestartGame
    /// （ticket M2 第 2 节）。不写分数算法，明细全部读 ScoreService，文案见 <see cref="UiStrings"/>。
    /// </summary>
    public class ResultPanel : MonoBehaviour
    {
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private GameObject _panel;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _scoreText;
        [SerializeField] private TMP_Text _breakdownText;
        [SerializeField] private Button _restartButton;

        private void Start()
        {
            if (_gameManager == null || _panel == null || _titleText == null
                || _scoreText == null || _breakdownText == null || _restartButton == null)
            {
                Debug.LogError("[ResultPanel] Missing serialized references, component disabled.");
                enabled = false;
                return;
            }

            _gameManager.OnStateChanged += HandleStateChanged;
            _restartButton.onClick.AddListener(_gameManager.RestartGame);
            HandleStateChanged(_gameManager.State);
        }

        private void OnDestroy()
        {
            if (_gameManager != null)
            {
                _gameManager.OnStateChanged -= HandleStateChanged;
            }

            if (_restartButton != null)
            {
                _restartButton.onClick.RemoveAllListeners();
            }
        }

        private void HandleStateChanged(GameState state)
        {
            bool show = state == GameState.Won || state == GameState.Lost;
            _panel.SetActive(show);

            if (!show || _gameManager.Score == null)
            {
                return;
            }

            bool won = state == GameState.Won;
            ScoreService score = _gameManager.Score;

            _titleText.text = won ? UiStrings.VictoryTitle : UiStrings.DefeatTitle;
            _scoreText.text = $"{UiStrings.FinalScoreLabel} {score.Score}";
            _breakdownText.text = BuildBreakdown(score, won);
        }

        /// <summary>击杀为 0 时不显示该行；「胜利奖励」只在胜利时显示（ticket M2 第 3 节）。</summary>
        private static string BuildBreakdown(ScoreService score, bool won)
        {
            string text = $"{UiStrings.SurvivalLabel} {score.SurvivalScore}";

            if (score.Kills > 0)
            {
                text += $"\n{UiStrings.KillsLabel} {score.KillScore}";
            }

            if (won)
            {
                text += $"\n{UiStrings.WinBonusLabel} {score.WinBonus}";
            }

            return text;
        }
    }
}
