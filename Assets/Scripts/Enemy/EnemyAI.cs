using Survival.Core;
using Survival.Player;
using UnityEngine;

namespace Survival.Enemy
{
    /// <summary>
    /// 追踪玩家 + 接触伤害。不判断胜负（规范 5.2）。由 <see cref="EnemySpawner"/> 运行时创建并注入引用。
    ///
    /// M4-B1 行为（ticket 2.2）：
    /// - 每只敌人有自己的「站位角」（12 个槽位 + 微小抖动），目标点 = 玩家 + 站位方向 × 停距；
    ///   停距 ≈ 玩家半径 + 敌人半径 − 0.08（约 0.7），接触伤害仍由 Trigger 判定。
    /// - 敌人之间**不在这里**做 steering 分离（密集时方向会每帧翻转），
    ///   改由 <see cref="EnemySpawner"/> 每帧做 1～2 轮两两位置修正。
    /// - 速度 = 生成时 ±12% 扰动 × 压力曲线（1.0→1.15），上限 2.2，始终慢于玩家。
    /// - 离相机视野（±0.05）持续 3 秒 → Unregister；进过视野后才计时，避免误杀刚出生的敌人。
    ///
    /// 无每帧分配约束：不要用 LINQ / 数组 / 闭包。
    /// </summary>
    public class EnemyAI : MonoBehaviour
    {
        private const int StanceSlotCount = 12;
        private const float StanceJitterRadians = 0.06f;
        private const float StopDistancePadding = 0.08f;

        // 到达容差：离站位点小于该距离就不再往里挤。
        // 没有它，Spawner 的两两修正每帧把敌人推开一点、敌人又立刻走回去，20 只时会持续小抖动。
        private const float ArrivalTolerance = 0.25f;

        // 回收判定：视口外扩 0.05（与 Spawner 的出生判定同一 margin）。
        private const float ViewMargin = 0.05f;

        [SerializeField] private GameConfig _config;
        [SerializeField] private EnemyDefinition _definition;
        [SerializeField] private Transform _target;
        [SerializeField] private PlayerHealth _playerHealth;
        [SerializeField] private Collider2D _playerCollider;
        [SerializeField] private EnemySpawner _spawner;
        [SerializeField] private Camera _camera;

        private float _baseSpeed;
        private float _stopDistance;
        private float _offScreenSeconds;
        private bool _hasBeenSeen;
        private Vector2 _stanceDirection;

        public void Initialize(
            GameConfig config,
            EnemyDefinition definition,
            Transform target,
            PlayerHealth playerHealth,
            EnemySpawner spawner,
            Camera camera,
            int stanceSlot)
        {
            _config = config;
            _definition = definition;
            _target = target;
            _playerHealth = playerHealth;
            _playerCollider = playerHealth != null ? playerHealth.GetComponent<Collider2D>() : null;
            _spawner = spawner;
            _camera = camera;

            if (definition == null || config == null)
            {
                return;
            }

            float jitter = 1f + Random.Range(-config.EnemySpeedJitter, config.EnemySpeedJitter);
            _baseSpeed = Mathf.Min(definition.MoveSpeed * jitter, config.EnemySpeedMax);
            _stopDistance = ComputeStopDistance(definition);

            float angle = stanceSlot * (Mathf.PI * 2f / StanceSlotCount)
                          + Random.Range(-StanceJitterRadians, StanceJitterRadians);
            _stanceDirection = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        /// <summary>停距 = 玩家半径 + 敌人半径 − 0.08：刚好让 Trigger 接触，又不叠进玩家身体里。</summary>
        private float ComputeStopDistance(EnemyDefinition definition)
        {
            float playerRadius = 0.4f;
            if (_playerCollider is CircleCollider2D circle)
            {
                Vector3 scale = _playerCollider.transform.lossyScale;
                playerRadius = circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            }

            return playerRadius + definition.ColliderRadius - StopDistancePadding;
        }

        private void Update()
        {
            if (_config == null || _definition == null || _target == null)
            {
                return;
            }

            float delta = Time.deltaTime;
            if (delta <= 0f)
            {
                // 暂停（timeScale = 0）：不移动、不累积回收计时。
                return;
            }

            Vector2 playerPosition = _target.position;
            Vector2 position = transform.position;
            Vector2 stancePoint = playerPosition + _stanceDirection * _stopDistance;
            Vector2 toStance = stancePoint - position;

            if (toStance.sqrMagnitude > ArrivalTolerance * ArrivalTolerance)
            {
                position += toStance.normalized * (GetCurrentSpeed() * delta);
                transform.position = position;
            }

            UpdateRecycle(position, delta);
        }

        /// <summary>
        /// 压力曲线（M4-B1）：速度随局内时间从 1.0 升到 1.15 倍，作用于**已存在**的敌人；
        /// 最终仍夹在 EnemySpeedMax（2.2）以内，必须慢于玩家 3.2。
        /// </summary>
        private float GetCurrentSpeed()
        {
            float progress = 1f;
            if (_spawner != null && _config.RoundDuration > 0f)
            {
                progress = Mathf.Clamp01(_spawner.RoundElapsed / _config.RoundDuration);
            }

            float multiplier = Mathf.Lerp(1f, _config.EnemyPressureMaxMultiplier, progress);
            return Mathf.Min(_baseSpeed * multiplier, _config.EnemySpeedMax);
        }

        /// <summary>
        /// 离开相机视野（margin 0.05）持续 N 秒 → Unregister + Destroy（M4-B0 2.4）。
        /// 出生点可能在视野外较远处（半径 1.15×半对角线），所以只有「进入过视野」后才开始计时，
        /// 避免刚出生的敌人在赶来路上被误回收。
        /// </summary>
        private void UpdateRecycle(Vector2 position, float delta)
        {
            if (_spawner == null || _camera == null)
            {
                return;
            }

            if (!IsOffScreen(position))
            {
                _hasBeenSeen = true;
                _offScreenSeconds = 0f;
                return;
            }

            if (!_hasBeenSeen)
            {
                return;
            }

            _offScreenSeconds += delta;
            if (_offScreenSeconds < _config.EnemyRecycleSeconds)
            {
                return;
            }

            _spawner.Unregister(this);
            enabled = false;
        }

        private bool IsOffScreen(Vector2 position)
        {
            Vector3 viewport = _camera.WorldToViewportPoint(position);
            return viewport.z <= 0f
                   || viewport.x < -ViewMargin || viewport.x > 1f + ViewMargin
                   || viewport.y < -ViewMargin || viewport.y > 1f + ViewMargin;
        }

        /// <summary>
        /// 兜底注销：任何路径销毁敌人（击杀、外部 Destroy、场景卸载）都不能在 Spawner 列表里留 null，
        /// 否则 ActiveCount 虚高、20 上限后不再刷怪。Forget 幂等，重复调用安全。
        /// </summary>
        private void OnDestroy()
        {
            if (_spawner != null)
            {
                _spawner.Forget(this);
            }
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            if (_definition == null || _playerHealth == null || other != _playerCollider)
            {
                return;
            }

            _playerHealth.TakeDamage(_definition.ContactDamage);
        }
    }
}
