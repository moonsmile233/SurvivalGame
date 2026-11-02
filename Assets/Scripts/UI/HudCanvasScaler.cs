using UnityEngine;
using UnityEngine.UI;

namespace Survival.UI
{
    /// <summary>
    /// 整块 Canvas 的参考分辨率切换（M4 第 1.2 节）。
    /// 手机竖屏时游戏区只有约 390×219 CSS px，1280×720 参考会把 24pt 缩到约 7px；
    /// 切到 640×360 后 HUD 与面板按钮一起放大（只缩 HUD 不够，面板按钮同样点不准）。
    ///
    /// 为什么用 Screen.width 而不是 RectTransform.rect.width：
    /// ScreenSpaceOverlay 的 Canvas rect 是「参考单位」，在 16:9 下恒等于参考宽（1280/640），
    /// 拿它判窄屏永远得不出结论；必须用屏幕宽度。DPR 由 H5 传给 createUnityInstance（上限 2），
    /// 本阈值按“设备像素宽 < 1000”估算窄屏；DPR&gt;1 且画布 CSS 宽在 500～1000 附近的边界尚未实测
    /// （见 `docs/代码审视-2026-11-02.md` P1-8）。
    ///
    /// 为什么在 Update 里判：等比例缩放时 OnRectTransformDimensionsChange 不会触发。
    /// 只在 Screen.width 变化时写一次，避免每帧改 CanvasScaler。
    /// </summary>
    public class HudCanvasScaler : MonoBehaviour
    {
        // 设备像素阈值：< 1000 视为手机/窄窗（390 CSS × DPR2 = 780；桌面 1280×1 = 1280）。
        private const float CompactWidthPixels = 1000f;

        private static readonly Vector2 WideReference = new Vector2(1280f, 720f);
        private static readonly Vector2 CompactReference = new Vector2(640f, 360f);

        [SerializeField] private CanvasScaler _scaler;

        private int _lastWidth = -1;

        private void Awake()
        {
            if (_scaler == null)
            {
                _scaler = GetComponent<CanvasScaler>();
            }
        }

        private void Start()
        {
            Apply(Screen.width);
        }

        private void Update()
        {
            Apply(Screen.width);
        }

        private void Apply(int screenWidth)
        {
            if (_scaler == null || screenWidth == _lastWidth)
            {
                return;
            }

            _lastWidth = screenWidth;
            _scaler.referenceResolution = screenWidth < CompactWidthPixels ? CompactReference : WideReference;
        }
    }
}
