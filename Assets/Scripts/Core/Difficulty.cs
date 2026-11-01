namespace Survival.Core
{
    /// <summary>
    /// 三档难度（M6 §4）。数值在 <see cref="DifficultyProfile"/> 资产里，代码只读不写。
    /// 数值固定为 0/1/2：<see cref="GameConfig"/> 的档位数组按下标取，改动顺序会静默错档。
    /// 字符串只在桥接层解析一次（H5 传来的 "easy"/"normal"/"hard"），游戏内一律用枚举比较。
    /// </summary>
    public enum Difficulty
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
    }
}
