using System;
using Survival.Core;
using UnityEngine;

namespace Survival.Player
{
    /// <summary>
    /// HP、受伤、无敌帧、死亡事件。不直接调 UI（规范 5.2）。
    /// 无敌帧在这里统一处理，敌人不做冷却。
    /// </summary>
    public class PlayerHealth : MonoBehaviour
    {
        [SerializeField] private GameConfig _config;

        private float _invulnerableUntil;

        // 压测用（M6 §3.4）：只有 PerfProbe 在 ?perf=1 时打开，正常游玩恒为 false。
        // 不受伤是为了让玩家站着不动也能稳定维持高同屏数量；判定/表现逻辑本身不变。
        private bool _perfInvincible;

        public int CurrentHealth { get; private set; }

        /// <summary>本局真正扣血的次数（无敌期忽略的不计），供结算“受击”与协议 hitCount。</summary>
        public int HitCount { get; private set; }

        public int MaxHealth => _config != null ? _config.PlayerMaxHealth : 0;

        public event Action<int, int> OnHealthChanged;
        public event Action OnDied;

        /// <summary>真正扣血时触发（无敌期忽略的那次不触发），供表现层做受击反馈。</summary>
        public event Action OnDamaged;

        private void Awake()
        {
            CurrentHealth = MaxHealth;
        }

        private void Start()
        {
            OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
        }

        /// <summary>受伤入口。无敌期间直接忽略，由本类负责冷却。</summary>
        public void TakeDamage(int damage)
        {
            if (_config == null || CurrentHealth <= 0)
            {
                return;
            }

            if (_perfInvincible || Time.time < _invulnerableUntil)
            {
                return;
            }

            _invulnerableUntil = Time.time + _config.InvincibleDuration;
            CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
            HitCount++;
            OnDamaged?.Invoke();
            OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);

            Debug.Log($"[PlayerHealth] damage={damage} hp={CurrentHealth}/{MaxHealth}");

            if (CurrentHealth <= 0)
            {
                OnDied?.Invoke();
            }
        }

        /// <summary>只给压测探针用（M6 §3.4）：打开后 TakeDamage 直接忽略，不扣血、不触发受击表现。</summary>
        public void SetPerfInvincible(bool value) => _perfInvincible = value;

        /// <summary>重开用：满血、清无敌计时、受击计数归零。</summary>
        public void ResetHealth()
        {
            CurrentHealth = MaxHealth;
            HitCount = 0;
            _invulnerableUntil = 0f;
            OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
        }
    }
}
