using System;
using UnityEngine;

namespace Survival.Core
{
    /// <summary>
    /// 分段存活分 + 剩余血量分 + 难度倍率 + 胜利奖励（M6 §5 / 决策 D4）。纯逻辑，不碰 UI（规范 6.1）。
    /// 由 GameManager 持有，每局 Reset。
    ///
    /// 公式：
    ///   存活分 = 按 elapsed 分段累计（10/12/15/20 每秒，0-15/15-30/30-45/45-60，满 60s = 855）
    ///   血量分 = 结算剩余 HP × GameConfig.ScorePerHealthPoint
    ///   最终分 = round((存活分 + 血量分) × 难度倍率) + 胜利奖励 200
    ///
    /// 关键不变量：
    /// - <see cref="SurvivalRawFor"/> 是 **elapsed 的纯函数**，每帧重算、不做浮点累加，所以暂停/掉帧不会漂移。
    /// - HUD 只读 <see cref="DisplayScore"/>（floor，整数、单调）；血量分与奖励只在结算出现（D2）。
    /// - 无击杀、无擦身：<see cref="Kills"/> 恒 0，仅为协议字段保留。
    /// </summary>
    public class ScoreService
    {
        private readonly GameConfig _config;

        private float _survivalRaw;
        private int _remainingHealth;
        private bool _won;
        private int _lastDisplayScore = int.MinValue;

        public ScoreService(GameConfig config)
        {
            _config = config;
        }

        /// <summary>难度倍率在开局时锁定（GameManager 设 ActiveDifficulty），局内不变。</summary>
        private float Multiplier => _config != null ? _config.ScoreMultiplier : 1f;

        // 资产里的 1.3f 实际是 1.29999995…，直接相乘会把 851.5 压成 851.4999 再取整到 851。
        // 设计倍率只到一位小数，先按十进制规整再算，保证 §4.4 的参考分表逐分对得上。
        private double DesignMultiplier => Math.Round((double)Multiplier, 3);

        /// <summary>
        /// HUD 显示分：存活分 × 倍率，向下取整。约每 0.05～0.1 秒变一次，
        /// 不出现小数、不做“整数秒 ×10”的跳字（用户 2026-11-01 口径）。
        /// </summary>
        public int DisplayScore => (int)Math.Floor(_survivalRaw * DesignMultiplier);

        /// <summary>结算明细里的存活分（乘倍率后取整）；与 <see cref="HealthScore"/> 相加可能与最终分差 1，以最终分为准。</summary>
        public int SurvivalScore => RoundToInt(_survivalRaw * DesignMultiplier);

        /// <summary>结算明细里的血量分（乘倍率后取整）。</summary>
        public int HealthScore => RoundToInt(HealthRaw * DesignMultiplier);

        /// <summary>最终分：存活分与血量分先相加再乘倍率，最后加固定奖励；奖励不乘倍率。</summary>
        public int FinalScore => RoundToInt((_survivalRaw + HealthRaw) * DesignMultiplier) + WinBonus;

        /// <summary>胜利奖励，仅胜利时非 0。</summary>
        public int WinBonus => _won && _config != null ? _config.WinBonus : 0;

        /// <summary>协议保留字段：本作无击杀，恒 0、不参与公式、不显示。</summary>
        public int Kills => 0;

        /// <summary>协议保留字段：恒 0。</summary>
        public int KillScore => 0;

        /// <summary>结算剩余血量（未乘倍率）。</summary>
        public int RemainingHealth => _remainingHealth;

        public event Action<int> OnScoreChanged;

        private float HealthRaw => _remainingHealth * (_config != null ? _config.ScorePerHealthPoint : 1f);

        /// <summary>
        /// 每帧调用：按当前 elapsed 重算原始存活分。只在 HUD 显示分变化时触发事件，
        /// 避免每帧重建 TMP 文本（M2 性能口径）。暂停/倒计时/结算不调用。
        /// </summary>
        public void Advance(float elapsed)
        {
            _survivalRaw = SurvivalRawFor(elapsed);
            NotifyDisplayScore();
        }

        /// <summary>
        /// 存活分原始值（未乘倍率）：按 <paramref name="seconds"/> 逐段取 min(remaining, segment)，
        /// 纯函数、无分配。钳到 [0, RoundDuration]；胜利时调用方直接传 RoundDuration（固定 60s）。
        /// </summary>
        public float SurvivalRawFor(float seconds)
        {
            float duration = _config != null ? _config.RoundDuration : 0f;
            float clamped = Mathf.Clamp(seconds, 0f, duration);
            float segment = _config != null ? _config.SurvivalSegmentSeconds : 15f;
            float total = 0f;
            int index = 0;

            while (clamped > 0f)
            {
                float step = Mathf.Min(clamped, segment);
                total += step * (_config != null ? _config.SurvivalRateForSegment(index) : 10f);
                clamped -= step;
                index++;
            }

            return total;
        }

        /// <summary>
        /// 结算：重算存活分并记录剩余血量/胜负。胜利时 elapsed 固定为回合时长，
        /// 失败时按实际 elapsed（GameManager 传当前值）。
        /// </summary>
        public void Finish(float elapsed, int remainingHealth, bool won)
        {
            float duration = _config != null ? _config.RoundDuration : 0f;
            _survivalRaw = SurvivalRawFor(won ? duration : elapsed);
            _remainingHealth = Mathf.Clamp(remainingHealth, 0, _config != null ? _config.PlayerMaxHealth : remainingHealth);
            _won = won;
            NotifyDisplayScore();
        }

        /// <summary>清空本局数据（重开/返回菜单）。</summary>
        public void Reset()
        {
            _survivalRaw = 0f;
            _remainingHealth = 0;
            _won = false;
            NotifyDisplayScore();
        }

        /// <summary>四舍五入（.5 向上），避免 Mathf.RoundToInt 的银行家舍入在 0.5 边界产生反直觉结果。</summary>
        private static int RoundToInt(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

        private void NotifyDisplayScore()
        {
            int value = DisplayScore;
            if (value == _lastDisplayScore)
            {
                return;
            }

            _lastDisplayScore = value;
            OnScoreChanged?.Invoke(value);
        }
    }
}
