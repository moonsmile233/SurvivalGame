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

        public int CurrentHealth { get; private set; }
        public int MaxHealth => _config != null ? _config.PlayerMaxHealth : 0;

        public event Action<int, int> OnHealthChanged;
        public event Action OnDied;

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

            if (Time.time < _invulnerableUntil)
            {
                return;
            }

            _invulnerableUntil = Time.time + _config.InvincibleDuration;
            CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
            OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);

            Debug.Log($"[PlayerHealth] damage={damage} hp={CurrentHealth}/{MaxHealth}");

            if (CurrentHealth <= 0)
            {
                OnDied?.Invoke();
            }
        }

        /// <summary>重开用：满血、清无敌计时。</summary>
        public void ResetHealth()
        {
            CurrentHealth = MaxHealth;
            _invulnerableUntil = 0f;
            OnHealthChanged?.Invoke(CurrentHealth, MaxHealth);
        }
    }
}
