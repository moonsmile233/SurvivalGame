using UnityEngine;

namespace Survival.Player
{
    /// <summary>
    /// 把多个输入源合成为一个：有触控/拖动就用触控，否则用键盘（M4 第 1.1 节）。
    /// 设备选择集中在这里，PlayerController 只依赖 <see cref="PlayerInputSource"/>，不做平台判断。
    /// </summary>
    public class PlayerInputRouter : PlayerInputSource
    {
        [SerializeField] private KeyboardInput _keyboard;
        [SerializeField] private TouchInput _touch;

        public override Vector2 Move
        {
            get
            {
                if (_touch != null)
                {
                    Vector2 touch = _touch.Move;
                    if (touch != Vector2.zero)
                    {
                        return touch;
                    }
                }

                return _keyboard != null ? _keyboard.Move : Vector2.zero;
            }
        }
    }
}
