using System.Collections.Generic;
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
        private int _contactDamage;
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

        private EnemyKind _kind;
        private SpriteRenderer _spriteRenderer;
        private Sprite _baseSprite;
        private Sprite _chargeWindupSprite;
        private Color _baseTint = Color.white;
        private Vector3 _baseScale = Vector3.one;

        private float _chargeTriggerDistance;
        private float _chargeWindupSeconds;
        private float _chargeDurationSeconds;
        private float _chargeDistance;
        private float _chargeSpeed;
        private float _chargeRecoverSeconds;
        private float _chargeCooldownSeconds;
        private int _chargeDamage;

        private ChargerState _chargerState = ChargerState.Approach;
        private float _chargerTimer;
        private Vector2 _chargeDirection = Vector2.up;
        private bool _chargeHitDone;
        private bool _chargeStartedInsideMap;
        // 冲刺探针：起终点与实际距离/时长（只在 s_logChargerStates 下写字符串）。
        private Vector2 _dashStartPosition;
        private float _dashElapsed;

        // 只由 PerfProbe 的 ?enemyKind=charger 打开：状态切换打一行 console，供 E2E 抓 Windup/Dash 截图。
        private static bool s_logChargerStates;

        private static readonly Color ChargerWindupTint = new Color(1f, 0.85f, 0.2f, 1f);
        private static readonly Color ChargerDashGhostTint = new Color(1f, 1f, 1f, 0.72f);

        // 绕行者进入“侧绕计时”的距离：从出生点 11.7u 赶来不算侧绕时间，否则 6s 会在半路耗尽。
        private const float FlankEngageDistance = 4.5f;

        // 探针日志间隔：只给单 Flanker 截图/轨迹核对用，40 只压测不会开（见 PerfProbe）。
        private const float FlankLogInterval = 0.5f;

        private float _flankRadius;
        private float _flankAngleDegrees;
        private float _flankPounceDistance;
        private float _flankTimeoutSeconds;
        private float _flankTimer;
        private float _flankLogTimer;
        // 出生时“玩家→自己”的方位；玩家静止时用它当固定基准，否则每帧用自身方位会让 bearing 恒为 0。
        private Vector2 _flankReferenceBearing = Vector2.up;
        // 到达侧后点后闩锁为 true，之后一路 seek 玩家；否则每帧在“追目标点/追玩家”间切换会在 2.4-0.6 处抖动不扑。
        private bool _flankPointReached;
        private FlankerState _flankerState = FlankerState.Flank;
        private static bool s_logFlankerStates;
        // perf-only（?flankTimeout=秒）：压短侧绕超时以验证 Direct；0 = 用资产值，正常游玩不设置。
        private static float s_perfFlankTimeoutOverride;

        private enum ChargerState
        {
            Approach = 0,
            Windup = 1,
            Dash = 2,
            Recover = 3,
            Cooldown = 4,
        }

        private enum FlankerState
        {
            Flank = 0,
            Pounce = 1,
            Direct = 2,
        }

        /// <summary>已在前环（离停距 ≤0.15）。不是“钉住”——软墙/让位仍然生效。</summary>
        public bool IsAtFront => _isAtFront;

        /// <summary>本敌类型；PerfProbe / Spawner 统计用。</summary>
        public EnemyKind Kind => _kind;

        /// <summary>Charger 是否处于冲刺帧（只读探针）。</summary>
        public bool IsCharging => _kind == EnemyKind.Charger && _chargerState == ChargerState.Dash;

        /// <summary>Charger 状态名；非 Charger 返回空串。只在探针/日志读，不进 Update 热路径。</summary>
        public string ChargerStateName => _kind == EnemyKind.Charger ? _chargerState.ToString() : string.Empty;

        /// <summary>只给 PerfProbe 用：强制类型测试时打开冲锋状态日志。</summary>
        public static void SetLogChargerStates(bool value) => s_logChargerStates = value;

        /// <summary>Flanker 是否处于侧绕帧（只读探针）。</summary>
        public bool IsFlanking => _kind == EnemyKind.Flanker && _flankerState == FlankerState.Flank;

        /// <summary>
        /// 是否进入过相机视野。Spawner 到顶补位只回收进过视野的敌人，
        /// 避免误杀刚出生、还在视野外赶来的（与 <see cref="UpdateRecycle"/> 同一判定口径）。
        /// </summary>
        public bool HasBeenSeen => _hasBeenSeen;

        /// <summary>Flanker 状态名；非 Flanker 返回空串。只在探针/日志读。</summary>
        public string FlankerStateName => _kind == EnemyKind.Flanker ? _flankerState.ToString() : string.Empty;

        /// <summary>只给 PerfProbe 用：强制类型测试时打开绕行状态日志。</summary>
        public static void SetLogFlankerStates(bool value) => s_logFlankerStates = value;

        /// <summary>只给 PerfProbe 用：视野超时回收时打一行日志（验证 1.5s 口径），最多 20 行。</summary>
        public static void SetLogRecycle(bool value) => s_logRecycle = value;

        private static bool s_logRecycle;
        private static int s_logRecycleCount;

        /// <summary>只给 PerfProbe 的 ?flankTimeout= 用；0 表示使用资产值。</summary>
        public static void SetPerfFlankTimeoutOverride(float seconds) => s_perfFlankTimeoutOverride = Mathf.Max(0f, seconds);

        /// <summary>离停距还差多少（&gt;0 表示还没到前环；压测探针用）。</summary>
        public float ExcessDistance => _excessDistance;

        /// <summary>本帧被邻居挡住的强度（0..1，取 front/behind 的较大值）；压测探针用它分组。</summary>
        public float BlockW => _blockW;

        /// <summary>前面有人挡住径向追击的强度（0..1）。</summary>
        public float FrontBlock { get; private set; }

        /// <summary>后面有人顶着我的强度（0..1）。</summary>
        public float BehindBlock { get; private set; }

        public void Initialize(
            GameConfig config,
            EnemyDefinition definition,
            Transform target,
            PlayerHealth playerHealth,
            EnemySpawner spawner,
            Camera camera,
            float stopRadius,
            SpriteRenderer spriteRenderer = null)
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

            _kind = definition != null ? definition.Kind : EnemyKind.Chaser;
            _spriteRenderer = spriteRenderer != null ? spriteRenderer : GetComponent<SpriteRenderer>();
            _baseTint = _spriteRenderer != null ? _spriteRenderer.color : Color.white;
            _baseSprite = _spriteRenderer != null ? _spriteRenderer.sprite : null;
            _chargeWindupSprite = definition != null ? definition.ChargeWindupSprite : null;
            _baseScale = transform.localScale;
            if (_baseScale.sqrMagnitude <= 0.000001f)
            {
                _baseScale = Vector3.one;
            }

            // 让位/侧闪的稳定侧：生成时随机，只在左右压力接近时使用（压力差大时按实际分布换边）。
            _sideSign = Random.value < 0.5f ? -1f : 1f;

            if (definition == null || config == null)
            {
                return;
            }

            // 难度在生成时结算一次（局内锁定，见协议 v2 §2.2）：不要放进 Update，
            // 避免每帧读档位、也避免中途改档位导致同一只敌人速度/伤害跳变。
            float jitter = 1f + Random.Range(-config.EnemySpeedJitter, config.EnemySpeedJitter);
            _baseSpeed = Mathf.Min(definition.MoveSpeed * config.EnemySpeedMultiplier * jitter, config.EnemySpeedMax);
            _contactDamage = Mathf.Max(1, Mathf.RoundToInt(definition.ContactDamage * config.DamageMultiplier));

            if (_kind == EnemyKind.Charger)
            {
                // 冲刺速度不吃 jitter/压力曲线，只乘难度速度倍率（困难 5.0×1.1=5.5）；
                // 也**不受 EnemySpeedMax（2.2）限制**——这是冲锋的固定路径速度。
                _chargeTriggerDistance = definition.ChargeTriggerDistance;
                _chargeWindupSeconds = Mathf.Max(0.05f, definition.ChargeWindupSeconds);
                // 冲刺用难度档案的绝对速度（4.0/4.5/5.0），不再乘 EnemySpeedMultiplier，避免二次缩放。
                float profileChargeSpeed = config.ActiveProfile != null ? config.ActiveProfile.ChargeSpeed : 0f;
                _chargeSpeed = profileChargeSpeed > 0f ? profileChargeSpeed : definition.ChargeSpeed;
                _chargeDistance = Mathf.Max(0.1f, definition.ChargeDistance);
                // 时长由距离/实际速度推导；三档难度都保证冲刺覆盖配置距离，不再写死 0.45s。
                _chargeDurationSeconds = _chargeDistance / Mathf.Max(0.01f, _chargeSpeed);
                _chargeRecoverSeconds = Mathf.Max(0f, definition.ChargeRecoverSeconds);
                _chargeCooldownSeconds = Mathf.Max(0f, definition.ChargeCooldownSeconds);
                _chargeDamage = Mathf.Max(1, Mathf.RoundToInt(definition.ChargeDamage * config.DamageMultiplier));
            }

            if (_kind == EnemyKind.Flanker)
            {
                _flankRadius = Mathf.Max(0.1f, definition.FlankRadius);
                _flankAngleDegrees = definition.FlankAngleDegrees;
                _flankPounceDistance = Mathf.Max(0.1f, definition.FlankPounceDistance);
                _flankTimeoutSeconds = s_perfFlankTimeoutOverride > 0f
                    ? s_perfFlankTimeoutOverride
                    : Mathf.Max(0.1f, definition.FlankTimeoutSeconds);
                _flankTimer = _flankTimeoutSeconds;
                Vector2 spawnBearing = (Vector2)transform.position - (Vector2)_target.position;
                _flankReferenceBearing = spawnBearing.sqrMagnitude > DirectionEpsilon
                    ? spawnBearing.normalized
                    : Vector2.up;
                // 走同一个入口打日志：探针需要看到“出生即 Flank”，否则只有 Pounce 一条，无法证明侧绕过程。
                EnterFlankerState(FlankerState.Flank);
            }
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

            _excessDistance = Mathf.Max(0f, distance - _stopRadius);
            _isAtFront = distance - _stopRadius <= FrontTolerance;

            float speed = GetCurrentSpeed();

            // 冲锋者有自己的接近/前摇/冲刺/恢复状态；其余类型走原来的 seek + 人群管线。
            if (_kind == EnemyKind.Charger)
            {
                UpdateCharger(position, direction, distance, speed, rawDelta, delta);
                return;
            }

            if (_kind == EnemyKind.Flanker)
            {
                UpdateFlanker(position, direction, distance, speed, rawDelta, delta);
                return;
            }

            // 1) 径向：比例控制（到停距为 0；玩家压进来时最多以 backOutRatio 退开）。
            Vector2 seekVelocity = ComputeSeekVelocity(direction, distance, speed);

            // 2) 人群管线（软墙/让位/防重合/积分）统一走 ApplyCrowdMovement；
            //    C3/C4 只改 seekVelocity 的来源与状态，不复制这段。
            Vector2 newPosition = ApplyCrowdMovement(position, direction, distance, speed, seekVelocity, delta);
            UpdateRecycle(newPosition, rawDelta);
        }

        /// <summary>
        /// 公共人群管线：被挡/被顶测量 → 玩家侧闪 → 切向让位 → 软墙 → 速度平滑 → 防重合 → 积分。
        /// Chaser 直接使用；Charger/Flanker 只改变传入的 seekVelocity 与速度，不复制这套修正。
        /// 不变量：唯一位置写入仍是 pos += finalVelocity * dt；不做两两位置修正。
        /// </summary>
        private Vector2 ApplyCrowdMovement(
            Vector2 position,
            Vector2 direction,
            float distance,
            float speed,
            Vector2 seekVelocity,
            float delta,
            bool allowPlayerDodge = true,
            bool allowSidestep = true)
        {
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
            //    Windup/Recover 传 false：冲锋者要站定读条，不被玩家穿过带着走。
            if (allowPlayerDodge)
            {
                AddPlayerDodge(ref desired, direction, distance, speed);
            }

            // 4) 切向让位：被挡住/被顶着时沿邻居较少的一侧滑移。
            if (allowSidestep)
            {
                float sidestep = Mathf.Min(
                    _config.EnemySidestepSpeed * frontBlock + _config.EnemyYieldSpeed * behindBlock,
                    _config.EnemySidestepSpeed + _config.EnemyYieldSpeed);
                AddSidestep(ref desired, direction, position, sidestep);
            }

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
            return newPosition;
        }

        /// <summary>径向 seek：到停距为 0；玩家压进来时最多以 backOutRatio 退开。</summary>
        private Vector2 ComputeSeekVelocity(Vector2 direction, float distance, float speed)
        {
            float excess = distance - _stopRadius;
            float radialFactor = Mathf.Clamp(excess * _config.EnemySeekGain, -_config.EnemyBackOutRatio, 1f);
            return direction * (radialFactor * speed);
        }

        /// <summary>
        /// 冲锋者状态机。Approach/Cooldown 走普通 seek + 人群管线；Windup/Recover 原地
        /// （只保留软墙/防重合，不跟随玩家侧闪）；Dash 走锁定方向直线，**完全跳过敌人-敌人分离/防重合**
        /// （软墙、让位、ComputeEmergency 都不参与，也不被别人推开），靠视觉提示 + 人工看片验收“穿阵”。
        /// 时间用 delta（已钳到 MaxDeltaTime）；暂停时 Update 提前返回。
        /// </summary>
        private void UpdateCharger(Vector2 position, Vector2 direction, float distance, float speed,
            float rawDelta, float delta)
        {
            switch (_chargerState)
            {
                case ChargerState.Approach:
                    if (distance <= _chargeTriggerDistance)
                    {
                        EnterChargerState(ChargerState.Windup);
                        return;
                    }

                    MoveChargerWithCrowd(position, direction, distance, speed, rawDelta, delta,
                        allowDodge: true, allowSidestep: true);
                    return;

                case ChargerState.Cooldown:
                    _chargerTimer -= delta;
                    if (_chargerTimer <= 0f)
                    {
                        EnterChargerState(ChargerState.Approach);
                        if (distance <= _chargeTriggerDistance)
                        {
                            EnterChargerState(ChargerState.Windup);
                            return;
                        }
                    }

                    // 冷却期：太近就后退到触发距离（否则会在 0.7 处再次前摇，玩家来不及反应）；
                    // 在触发距离附近原地等；太远才继续接近。
                    float gap = distance - _chargeTriggerDistance;
                    bool retreat = gap < -0.1f;
                    bool hold = !retreat && gap <= 0.1f;
                    MoveChargerWithCrowd(position, direction, distance, speed, rawDelta, delta,
                        allowDodge: true, allowSidestep: true, hold: hold, retreat: retreat);
                    return;

                case ChargerState.Windup:
                    _chargerTimer -= delta;

                    if (_chargerTimer <= 0f)
                    {
                        // 方向只在 Dash 开始锁定；前摇中玩家仍可横向拉开。
                        _chargeDirection = distance > DirectionEpsilon ? direction : _lastDirection;
                        _chargeHitDone = false;
                        _chargeStartedInsideMap = IsInsideMap(position);
                        EnterChargerState(ChargerState.Dash);
                        return;
                    }

                    MoveChargerWithCrowd(position, direction, distance, speed, rawDelta, delta,
                        allowDodge: false, allowSidestep: true);
                    return;

                case ChargerState.Dash:
                    UpdateChargerDash(position, rawDelta, delta);
                    return;

                case ChargerState.Recover:
                    _chargerTimer -= delta;
                    if (_chargerTimer <= 0f)
                    {
                        EnterChargerState(ChargerState.Cooldown);
                    }

                    MoveChargerWithCrowd(position, direction, distance, speed, rawDelta, delta,
                        allowDodge: false, allowSidestep: true);
                    return;
            }
        }

        private void MoveChargerWithCrowd(Vector2 position, Vector2 direction, float distance, float speed,
            float rawDelta, float delta, bool allowDodge, bool allowSidestep, bool hold = false, bool retreat = false)
        {
            // Windup/Recover 原地；Cooldown 太近后退、在触发距离附近原地、太远接近。
            Vector2 seek;
            if (retreat)
            {
                seek = -direction * speed;
            }
            else if (hold || _chargerState == ChargerState.Windup || _chargerState == ChargerState.Recover)
            {
                seek = Vector2.zero;
            }
            else
            {
                seek = ComputeSeekVelocity(direction, distance, speed);
            }

            Vector2 moved = ApplyCrowdMovement(position, direction, distance, speed, seek, delta,
                allowPlayerDodge: allowDodge, allowSidestep: allowSidestep);
            UpdateRecycle(moved, rawDelta);
        }

        private void EnterChargerState(ChargerState next)
        {
            _chargerState = next;
            switch (next)
            {
                case ChargerState.Approach:
                    _chargerTimer = 0f;
                    SetChargerSprite(_baseSprite, _baseTint, 1f);
                    break;

                case ChargerState.Windup:
                    _chargerTimer = _chargeWindupSeconds;
                    _velocity = Vector2.zero;
                    _emergencyVelocity = Vector2.zero;
                    // 前摇换黄色同轮廓图；旧资产没有 windup sprite 时回退 tint 方案。
                    SetChargerSprite(
                        _chargeWindupSprite != null ? _chargeWindupSprite : _baseSprite,
                        _chargeWindupSprite != null ? Color.white : ChargerWindupTint,
                        1.2f);
                    break;

                case ChargerState.Dash:
                    _chargerTimer = _chargeDurationSeconds;
                    _velocity = _chargeDirection * _chargeSpeed;
                    _emergencyVelocity = Vector2.zero;
                    FrontBlock = 0f;
                    BehindBlock = 0f;
                    _blockW = 0f;
                    _dashStartPosition = transform.position;
                    _dashElapsed = 0f;
                    // 冲刺视觉：半透明 + 1.15 缩放，给出“此刻会穿过人群”的可读信号。
                    // 只改 SpriteRenderer 颜色/缩放，不新增组件、不每帧分配（50 只也不掉帧）。
                    SetChargerSprite(_baseSprite, ChargerDashGhostTint, 1.15f);
                    break;

                case ChargerState.Recover:
                    _chargerTimer = _chargeRecoverSeconds;
                    _velocity = Vector2.zero;
                    _emergencyVelocity = Vector2.zero;
                    SetChargerSprite(_baseSprite, _baseTint, 1f);
                    break;

                case ChargerState.Cooldown:
                    _chargerTimer = _chargeCooldownSeconds;
                    SetChargerSprite(_baseSprite, _baseTint, 1f);
                    break;
            }

            if (s_logChargerStates)
            {
                float elapsed = _spawner != null ? _spawner.RoundElapsed : 0f;
                string dashInfo = next == ChargerState.Dash
                    ? $" speed={_chargeSpeed:F2} duration={_chargeDurationSeconds:F2} planned={_chargeDistance:F2}"
                    : string.Empty;
                Debug.Log($"[Charger] state={next} t={elapsed:F2} pos={transform.position.x:F2},{transform.position.y:F2}{dashInfo}");
            }
        }

        /// <summary>
        /// 冲刺：方向/速度锁定，不转向；只保留横向防重合，绝不抵消前进分量。
        /// 从地图内出发时撞到地图边界提前结束；从地图外生成的允许先走进来。
        /// </summary>
        private void UpdateChargerDash(Vector2 position, float rawDelta, float delta)
        {
            _chargerTimer -= delta;
            _dashElapsed += delta;

            Vector2 dashVelocity = _chargeDirection * _chargeSpeed;

            // 冲刺期间「穿阵」：不参与敌人-敌人分离/防重合，也不被别人推开（等价于冲刺中的质量无限大），
            // 方向/速度/距离/撞边界全部保持不变。玩家伤害 Trigger 不变（碰撞体没有关闭）。
            // 其他敌人仍把这里当邻居，用软墙/让位自然让开；冲刺 pair 由人工看片判断，不计入 minPairDist。
            Vector2 newPosition = position + dashVelocity * delta;

            if (_chargeStartedInsideMap && !IsInsideMap(newPosition))
            {
                newPosition = ClampToMap(newPosition);
                _chargerTimer = 0f;
            }

            transform.position = newPosition;
            UpdateRecycle(newPosition, rawDelta);

            if (!enabled)
            {
                return;
            }

            if (_chargerTimer <= 0f)
            {
                LogDashEnd(newPosition);
                EnterChargerState(ChargerState.Recover);
            }
        }

        /// <summary>
        /// 冲刺结束探针：实际位移与时长。撞地图边界会提前结束，此时 dist 小于 planned；
        /// 三档难度都应 ≥6.75（配置 7.0，留 0.25 容差）。
        /// </summary>
        private void LogDashEnd(Vector2 endPosition)
        {
            if (!s_logChargerStates)
            {
                return;
            }

            float elapsed = _spawner != null ? _spawner.RoundElapsed : 0f;
            float actual = Vector2.Distance(_dashStartPosition, endPosition);
            Debug.Log($"[Charger] dash-end t={elapsed:F2} dist={actual:F2} elapsed={_dashElapsed:F2} " +
                      $"speed={_chargeSpeed:F2} planned={_chargeDistance:F2}");
        }

        /// <summary>
        /// Charger 视觉：颜色已烤进 sprite，只换图 + 缩放；tint 保持白色。
        /// Windup 用黄色专用图，Dash 用红色基图，其余恢复基图。
        /// </summary>
        private void SetChargerSprite(Sprite sprite, Color tint, float scaleMultiplier)
        {
            if (_spriteRenderer != null)
            {
                _spriteRenderer.sprite = sprite != null ? sprite : _baseSprite;
                _spriteRenderer.color = tint;
            }

            transform.localScale = _baseScale * scaleMultiplier;
        }

        private bool IsInsideMap(Vector2 position)
        {
            float halfWidth = _config.MapWidth * 0.5f;
            float halfHeight = _config.MapHeight * 0.5f;
            return position.x >= -halfWidth && position.x <= halfWidth
                   && position.y >= -halfHeight && position.y <= halfHeight;
        }

        private Vector2 ClampToMap(Vector2 position)
        {
            float halfWidth = _config.MapWidth * 0.5f;
            float halfHeight = _config.MapHeight * 0.5f;
            return new Vector2(
                Mathf.Clamp(position.x, -halfWidth, halfWidth),
                Mathf.Clamp(position.y, -halfHeight, halfHeight));
        }

        /// <summary>
        /// 绕行者：侧后方目标点 2.4（相对玩家速度方向；玩家静止时相对自身方位），
        /// 距玩家 ≤1.2 直接扑；进入 4.5 后侧绕 6s 未切入则转直追。
        /// </summary>
        private void UpdateFlanker(Vector2 position, Vector2 direction, float distance, float speed,
            float rawDelta, float delta)
        {
            switch (_flankerState)
            {
                case FlankerState.Flank:
                    if (distance <= _flankPounceDistance)
                    {
                        EnterFlankerState(FlankerState.Pounce);
                        return;
                    }

                    // 只有进入交战距离后才计侧绕时间，出生赶路不算。
                    if (distance <= FlankEngageDistance)
                    {
                        _flankTimer -= delta;
                        if (_flankTimer <= 0f)
                        {
                            EnterFlankerState(FlankerState.Direct);
                            return;
                        }
                    }

                    if (s_logFlankerStates)
                    {
                        _flankLogTimer += delta;
                        if (_flankLogTimer >= FlankLogInterval)
                        {
                            _flankLogTimer = 0f;
                            LogFlankerTelemetry(position, distance);
                        }
                    }

                    Vector2 target = ComputeFlankTarget(position);
                    Vector2 toTarget = target - position;
                    if (!_flankPointReached && toTarget.sqrMagnitude <= 0.36f)
                    {
                        _flankPointReached = true;
                    }

                    Vector2 seek = _flankPointReached
                        ? ComputeSeekVelocity(direction, distance, speed)
                        : toTarget.normalized * speed;

                    MoveFlankerWithCrowd(position, direction, distance, speed, seek, rawDelta, delta);
                    return;

                case FlankerState.Pounce:
                case FlankerState.Direct:
                    Vector2 seekPlayer = ComputeSeekVelocity(direction, distance, speed);
                    MoveFlankerWithCrowd(position, direction, distance, speed, seekPlayer, rawDelta, delta);
                    return;
            }
        }

        private void MoveFlankerWithCrowd(Vector2 position, Vector2 direction, float distance, float speed,
            Vector2 seekVelocity, float rawDelta, float delta)
        {
            Vector2 moved = ApplyCrowdMovement(position, direction, distance, speed, seekVelocity, delta);
            UpdateRecycle(moved, rawDelta);
        }

        /// <summary>
        /// 目标点 = 玩家 + 侧后方 2.4。玩家在动就用速度方向做 forward；静止时用出生时的固定方位，
        /// 保证静止玩家也会被侧绕而不是被直线贴脸。目标点钳在地图内。
        /// </summary>
        private Vector2 ComputeFlankTarget(Vector2 position)
        {
            Vector2 playerPosition = _target.position;
            Vector2 playerVelocity = _playerBody != null ? _playerBody.linearVelocity : Vector2.zero;

            Vector2 forward;
            if (playerVelocity.sqrMagnitude > 0.09f)
            {
                forward = playerVelocity.normalized;
            }
            else
            {
                // 玩家静止：用出生时的固定方位。若用每帧“玩家→自己”的实时方位，
                // 目标点会跟着自己转，实际轨迹只是轻微内旋、近似正面直冲，绕不出侧后 135°。
                forward = _flankReferenceBearing;
            }

            float angle = _flankAngleDegrees * Mathf.Deg2Rad * _sideSign;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            Vector2 side = new Vector2(
                forward.x * cos - forward.y * sin,
                forward.x * sin + forward.y * cos);

            return ClampToMap(playerPosition + side * _flankRadius);
        }

        private void EnterFlankerState(FlankerState next)
        {
            _flankerState = next;
            _flankLogTimer = 0f;
            if (next == FlankerState.Flank)
            {
                _flankPointReached = false;
            }

            if (s_logFlankerStates)
            {
                float elapsed = _spawner != null ? _spawner.RoundElapsed : 0f;
                Debug.Log($"[Flanker] state={next} t={elapsed:F2} pos={transform.position.x:F2},{transform.position.y:F2}");
            }
        }

        /// <summary>
        /// 探针日志：记录侧绕轨迹。bearing 是“玩家朝向 → 玩家到敌人方向”的有符号夹角，
        /// 期望目标点约 ±135°（侧后方）；玩家静止时用“玩家→敌人”自身方位做 forward，与目标点同一基准。
        /// 只由 ?enemyKind=flanker&amp;enemyLimit≤2 打开，正常游玩/40 只压测不分配字符串。
        /// </summary>
        private void LogFlankerTelemetry(Vector2 position, float distance)
        {
            Vector2 playerPosition = _target.position;
            Vector2 playerVelocity = _playerBody != null ? _playerBody.linearVelocity : Vector2.zero;
            Vector2 toEnemy = position - playerPosition;

            Vector2 forward = playerVelocity.sqrMagnitude > 0.09f
                ? playerVelocity.normalized
                : _flankReferenceBearing;

            float bearing = toEnemy.sqrMagnitude > DirectionEpsilon
                ? Vector2.SignedAngle(forward, toEnemy)
                : 0f;
            float absAngle = toEnemy.sqrMagnitude > DirectionEpsilon
                ? Vector2.SignedAngle(Vector2.right, toEnemy)
                : 0f;
            float elapsed = _spawner != null ? _spawner.RoundElapsed : 0f;
            Debug.Log($"[Flanker] state=Flank t={elapsed:F2} dist={distance:F2} bearing={bearing:F0} " +
                      $"abs={absAngle:F0} pos={position.x:F2},{position.y:F2} " +
                      $"moving={(playerVelocity.sqrMagnitude > 0.09f ? 1 : 0)}");
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

            // 探针日志：只验证「进过视野后离屏 N 秒回收」，正常游玩不打开；限 20 行避免刷爆 console。
            if (s_logRecycle && s_logRecycleCount < 20)
            {
                s_logRecycleCount++;
                Debug.Log($"[Recycle] kind={_kind} offScreen={_offScreenSeconds:F2}s threshold={_config.EnemyRecycleSeconds:F2}s");
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

            // 伤害在 Initialize 里按难度算好（_contactDamage / _chargeDamage），这里是唯一伤害入口。
            // 冲锋一次最多结算一次：即使玩家处于无敌帧，本次冲刺也不再尝试第二次。
            if (_kind == EnemyKind.Charger && _chargerState == ChargerState.Dash && !_chargeHitDone)
            {
                _chargeHitDone = true;
                _playerHealth.TakeDamage(_chargeDamage);
                return;
            }

            _playerHealth.TakeDamage(_contactDamage);
        }
    }
}
