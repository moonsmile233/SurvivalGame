using Survival.Core;
using UnityEngine;

namespace Survival.Player
{
    /// <summary>
    /// 受击表现：0.1s 红闪 + 无敌期 10Hz 闪烁。只操作自己的 SpriteRenderer（ticket M2 第 3 节）。
    /// 只订阅 PlayerHealth 的事件，不反向调用、不改 HP、不改状态。
    /// </summary>
    public class PlayerView : MonoBehaviour
    {
        private const float FlashSeconds = 0.1f;
        private const float BlinkInterval = 0.1f; // 10Hz

        private static readonly Color HitColor = new Color(1f, 0.25f, 0.25f, 1f);

        [SerializeField] private PlayerHealth _health;
        [SerializeField] private GameConfig _config;

        private SpriteRenderer _renderer;
        private float _flashRemaining;
        private float _blinkRemaining;
        private float _blinkTimer;

        private void Awake()
        {
            _renderer = GetComponent<SpriteRenderer>();
        }

        private void Start()
        {
            if (_renderer == null || _health == null)
            {
                Debug.LogError("[PlayerView] Missing SpriteRenderer or PlayerHealth, component disabled.");
                enabled = false;
                return;
            }

            _health.OnDamaged += HandleDamaged;
            _health.OnHealthChanged += HandleHealthChanged;
            _health.OnDied += StopFeedback;
        }

        private void OnDestroy()
        {
            if (_health == null)
            {
                return;
            }

            _health.OnDamaged -= HandleDamaged;
            _health.OnHealthChanged -= HandleHealthChanged;
            _health.OnDied -= StopFeedback;
        }

        private void Update()
        {
            // 暂停 / 结算时 timeScale = 0，deltaTime = 0，表现自然冻结。
            float delta = Time.deltaTime;

            if (_flashRemaining > 0f)
            {
                _flashRemaining -= delta;
                if (_flashRemaining <= 0f)
                {
                    _flashRemaining = 0f;
                    _renderer.color = Color.white;
                }

                return;
            }

            if (_blinkRemaining <= 0f)
            {
                return;
            }

            _blinkRemaining -= delta;
            _blinkTimer -= delta;

            if (_blinkTimer <= 0f)
            {
                _blinkTimer += BlinkInterval;
                _renderer.enabled = !_renderer.enabled;
            }

            if (_blinkRemaining <= 0f)
            {
                StopFeedback();
            }
        }

        private void HandleDamaged()
        {
            _renderer.color = HitColor;
            _renderer.enabled = true;

            _flashRemaining = FlashSeconds;
            float invincible = _config != null ? _config.InvincibleDuration : 1f;
            _blinkRemaining = Mathf.Max(0f, invincible - FlashSeconds);
            _blinkTimer = BlinkInterval;
        }

        /// <summary>重开（满血）时也要清掉残留表现。</summary>
        private void HandleHealthChanged(int current, int max)
        {
            if (current >= max)
            {
                StopFeedback();
            }
        }

        /// <summary>恢复原色 + 可见，死亡 / 重开都必须走到这里。</summary>
        private void StopFeedback()
        {
            _flashRemaining = 0f;
            _blinkRemaining = 0f;
            _blinkTimer = 0f;

            if (_renderer == null)
            {
                return;
            }

            _renderer.color = Color.white;
            _renderer.enabled = true;
        }
    }
}
