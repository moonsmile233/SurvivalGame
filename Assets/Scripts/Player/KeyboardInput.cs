using UnityEngine;

namespace Survival.Player
{
    /// <summary>
    /// 键盘输入：WASD / 方向键 → Vector2。只输出方向，不移动（规范 5.5）。
    /// 用 Unity 内置 Input，不引入新 Input System；`activeInputHandler = Both` 由 ProjectSettings 保证。
    /// </summary>
    public class KeyboardInput : PlayerInputSource
    {
        private Vector2 _move;

        public override Vector2 Move => _move;

        private void Update()
        {
            var move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

            // 斜向不叠加：对角速度与正向一致。
            _move = move.sqrMagnitude > 1f ? move.normalized : move;
        }
    }
}
