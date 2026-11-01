using Survival.Core;
using Survival.Player;
using UnityEngine;

namespace Survival.Enemy
{
    /// <summary>
    /// 追踪玩家 + 接触伤害。不判断胜负（规范 5.2）。由 <see cref="EnemySpawner"/> 运行时创建并注入引用。
    ///
    /// M6 拥挤重做（2026-11-01，替代「唯一站位槽 + 站定钉住」）：
    /// - 没有固定站位点：只 seek「玩家 + 停距」，方向按当前位置实时算；被挤开就自然换位，不会被拉回钉子。
    /// - **软墙**（核心）：对邻居只扣「正在靠近」的速度分量，按距离线性衰减（R=0.90 → 软墙 0.70），
    ///   不产生任何向外推力 → 贴身时不会“被弹开”，典型中心距保持在 0.70~0.75（允许小部分重叠）。
    /// - **切向让位**：被挡住时沿邻居较少的一侧滑移；前面被挡（frontBlock）走 0.35 u/s 上限，
    ///   后面有人顶（behindBlock）走 0.2 u/s 上限。后排让位让“正面排队”变成“侧向让位/缓慢换位”。
    ///   没有全局漂移；前环只在被顶时让开（让开后强度自然归零），不会整群围着玩家转圈。
    /// - **防重合兜底**：只有邻居 <0.5 才触发，速度连续渐增（加速度受限），不是位置修正/冲量。
    /// - **玩家与敌人无碰撞**：玩家穿过人群时只掉血；敌人最多以 0.6×速度让位 + 侧闪，玩家不被挡/推。
    /// - 唯一的位置写入是 `pos += velocity * dt`（dt 钳到 0.05），没有任何两两位置修正，单帧位移有上限。
    ///
    /// 无每帧分配约束：不要用 LINQ / 数组 / 闭包；邻居是 Spawner 30Hz 填好的引用缓冲。
    /// </summary>
    public class EnemyAI : MonoBehaviour
    {
        // 单帧最大时间步：卡顿帧（如 0.1s）不放大位移，避免“瞬移”和穿透判定。
        private const float MaxDeltaTime = 0.05f;

        // 视口外扩 0.05（与 Spawner 的出生判定同一 margin）。
        private const float ViewMargin = 0.05f;

        // “在前环”的容差：离停距不到这么多，压测探针记为已到位（不是不再移动）。
        private const float FrontTolerance = 0.15f;

        // 邻居缓冲上限：六边形密排 6 个 + 少量外圈，8 个足够覆盖软墙与兜底。
        private const int MaxNeighbors = 8;

        // 软墙投影迭代次数：多邻居方向斜交时，单次顺序投影会残留逼近分量（Jacobi 耦合），
        // 迭代 3 次把残留压到可忽略，避免人群慢慢“压实”到 0.55。
        private const int WallIterations = 3;

        // 方向退化（dist≈0）时的兜底阈值。
        private const float DirectionEpsilon = 0.0001f;

        [SerializeField] private GameConfig _config;
        [SerializeField] private EnemyDefinition _definition;
        [SerializeField] private Transform _target;
        [SerializeField] private PlayerHealth _playerHealth;
        [SerializeField] private Collider2D _playerCollider;
        [SerializeField] private EnemySpawner _spawner;
        [SerializeField] private Camera _camera;

        private float _baseSpeed;
        private float _stopRadius;
        private float _offScreenSeconds;
        private bool _hasBeenSeen;

        private Vector2 _velocity;
        private Vector2 _emergencyVelocity;
        private Vector2 _lastDirection = Vector2.up;
        private float _sideSign;
        private Rigidbody2D _playerBody;

        private readonly EnemyAI[] _neighbors = new EnemyAI[MaxNeighbors];
        private int _neighborCount;

        private float _excessDistance;
        private bool _isAtFront;
        private float _blockW;

