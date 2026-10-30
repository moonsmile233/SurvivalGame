namespace Survival.UI
{
    /// <summary>
    /// 全部中文 UI 文案的唯一出处（ticket M2 第 0 节）。
    /// 改文案后必须同步 Assets/Fonts/ui-chars.txt 并重烤 TMP 字体图集，否则新字符会显示成 tofu 方块。
    /// 只放常量，不写逻辑。
    /// </summary>
    public static class UiStrings
    {
        public const string HealthLabel = "生命";
        public const string TimeLabel = "时间";
        public const string ScoreLabel = "分数";

        public const string PauseButton = "暂停";
        public const string PausedTitle = "已暂停";
        public const string ResumeButton = "继续";

        public const string VictoryTitle = "胜利";
        public const string DefeatTitle = "失败";
        public const string FinalScoreLabel = "得分";
        // 结算明细里是分数不是秒数；H5 那句「存活 15.7 秒」是时长，避免两者混淆（M4 第 1.3 节）。
        public const string SurvivalLabel = "存活分";
        public const string KillsLabel = "击杀";
        public const string WinBonusLabel = "胜利奖励";
        public const string RestartButton = "重新开始";

        /// <summary>本项目用到的全部字符（供字体子集自检，必须都包含在 ui-chars.txt 中）。</summary>
        public const string AllCharacters =
            HealthLabel + TimeLabel + ScoreLabel + PauseButton + PausedTitle + ResumeButton +
            VictoryTitle + DefeatTitle + FinalScoreLabel + SurvivalLabel + KillsLabel +
            WinBonusLabel + RestartButton + " 0123456789:/";
    }
}
