using Survival.Core;
using UnityEngine;

namespace Survival.Player
{
    /// <summary>
    /// 按输入移动刚体，并把玩家钳制在**地图边界**内（M4-B0：地图 36×20 大于视野，
    /// 相机跟随玩家；玩家活动范围不再等于相机视野）。不改状态机（规范 5.2）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] private GameConfig _config;
        [SerializeField] private PlayerInputSource _input;
        [SerializeField] private GameManager _gameManager;

        private Rigidbody2D _rb;
        private Vector2 _startPosition;
        private float _margin;

        private float _minX;
        private float _maxX;
        private float _minY;
        private float _maxY;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _startPosition = transform.position;

            if (_gameManager == null)
            {
                _gameManager = FindFirstObjectByType<GameManager>();
            }

            float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
            var collider = GetComponent<CircleCollider2D>();
            _margin = collider != null ? collider.radius * scale : 0.5f;

            // 精灵可能比碰撞体宽，按两者较大的半宽留边，避免贴边时图形露出一半。
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

            // Menu / Starting / Paused / Result 一律不许移动；只有 Playing 才接受输入（M3 第 1.2 节）。
            bool canMove = _gameManager != null && _gameManager.State == GameState.Playing;
            Vector2 move = canMove && _input != null ? _input.Move : Vector2.zero;
            _rb.linearVelocity = move * _config.PlayerSpeed;

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

        /// <summary>边界 = 地图四周减玩家半宽；固定不变（相机移动不影响）。</summary>
        private void RefreshBounds()
        {
            if (_config == null)
            {
                return;
            }

            // 钳制发生在物理积分之前，预留一步位移，避免积分后越过边界。
            float guard = _config.PlayerSpeed * Time.fixedDeltaTime;
            float inset = _margin + guard;

            _minX = -_config.MapWidth * 0.5f + inset;
            _maxX = _config.MapWidth * 0.5f - inset;
            _minY = -_config.MapHeight * 0.5f + inset;
            _maxY = _config.MapHeight * 0.5f - inset;
        }
    }
}
