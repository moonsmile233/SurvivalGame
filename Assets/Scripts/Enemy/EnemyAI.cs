using Survival.Core;
using Survival.Player;
using UnityEngine;

namespace Survival.Enemy
{
    /// <summary>
    /// 追踪玩家 + 接触伤害。不判断胜负（规范 5.2）。
    /// 由 EnemySpawner 运行时创建并注入引用。
    /// </summary>
    public class EnemyAI : MonoBehaviour
    {
        [SerializeField] private GameConfig _config;
        [SerializeField] private Transform _target;
        [SerializeField] private PlayerHealth _playerHealth;
        [SerializeField] private Collider2D _playerCollider;

        public void Initialize(GameConfig config, Transform target, PlayerHealth playerHealth)
        {
            _config = config;
            _target = target;
            _playerHealth = playerHealth;
            _playerCollider = playerHealth != null ? playerHealth.GetComponent<Collider2D>() : null;
        }

        private void Update()
        {
            if (_config == null || _target == null)
            {
                return;
            }

            transform.position = Vector2.MoveTowards(
                transform.position,
                _target.position,
                _config.EnemySpeed * Time.deltaTime);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (_config == null || _playerHealth == null || other != _playerCollider)
            {
                return;
            }

            _playerHealth.TakeDamage(_config.EnemyContactDamage);
        }
    }
}