        /// <summary>已在前环（离停距 ≤0.15）。不是“钉住”——软墙/让位仍然生效。</summary>
        public bool IsAtFront => _isAtFront;

        /// <summary>离停距还差多少（&gt;0 表示还没到前环；压测探针用）。</summary>
        public float ExcessDistance => _excessDistance;

        /// <summary>本帧被邻居挡住的强度（0..1，取 front/behind 的较大值）；压测探针用它分组。</summary>
        public float BlockW => _blockW;

        /// <summary>前面有人挡住径向追击的强度（0..1）。</summary>
        public float FrontBlock { get; private set; }

        /// <summary>后面有人顶着我的强度（0..1）。</summary>
        public float BehindBlock { get; private set; }

        public float DistanceToPlayer { get; private set; }

        public void Initialize(
            GameConfig config,
            EnemyDefinition definition,
            Transform target,
            PlayerHealth playerHealth,
            EnemySpawner spawner,
            Camera camera,
            float stopRadius)
        {
            _config = config;
            _definition = definition;
            _target = target;
            _playerHealth = playerHealth;
            _playerCollider = playerHealth != null ? playerHealth.GetComponent<Collider2D>() : null;
            _playerBody = playerHealth != null ? playerHealth.GetComponent<Rigidbody2D>() : null;
            _spawner = spawner;
            _camera = camera;
            _stopRadius = stopRadius;

            // 让位/侧闪的稳定侧：生成时随机，只在左右压力接近时使用（压力差大时按实际分布换边）。
            _sideSign = Random.value < 0.5f ? -1f : 1f;

            if (definition == null || config == null)
            {
                return;
            }

            float jitter = 1f + Random.Range(-config.EnemySpeedJitter, config.EnemySpeedJitter);
            _baseSpeed = Mathf.Min(definition.MoveSpeed * jitter, config.EnemySpeedMax);
        }

        /// <summary>Spawner 30Hz 调用：只更新“看得见谁”，位置在 Update 里读实时 transform。</summary>
        public void SetNeighbors(EnemyAI[] source, int count)
        {
            _neighborCount = Mathf.Clamp(count, 0, MaxNeighbors);
            for (int i = 0; i < _neighborCount; i++)
            {
                _neighbors[i] = source[i];
            }

            for (int i = _neighborCount; i < MaxNeighbors; i++)
            {
                _neighbors[i] = null;
            }
        }

