using System;

namespace Survival.Core
{
    /// <summary>
    /// 存活分 + 击杀分 + 胜利奖励。纯逻辑，不碰 UI（规范 6.1）。
    /// 由 GameManager 持有，每局 Reset。
    /// </summary>
    public class ScoreService
    {
        private readonly GameConfig _config;

        private int _survivedSeconds;
        private int _kills;
        private bool _won;

        public ScoreService(GameConfig config)
        {
            _config = config;
        }

        public int Score { get; private set; }
        public int Kills => _kills;

        public event Action<int> OnScoreChanged;

        /// <summary>已存活整数秒（只在整数变化时重算）。</summary>
        public void SetSurvivedSeconds(int seconds)
        {
            if (seconds == _survivedSeconds)
            {
                return;
            }

            _survivedSeconds = seconds;
            Recalculate();
        }

        public void AddKill()
        {
            _kills++;
            Recalculate();
        }

        /// <summary>胜利奖励只在胜利时计入。</summary>
        public void SetWon(bool won)
        {
            if (_won == won)
            {
                return;
            }

            _won = won;
            Recalculate();
        }

        public void Reset()
        {
            _survivedSeconds = 0;
            _kills = 0;
            _won = false;
            SetScore(0);
        }

        private void Recalculate()
        {
            int score = _survivedSeconds * _config.ScorePerSurvivedSecond
                        + _kills * _config.ScorePerKill
                        + (_won ? _config.WinBonus : 0);
            SetScore(score);
        }

        private void SetScore(int value)
        {
            if (Score == value)
            {
                return;
            }

            Score = value;
            OnScoreChanged?.Invoke(Score);
        }
    }
}
