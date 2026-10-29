using UnityEngine;

namespace Survival.Player
{
    /// <summary>
    /// 输入层：键盘 WASD / 方向键 → Vector2。只输出方向，不移动（规范 5.5）。
    /// 用 Unity 内置 Input，不引入新 Input System。
    /// </summary>
    public class PlayerInputController : MonoBehaviour
    {
        public Vector2 Move { get; private set; }

        private void Update()
        {
            var move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            Move = move.sqrMagnitude > 1f ? move.normalized : move;
        }
    }
}
