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

        /// <summary>存活分（已存活整数秒 × 每秒分），供结算明细使用。</summary>
        public int SurvivalScore => _survivedSeconds * _config.ScorePerSurvivedSecond;

        /// <summary>击杀分（击杀数 × 每个分）。</summary>
        public int KillScore => _kills * _config.ScorePerKill;

        /// <summary>胜利奖励，仅胜利时非 0。</summary>
        public int WinBonus => _won ? _config.WinBonus : 0;

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
            SetScore(SurvivalScore + KillScore + WinBonus);
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
