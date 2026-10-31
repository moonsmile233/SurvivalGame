using Survival.Core;
using Survival.Player;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Survival.UI
{
    /// <summary>
    /// 触控浮动摇杆可视化（M4.7）：只读 <see cref="TouchInput"/> 的公开状态，不改速度/死区/半径。
    /// 显示条件：触摸按下且 Playing，且手指不在 UI 上（避免点暂停按钮时闪一下）。
    /// 桌面鼠标拖动不显示（TouchInput.TouchActive 只对触摸为 true）。
    /// 每帧无分配：两个 Image + 一个运行时生成的圆形 Sprite 复用，颜色只设一次。
    /// </summary>
    public class TouchJoystickView : MonoBehaviour
    {
        private const int CircleTextureSize = 128;
        private const float BaseAlpha = 0.22f;
        private const float KnobAlpha = 0.55f;
        private const float KnobRatio = 0.34f;

        // 外圈画成细圆环（线宽约半径的 10%），比实心大圆视觉重量小；内半径 = 外半径 × 该值。
        private const float RingInnerRatio = 0.9f;

        // 用户真机反馈「外圈太显眼」：可视化直径只占输入 maxRadius 的 65%，整体更淡。
        // 只影响观感——TouchInput 的 maxRadius/死区/速度完全不变；摇杆头在外圈边缘时约 65% 满速，
        // 再往外拖仍会加速（外圈不是满速判定的边界，输入逻辑仍按 maxRadius）。
        private const float VisualRadiusScale = 0.65f;

        [SerializeField] private TouchInput _touchInput;
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private Canvas _canvas;
        [SerializeField] private RectTransform _canvasRect;
        [SerializeField] private RectTransform _base;
        [SerializeField] private RectTransform _knob;
        [SerializeField] private Image _baseImage;
        [SerializeField] private Image _knobImage;

        private Sprite _ringSprite;
        private Texture2D _ringTexture;
        private Sprite _knobSprite;
        private Texture2D _knobTexture;
        private float _appliedDiameter = -1f;

        private void Awake()
        {
            _ringTexture = CreateCircleTexture(CircleTextureSize, RingInnerRatio);
            _ringSprite = Sprite.Create(
                _ringTexture,
                new Rect(0f, 0f, CircleTextureSize, CircleTextureSize),
                new Vector2(0.5f, 0.5f),
                100f);

            _knobTexture = CreateCircleTexture(CircleTextureSize, 0f);
            _knobSprite = Sprite.Create(
                _knobTexture,
                new Rect(0f, 0f, CircleTextureSize, CircleTextureSize),
                new Vector2(0.5f, 0.5f),
                100f);

            // 颜色/裁剪只设一次；后续只改 RectTransform，避免每帧 new Color。
            _baseImage.sprite = _ringSprite;
            _baseImage.color = new Color(1f, 1f, 1f, BaseAlpha);
            _baseImage.raycastTarget = false;

            _knobImage.sprite = _knobSprite;
            _knobImage.color = new Color(1f, 1f, 1f, KnobAlpha);
            _knobImage.raycastTarget = false;

            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (_ringSprite != null)
            {
                Destroy(_ringSprite);
            }

            if (_ringTexture != null)
            {
                Destroy(_ringTexture);
            }

            if (_knobSprite != null)
            {
                Destroy(_knobSprite);
            }

            if (_knobTexture != null)
            {
                Destroy(_knobTexture);
            }
        }

        private void Update()
        {
            if (_touchInput == null || _gameManager == null || _canvas == null || _canvasRect == null)
            {
                return;
            }

            if (!_touchInput.TouchActive || _gameManager.State != GameState.Playing)
            {
                SetVisible(false);
                return;
            }

            // 手指在暂停按钮等 UI 上时不画，避免轻点 UI 时摇杆闪一下。
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(_touchInput.FingerId))
            {
                SetVisible(false);
                return;
            }

            // Overlay Canvas 传 null 相机；结果已是 canvas 本地坐标（参考分辨率单位）。
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, _touchInput.AnchorScreen, null, out Vector2 local))
            {
                SetVisible(false);
                return;
            }

            float scale = _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            float radius = _touchInput.MaxRadiusPixels * VisualRadiusScale / scale;
            float diameter = radius * 2f;

            // maxRadius 随屏幕高度变化；只在真的变了时改 sizeDelta。
            if (!Mathf.Approximately(diameter, _appliedDiameter))
            {
                _appliedDiameter = diameter;
                _base.sizeDelta = new Vector2(diameter, diameter);
                _knob.sizeDelta = new Vector2(diameter * KnobRatio, diameter * KnobRatio);
            }

            _base.anchoredPosition = local;

            Vector2 offset = (_touchInput.CurrentScreen - _touchInput.AnchorScreen) / scale;
            float maxOffset = radius;
            if (offset.sqrMagnitude > maxOffset * maxOffset)
            {
                offset = offset.normalized * maxOffset;
            }

            _knob.anchoredPosition = local + offset;
            SetVisible(true);
        }

        private void SetVisible(bool visible)
        {
            if (_base != null && _base.gameObject.activeSelf != visible)
            {
                _base.gameObject.SetActive(visible);
            }
        }

        /// <summary>
        /// 生成 1px 软边的白色圆/圆环（透明底），只用于 UI Image，tint 由 Image.color 控制。
        /// innerRatio &lt;= 0 是实心圆（摇杆头）；0.9 是细环（底座，线宽约半径 10%）。
        /// </summary>
        private static Texture2D CreateCircleTexture(int size, float innerRatio)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[size * size];
            float outer = (size - 1) * 0.5f;
            float inner = outer * Mathf.Clamp01(innerRatio);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - outer;
                    float dy = y - outer;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = inner <= 0f
                        ? Mathf.Clamp01(outer - distance + 0.5f)
                        : Mathf.Clamp01(Mathf.Min(outer - distance, distance - inner) + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
