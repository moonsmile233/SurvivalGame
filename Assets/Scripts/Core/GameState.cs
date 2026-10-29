namespace Survival.Core
{
    /// <summary>一局游戏的全部合法状态（规范 5.1 / M1 状态机）。</summary>
    public enum GameState
    {
        Booting,
        Ready,
        Playing,
        Paused,
        Won,
        Lost,
    }
}