        private void Update()
        {
            if (_config == null || _definition == null || _target == null)
            {
                return;
            }

            float rawDelta = Time.deltaTime;
            if (rawDelta <= 0f)
            {
                // 暂停（timeScale = 0）：不移动、不累积回收计时。
                return;
            }

            float delta = Mathf.Min(rawDelta, MaxDeltaTime);
            Vector2 position = transform.position;
            Vector2 playerPosition = _target.position;
            Vector2 toPlayer = playerPosition - position;
            float distance = toPlayer.magnitude;

            Vector2 direction;
            if (distance > DirectionEpsilon)
            {
                direction = toPlayer / distance;
                _lastDirection = direction;
            }
            else
            {
                // 玩家正好压在身上：用上一次有效方向，避免 normalize(0) 产生 NaN。
                direction = _lastDirection;
            }

            DistanceToPlayer = distance;
            _excessDistance = Mathf.Max(0f, distance - _stopRadius);
            _isAtFront = distance - _stopRadius <= FrontTolerance;

            float speed = GetCurrentSpeed();
            float excess = distance - _stopRadius;

            // 1) 径向：比例控制（到停距为 0；玩家压进来时最多以 backOutRatio 退开）。
            float radialFactor = Mathf.Clamp(excess * _config.EnemySeekGain, -_config.EnemyBackOutRatio, 1f);
            Vector2 seekVelocity = direction * (radialFactor * speed);

            // 2) 两个“被压”来源：
            //    - 前面有人挡住径向追击（frontBlock）→ 外圈侧移找空位；
            //    - 后面有人顶着我（behindBlock）→ 前排给后面让开（≤0.2 u/s，自限：让开后就不再触发）。
            //    只看“意图/位置”，不看实际速度：墙把速度扣到 0 后也不会误判成没被压。
            float frontBlock = MeasureBlock(seekVelocity, position);
            float behindBlock = MeasureBehind(direction, position);
            FrontBlock = frontBlock;
            BehindBlock = behindBlock;
            _blockW = Mathf.Max(frontBlock, behindBlock);

            Vector2 desired = seekVelocity;

            // 3) 玩家移动穿过人群：侧向闪避（只在玩家朝我靠近时；玩家静止时不生效）。
            AddPlayerDodge(ref desired, direction, distance, speed);

            // 4) 切向让位：被挡住/被顶着时沿邻居较少的一侧滑移。
            float sidestep = Mathf.Min(
                _config.EnemySidestepSpeed * frontBlock + _config.EnemyYieldSpeed * behindBlock,
                _config.EnemySidestepSpeed + _config.EnemyYieldSpeed);
            AddSidestep(ref desired, direction, position, sidestep);

            // 5) 软墙：先合成、再投影（侧移不能把扣掉的逼近分量加回来）。
            desired = ApplySoftWall(desired, position);

            // 6) 速度指数平滑（连续，无位置跳变）。
            float blend = 1f - Mathf.Exp(-_config.EnemyVelocitySmoothing * delta);
            _velocity = Vector2.Lerp(_velocity, desired, blend);

            // 7) 平滑后再投影一次：消除平滑滞后造成的一帧穿入。
            _velocity = ApplySoftWall(_velocity, position);

            // 8) 防重合兜底：投影之后相加，单独限速逼近。
            Vector2 emergency = ComputeEmergency(position);
            _emergencyVelocity = Vector2.MoveTowards(
                _emergencyVelocity, emergency, _config.EnemyEmergencyAccel * delta);

            Vector2 finalVelocity = _velocity + _emergencyVelocity;
            finalVelocity = Vector2.ClampMagnitude(finalVelocity, _config.EnemyMaxVelocity);

            Vector2 newPosition = position + finalVelocity * delta;
            transform.position = newPosition;

            UpdateRecycle(newPosition, rawDelta);
        }

        /// <summary>
        /// 玩家移动穿过人群时，敌人往侧后方让开。
        /// 只在玩家确实朝自己靠近时启动；玩家静止时 playerVelocity≈0，不产生任何位移。
        /// 玩家本身不受力（敌人只有 Trigger Collider，不产生接触响应）。
        /// </summary>
        private void AddPlayerDodge(ref Vector2 desired, Vector2 direction, float distance, float speed)
        {
            if (_playerBody == null || _config.EnemyPlayerDodgeFactor <= 0f)
            {
                return;
            }

            Vector2 playerVelocity = _playerBody.linearVelocity;
            float closing = -Vector2.Dot(playerVelocity, direction);
            if (closing <= 0.3f || distance > _stopRadius + 0.15f)
            {
                return;
            }

            Vector2 playerToEnemy = -direction;
            float cross = playerVelocity.x * playerToEnemy.y - playerVelocity.y * playerToEnemy.x;
            float sign = Mathf.Abs(cross) > 0.05f ? Mathf.Sign(cross) : _sideSign;

            float speedMagnitude = playerVelocity.magnitude;
            if (speedMagnitude <= DirectionEpsilon)
            {
                return;
            }

            Vector2 perpendicular = new Vector2(-playerVelocity.y, playerVelocity.x) / speedMagnitude;
            float dodgeSpeed = Mathf.Min(_config.EnemyPlayerDodgeFactor * closing,
                _config.EnemyBackOutRatio * speed);
            desired += perpendicular * (sign * dodgeSpeed);
        }

