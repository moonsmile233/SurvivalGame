using UnityEngine;

namespace Survival.Player
{
    /// <summary>
    /// 输入层抽象：只输出方向，不移动（规范 5.5）。
    /// <see cref="PlayerController"/> 只依赖本基类，键盘/触控差异在具体输入源里，不写平台判断。
    /// </summary>
    public abstract class PlayerInputSource : MonoBehaviour
    {
        /// <summary>本帧移动方向，模长 0~1；非 Playing 状态由 PlayerController 忽略。</summary>
        public abstract Vector2 Move { get; }
    }
}
