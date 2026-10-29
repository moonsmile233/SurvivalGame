using Survival.Core;
using UnityEngine;

namespace Survival.Player
{
    /// <summary>
    /// 按输入移动刚体，并把玩家钳制在相机可视范围内。
    /// 不改状态机（规范 5.2）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private GameConfig _config;
        [SerializeField] private PlayerInputController _input;
        [SerializeField] private Camera _camera;

        private Rigidbody2D _rb;
        private Vector2 _startPosition;
        private float _margin;

        private float _minX;
        private float _maxX;
        private float _minY;
        private float _maxY;
        private float _boundsAspect = -1f;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _startPosition = transform.position;

            if (_camera == null)
            {
                _camera = Camera.main;
            }

            float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
            var collider = GetComponent<CircleCollider2D>();
            _margin = collider != null ? collider.radius * scale : 0.5f;

            // 精灵可能比碰撞体宽，按两者较大的半宽留边，避免贴边时图形露出视野。
            var renderer = GetComponent<SpriteRenderer>();
            if (renderer != null && renderer.sprite != null)
            {
                Vector3 extents = renderer.sprite.bounds.extents;
                _margin = Mathf.Max(_margin, Mathf.Max(extents.x, extents.y) * scale);
            }

            RefreshBounds();
        }

        private void FixedUpdate()
        {
            if (_config == null)
            {
                return;
            }

            Vector2 move = _input != null ? _input.Move : Vector2.zero;
            _rb.linearVelocity = move * _config.PlayerSpeed;

            if (_camera == null)
            {
                return;
            }

            // Game 视图改分辨率 / 浏览器缩放时重算边界。
            if (!Mathf.Approximately(_boundsAspect, _camera.aspect))
            {
                RefreshBounds();
            }

            Vector2 position = _rb.position;
            position.x = Mathf.Clamp(position.x, _minX, _maxX);
            position.y = Mathf.Clamp(position.y, _minY, _maxY);
            _rb.position = position;
        }

        /// <summary>重开用：回原点并清速度。</summary>
        public void ResetToStart()
        {
            _rb.linearVelocity = Vector2.zero;
            _rb.position = _startPosition;
            transform.position = _startPosition;
        }

        private void RefreshBounds()
        {
            if (_camera == null)
            {
                return;
            }

            _boundsAspect = _camera.aspect;
            float distance = Mathf.Abs(_camera.transform.position.z - transform.position.z);
            Vector3 bottomLeft = _camera.ViewportToWorldPoint(new Vector3(0f, 0f, distance));
            Vector3 topRight = _camera.ViewportToWorldPoint(new Vector3(1f, 1f, distance));

            // 钳制发生在物理积分之前，预留一步位移，避免积分后越过边界。
            float guard = _config != null ? _config.PlayerSpeed * Time.fixedDeltaTime : 0f;
            float inset = _margin + guard;

            _minX = bottomLeft.x + inset;
            _maxX = topRight.x - inset;
            _minY = bottomLeft.y + inset;
            _maxY = topRight.y - inset;
        }
    }
}