        /// <summary>
        /// 切向让位：按左右邻居压力选空的一侧；强度只看被挡/被顶程度，没有“围着玩家巡游”的全局漂移。
        /// 左压力小 → 往左（perp = 逆时针 90°）；压力差不超过 margin 时沿用生成时的稳定侧。
        /// </summary>
        private void AddSidestep(ref Vector2 desired, Vector2 direction, Vector2 position, float amount)
        {
            if (amount <= 0.001f || _neighborCount == 0)
            {
                return;
            }

            Vector2 perpendicular = new Vector2(-direction.y, direction.x);
            float leftPressure = 0f;
            float rightPressure = 0f;

            for (int i = 0; i < _neighborCount; i++)
            {
                EnemyAI neighbor = _neighbors[i];
                if (neighbor == null || neighbor == this)
                {
                    continue;
                }

                Vector2 offset = (Vector2)neighbor.transform.position - position;
                float distance = offset.magnitude;
                if (distance >= _config.EnemySeparationRadius)
                {
                    continue;
                }

                float weight = 1f - distance / _config.EnemySeparationRadius;
                float cross = direction.x * offset.y - direction.y * offset.x;
                if (cross > 0.05f)
                {
                    leftPressure += weight;
                }
                else if (cross < -0.05f)
                {
                    rightPressure += weight;
                }
            }

            float margin = _config.EnemySideMargin;
            if (leftPressure + margin < rightPressure)
            {
                _sideSign = 1f; // 左边人少 → 往左
            }
            else if (rightPressure + margin < leftPressure)
            {
                _sideSign = -1f;
            }

            desired += perpendicular * (_sideSign * amount);
        }

        /// <summary>
        /// “被挡”强度：只看径向追击意图（不含让位/侧闪）是否被邻居挡住。
        /// 前环 seek=0 → 永远不算被挡（不会自己绕圈）；外圈还想往里挤时才算，触发切向让位。
        /// </summary>
        private float MeasureBlock(Vector2 seekVelocity, Vector2 position)
        {
            float radius = _config.EnemySeparationRadius;
            float band = Mathf.Max(0.01f, radius - _config.EnemySoftWallDistance);
            float blockW = 0f;

            for (int i = 0; i < _neighborCount; i++)
            {
                EnemyAI neighbor = _neighbors[i];
                if (neighbor == null || neighbor == this)
                {
                    continue;
                }

                Vector2 offset = position - (Vector2)neighbor.transform.position;
                float distance = offset.magnitude;
                if (distance >= radius || distance < DirectionEpsilon)
                {
                    continue;
                }

                Vector2 toward = offset / -distance;
                if (Vector2.Dot(seekVelocity, toward) <= 0f)
                {
                    continue;
                }

                float weight = Mathf.Clamp01((radius - distance) / band);
                if (weight > blockW)
                {
                    blockW = weight;
                }
            }

            return blockW;
        }

        /// <summary>
        /// 后方顶压强度：有人在我背后（朝玩家方向的相反侧 113° 内）且很近时，我为它让开。
        /// 自限：让开一个身位后背后的人不再是“正后方”，强度自然归零，不会持续绕圈。
        /// </summary>
        private float MeasureBehind(Vector2 direction, Vector2 position)
        {
            float radius = _config.EnemySeparationRadius;
            float band = Mathf.Max(0.01f, radius - _config.EnemySoftWallDistance);
            float behind = 0f;

            for (int i = 0; i < _neighborCount; i++)
            {
                EnemyAI neighbor = _neighbors[i];
                if (neighbor == null || neighbor == this)
                {
                    continue;
                }

                Vector2 offset = (Vector2)neighbor.transform.position - position;
                float distance = offset.magnitude;
                if (distance >= radius || distance < DirectionEpsilon)
                {
                    continue;
                }

                Vector2 toward = offset / distance;
                if (Vector2.Dot(direction, toward) > -0.4f)
                {
                    // 正前方/侧面的人不算“顶我”；只处理背后这一扇区。
                    continue;
                }

                float weight = Mathf.Clamp01((radius - distance) / band);
                if (weight > behind)
                {
                    behind = weight;
                }
            }

            return behind;
        }

