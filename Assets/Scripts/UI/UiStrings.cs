namespace Survival.UI
{
    /// <summary>
    /// 全部中文 UI 文案的唯一出处（ticket M2 第 0 节）。
    /// 改文案后必须同步 Assets/Fonts/ui-chars.txt 并重烤 TMP 字体图集，否则新字符会显示成 tofu 方块。
    /// 子集字体 NotoSansSC-UI-Subset.otf 若缺字，用 tools/font-subset.py 从完整 Noto Sans SC 重裁。
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

        // ---------- v1.2 游戏优先：Unity 主菜单 / 暂停菜单 / 结算（2026-11-01） ----------

        public const string MenuTitle = "生存挑战";
        public const string MenuSubtitle = "存活 60 秒";
        public const string StartGameButton = "开始游戏";

        public const string DifficultyEasy = "简单";
        public const string DifficultyNormal = "普通";
        public const string DifficultyHard = "困难";

        // 菜单卡短标签：640×360 参考分辨率下三列并排，长句会挤（方案 §4.1）。
        public const string TagEasy = "更慢更少";
        public const string TagNormal = "标准节奏";
        public const string TagHard = "更快更多";

        public const string BackToMenuButton = "返回菜单";
        public const string PlayAgainButton = "再来一局";

        public const string RoundScoreLabel = "本局得分";
        public const string DifficultyLabel = "难度";
        public const string SurvivedLabel = "存活";
        public const string SecondsSuffix = "秒";
        public const string MultiplierLabel = "倍率";

        /// <summary>结算信息行的分隔符；与 `×` 一起必须进字体子集。</summary>
        public const string InfoSeparator = " · ";

        /// <summary>本项目用到的全部字符（供字体子集自检，必须都包含在 ui-chars.txt 中）。</summary>
        public const string AllCharacters =
            HealthLabel + TimeLabel + ScoreLabel + PauseButton + PausedTitle + ResumeButton +
            VictoryTitle + DefeatTitle + FinalScoreLabel + SurvivalLabel + KillsLabel +
            WinBonusLabel + RestartButton +
            MenuTitle + MenuSubtitle + StartGameButton +
            DifficultyEasy + DifficultyNormal + DifficultyHard +
            TagEasy + TagNormal + TagHard +
            BackToMenuButton + PlayAgainButton +
            RoundScoreLabel + DifficultyLabel + SurvivedLabel + SecondsSuffix + MultiplierLabel +
            InfoSeparator +
            " %-./0123456789:×";
    }
}
