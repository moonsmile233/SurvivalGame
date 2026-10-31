using UnityEngine;

namespace Survival.Player
{
    /// <summary>
    /// 触控输入：全屏「锚点相对」拖动（M4 第 1.1 节）。
    /// 不画固定摇杆；M4.7 的浮动摇杆可视化由 <c>TouchJoystickView</c> 只读本类状态渲染，输入逻辑不在这里改。
    ///
    /// 语义：手指按下点就是摇杆中心，之后指到哪就朝哪走，幅度 = 距离 / maxRadius。
    /// 这样连续移动不需要一直拖、也不会拖到屏幕边缘就得抬手；松手才归零。
    /// 不采用「每帧位移归一化」：慢速拖动方向会断断续续，手停人也停。
    ///
    /// 约束：
    /// - 只跟踪第一根手指，记录 fingerId；原手指抬起换成另一根时必须重新锚定，不能跳方向。
    /// - 触摸存在时忽略鼠标：Unity 会把触摸模拟成鼠标事件，不跳过会双份输入。
    /// - maxRadius 用设备像素（Screen.height 的比例），夹在 80～160，与 DPR 无关。
    /// </summary>
    public class TouchInput : PlayerInputSource
    {
        private const float DeadZonePixels = 8f;
        private const float MinRadiusPixels = 80f;
        private const float MaxRadiusLimitPixels = 160f;

        // 满偏距离 ≈ 0.2 屏高：390×844 手机约 169 设备像素，夹到 160；
        // 拇指移动约 1/5 屏就能到满速，再远也不会更快。
        private const float RadiusScreenRatio = 0.2f;

        private Vector2 _move;
        private Vector2 _anchor;
        private int _fingerId = -1;
        private bool _mouseTracking;

        public override Vector2 Move => _move;

        // 只读状态给 TouchJoystickView 画浮动摇杆（M4.7）。鼠标拖动不算 TouchActive，桌面不显示摇杆。
        public bool TouchActive { get; private set; }
        public int FingerId => _fingerId;
        public Vector2 AnchorScreen => _anchor;
        public Vector2 CurrentScreen { get; private set; }
        public float MaxRadiusPixels { get; private set; }

        private void Update()
        {
            float maxRadius = Mathf.Clamp(Screen.height * RadiusScreenRatio, MinRadiusPixels, MaxRadiusLimitPixels);
            MaxRadiusPixels = maxRadius;
            TouchActive = false;

            if (Input.touchCount > 0)
            {
                _mouseTracking = false;
                UpdateTouch(maxRadius);
                return;
            }

            _fingerId = -1;
            UpdateMouse(maxRadius);
        }

        private void UpdateTouch(float maxRadius)
        {
            if (_fingerId < 0)
            {
                // 新的一根手指：以按下点为中心重新锚定。
                Touch first = Input.GetTouch(0);
                _fingerId = first.fingerId;
                _anchor = first.position;
                CurrentScreen = first.position;
                TouchActive = true;
                _move = Vector2.zero;
                return;
            }

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.fingerId != _fingerId)
                {
                    continue;
                }

                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    ResetTouch();
                    return;
                }

                CurrentScreen = touch.position;
                TouchActive = true;
                _move = ComputeMove(touch.position - _anchor, maxRadius);
                return;
            }

            // 跟踪的手指已经不在了（抬起后 touch 0 换成别人）：这一帧停住，下一帧重新锚定。
            ResetTouch();
        }

        private void UpdateMouse(float maxRadius)
        {
            if (Input.GetMouseButtonDown(0))
            {
                _mouseTracking = true;
                _anchor = Input.mousePosition;
                _move = Vector2.zero;
                return;
            }

            if (!_mouseTracking || !Input.GetMouseButton(0))
            {
                if (Input.GetMouseButtonUp(0))
                {
                    _mouseTracking = false;
                }

                _move = Vector2.zero;
                return;
            }

            _move = ComputeMove((Vector2)Input.mousePosition - _anchor, maxRadius);
        }

        private static Vector2 ComputeMove(Vector2 offset, float maxRadius)
        {
            float distance = offset.magnitude;
            if (distance <= DeadZonePixels)
            {
                return Vector2.zero;
            }

            float magnitude = Mathf.Clamp01((distance - DeadZonePixels) / (maxRadius - DeadZonePixels));
            return offset / distance * magnitude;
        }

        private void ResetTouch()
        {
            _fingerId = -1;
            TouchActive = false;
            _move = Vector2.zero;
        }
    }
}