        /// <summary>
        /// 软墙：只扣“朝邻居的逼近速度”，按距离线性衰减（R → 软墙距离）。
        /// 关键不变量：绝不产生向外速度（所以不会“贴着还被推”）、绝不反转速度（所以没有来回抖）。
        /// 迭代 WallIterations 次：多邻居斜交时单次顺序投影会残留逼近分量（人群会被慢慢压实）。
        /// </summary>
        private Vector2 ApplySoftWall(Vector2 velocity, Vector2 position)
        {
            float radius = _config.EnemySeparationRadius;
            float wall = _config.EnemySoftWallDistance;
            float band = Mathf.Max(0.01f, radius - wall);

            for (int iteration = 0; iteration < WallIterations; iteration++)
            {
                for (int i = 0; i < _neighborCount; i++)
                {
                    EnemyAI neighbor = _neighbors[i];
                    if (neighbor == null || neighbor == this)
                    {
                        continue;
                    }

                    Vector2 offset = position - (Vector2)neighbor.transform.position;
                    float distance = offset.magnitude;
                    if (distance >= radius || distance < DirectionEpsilon)
                    {
                        // 完全重合时方向不可靠，交给兜底（带反方向的稳定哈希）处理。
                        continue;
                    }

                    Vector2 toward = offset / -distance;
                    float closing = Vector2.Dot(velocity, toward);
                    if (closing <= 0f)
                    {
                        continue;
                    }

                    float weight = Mathf.Clamp01((radius - distance) / band);
                    velocity -= toward * (closing * weight);
                }
            }

            return velocity;
        }

        /// <summary>
        /// 防重合兜底：只在邻居 &lt;0.5 时触发；多邻居先求和再裁剪，求和抵消时用最近邻居方向兜底。
        /// 返回的是目标速度（由调用方按 EnemyEmergencyAccel 连续逼近），不是位置修正。
        /// </summary>
        private Vector2 ComputeEmergency(Vector2 position)
        {
            float threshold = _config.EnemyEmergencyDistance;
            Vector2 sum = Vector2.zero;
            Vector2 nearestDirection = Vector2.zero;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i < _neighborCount; i++)
            {
                EnemyAI neighbor = _neighbors[i];
                if (neighbor == null || neighbor == this)
                {
                    continue;
                }

                Vector2 offset = position - (Vector2)neighbor.transform.position;
                float distance = offset.magnitude;
                if (distance >= threshold)
                {
                    continue;
                }

                Vector2 direction = distance > DirectionEpsilon
                    ? offset / distance
                    : PairFallbackDirection(neighbor);
                float weight = 1f - distance / threshold;
                sum += direction * weight;

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestDirection = direction;
                }
            }

            if (nearestDistance >= float.MaxValue)
            {
                return Vector2.zero;
            }

            if (sum.sqrMagnitude < 0.04f)
            {
                // 对称环求和会抵消：改用最近邻居方向，保证仍有连续的外推目标。
                sum = nearestDirection;
            }

            return Vector2.ClampMagnitude(sum, 1f) * _config.EnemyEmergencyStrength;
        }

        /// <summary>
        /// 完全重合（d≈0）时的稳定方向：对哈希给角度，实例 ID 定号 → 两只敌人得到相反方向。
        /// 只用于异常兜底，正常游玩不会走到这里。
        /// </summary>
        private Vector2 PairFallbackDirection(EnemyAI other)
        {
            int selfId = GetInstanceID();
            int otherId = other.GetInstanceID();
            int low = Mathf.Min(selfId, otherId);
            int high = Mathf.Max(selfId, otherId);
            int hash = (low * 73856093) ^ (high * 19349663);
            float angle = (hash & 0x7FFFFFFF) % 6283 * 0.001f;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            return selfId < otherId ? direction : -direction;
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
