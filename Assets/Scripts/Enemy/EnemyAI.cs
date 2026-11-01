using Survival.Core;
using Survival.Player;
using UnityEngine;

namespace Survival.Enemy
{
    /// <summary>
    /// 追踪玩家 + 接触伤害。不判断胜负（规范 5.2）。由 <see cref="EnemySpawner"/> 运行时创建并注入引用。
    ///
    /// M6 行为（2026-11-01，拥挤修复）：
    /// - 每只敌人由 Spawner 分配**唯一站位槽**（角度 + 环半径），目标点 = 玩家 + 方向 × 半径；
    ///   不再多只共享同一个目标点，所以"走到位→被推开→走回去"的微抖从根上消失。
    /// - 到位的判定更紧（<see cref="ArrivalTolerance"/>）：到位后本类不再移动，Spawner 的分离修正也不推它——
    ///   站位环是钉子，不能被邻格推力带出极限环。
    /// - 接触伤害仍由 Trigger 判定；只有内圈（半径 = 玩家半径 + 敌人半径 − 0.08）能碰到玩家，外环不造成伤害。
    /// - 速度 = 生成时 ±12% 扰动 × 压力曲线（1.0→1.15），上限 2.2，始终慢于玩家。
    /// - 离相机视野（±0.05）持续 N 秒 → Unregister；进过视野后才计时，避免误杀刚出生的敌人。
    ///
    /// 无每帧分配约束：不要用 LINQ / 数组 / 闭包。
    /// </summary>
    public class EnemyAI : MonoBehaviour
    {
        // 到达容差：离站位点小于该距离就不再往里挤。原来是 0.25（配合旧 12 槽的抖动），
        // 现在槽位唯一，收紧到 0.10：站位更准，也不会因为两个人各差 0.25 而贴到一起。
        private const float ArrivalTolerance = 0.10f;

        // "已进入站位区"的宽限：离自己的站位点小于该值就不参与分离修正（不推别人，也不被推）。
        // 取 1.0（≈一个间距）而不是更小：槽位本身已经保证最终间距，进入站位区后还继续推，
        // 只会在站位点附近和邻格形成"推开—走回"的极限环；而站位环也不能挡住赶路的敌人
        // （否则后到内圈的敌人会被外环永远挡在外面）。
        private const float NearStanceTolerance = 1.0f;

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
        private float _offScreenSeconds;
        private bool _hasBeenSeen;
        private bool _isSettled;
        private bool _isNearStance;
        private float _stanceDistance;

        // 站位槽（由 Spawner 分配）：方向 + 环半径；slotId/generation 用于销毁时归还槽位。
        private Vector2 _stanceDirection;
        private float _stanceRadius;
        private int _stanceSlotId = -1;
        private int _roundGeneration;

        /// <summary>已经站在自己的站位点（误差 &lt; 到达容差），本帧不再移动。</summary>
        public bool IsSettled => _isSettled;

        /// <summary>已进入站位区（&lt; 1.0）：不参与分离修正，避免"快到位被推开→走回去"的极限环。</summary>
        public bool IsNearStance => _isNearStance;

        /// <summary>到站位点的距离（压测探针用；用于判断没站定的敌人离目标还有多远）。</summary>
        public float StanceDistance => _stanceDistance;

        public void Initialize(
            GameConfig config,
            EnemyDefinition definition,
            Transform target,
            PlayerHealth playerHealth,
            EnemySpawner spawner,
            Camera camera,
            float stanceAngle,
            float stanceRadius,
            int stanceSlotId,
            int roundGeneration)
        {
            _config = config;
            _definition = definition;
            _target = target;
            _playerHealth = playerHealth;
            _playerCollider = playerHealth != null ? playerHealth.GetComponent<Collider2D>() : null;
            _spawner = spawner;
            _camera = camera;
            _stanceRadius = stanceRadius;
            _stanceSlotId = stanceSlotId;
            _roundGeneration = roundGeneration;
            _stanceDirection = new Vector2(Mathf.Cos(stanceAngle), Mathf.Sin(stanceAngle));

            if (definition == null || config == null)
            {
                return;
            }

            float jitter = 1f + Random.Range(-config.EnemySpeedJitter, config.EnemySpeedJitter);
            _baseSpeed = Mathf.Min(definition.MoveSpeed * jitter, config.EnemySpeedMax);
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
            Vector2 stancePoint = playerPosition + _stanceDirection * _stanceRadius;
            Vector2 toStance = stancePoint - position;
            _stanceDistance = toStance.magnitude;
            _isSettled = _stanceDistance <= ArrivalTolerance;
            _isNearStance = _stanceDistance <= NearStanceTolerance;

            if (!_isSettled)
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
        /// 兜底注销：任何路径销毁敌人（外部 Destroy、场景卸载）都不能在 Spawner 列表里留 null，
        /// 否则 ActiveCount 虚高、上限后不再刷怪。Forget 幂等，重复调用安全。
        /// 同时归还站位槽（带 generation，清场那帧晚到的销毁不会误释放新回合的槽）。
        /// </summary>
        private void OnDestroy()
        {
            if (_spawner != null)
            {
                _spawner.Forget(this);
                _spawner.ReleaseStanceSlot(_stanceSlotId, _roundGeneration);
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
