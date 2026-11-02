namespace Survival.Core
{
    /// <summary>
    /// 一局游戏的全部合法状态（v1.2 游戏优先；规范 5.1 / M1 状态机）。
    /// 业务态 = Menu / Playing / Paused / Result；Starting 是内部过渡态（3-2-1），
    /// 不发 onStateChange、不进 H5 文案、暂停/结算面板不显示（策划裁决 2026-11-01）。
    /// </summary>
    public enum GameState
    {
        Booting,
        Menu,
        Starting,
        Playing,
        Paused,
        Result,
    }
}
