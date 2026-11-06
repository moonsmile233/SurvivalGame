using System.Collections.Generic;
using Survival.Core;
using Survival.Player;
using UnityEngine;

namespace Survival.Enemy
{
    /// <summary>
    /// 定时生成敌人、间隔递减、同屏上限、清场、回收。不追踪玩家（规范 5.2）。
    /// 敌人是运行时动态物体，场景里不预摆（CLAUDE.md 硬约束 1）。
    /// 敌人类型来自 <see cref="EnemyDefinition"/> 数组（M4 只有 1 个基础追击定义）。
    ///
    /// M6 拥挤重做（2026-11-01）：
    /// - **没有站位槽**：敌人只在自己的 <see cref="EnemyAI"/> 里 seek「玩家 + 停距」，
    ///   被挤开就自然换位，不会被拉回固定点（旧的“站定 = 钉子”已删除）。
    /// - 本类只做两件事：均匀网格选出每只敌人最近的 ≤8 个邻居（30Hz），以及统计
    ///   <see cref="MinPairDistance"/> 给压测探针验收“没有两只长期 &lt;0.3”。
    ///   移动、软墙投影、切向让位、防重合兜底全部在 EnemyAI 里逐帧做。
    /// - 接触伤害仍是 Trigger 判定（EnemyAI.OnTriggerStay2D），玩家可穿过人群。
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        private const float TickSeconds = 10f;

        // 出生点相对玩家的半径系数：约 1.15 × 相机半对角线（16:9、ortho 5 时 ≈11.7）。
        private const float SpawnRadiusCameraRatio = 1.15f;

        // 出生点必须落在相机视野外（含该 margin），避免「脸上刷怪」。
        private const float SpawnViewMargin = 0.05f;
        private const int SpawnAttempts = 8;

        // 出生点与现有敌人的最小中心距：太小会一出生就重合（防重合兜底只应是异常路径）。
        private const float SpawnMinEnemyDistance = 0.45f;

        // 内圈停距留的缝隙：玩家半径 + 敌人半径 − 0.08，刚好让 Trigger 接触。
        private const float StopDistancePadding = 0.08f;

        // 每只敌人保留的邻居数（六边形密排 6 个 + 少量外圈）。
        private const int MaxNeighbors = 8;

        [SerializeField] private GameConfig _config;
        [SerializeField] private Transform _playerTarget;
        [SerializeField] private PlayerHealth _playerHealth;
        [SerializeField] private Camera _camera;
        [SerializeField] private EnemyDefinition[] _enemyDefinitions;

        // 只给 PerfProbe 的 ?enemyKind= 测试用；正常游玩为 null。
        private EnemyKind? _perfForcedKind;

        // 只给 PerfProbe：?enemyMix=1 从 0s 起用晚期权重（压测/证据），?noSpawn=1 完全停生成（alloc 0 敌对照）。
        private bool _perfForceMix;
        private bool _perfSuppressSpawn;
        private int _perfSpawnOpsMask;

        // 5-tick 生成袋（C5.2）：2 追击 + 2 绕行 + 1 冲锋；每轮洗牌，未解锁类型用追击补位。
        private const int SpawnBagSize = 5;
        private readonly EnemyKind[] _spawnBag = new EnemyKind[SpawnBagSize];
        private int _spawnBagIndex = SpawnBagSize;

        // 三类定义按 Kind 缓存（Awake 解析一次），加权选择不每帧扫数组。
        private EnemyDefinition _chaserDefinition;
        private EnemyDefinition _chargerDefinition;
        private EnemyDefinition _flankerDefinition;

        private readonly List<EnemyAI> _enemies = new List<EnemyAI>(24);

        private Transform _root;
        private float _elapsed;
        private float _spawnTimer;
        private bool _running;
        private float _separationAccumulator;
        private float _playerRadius = 0.4f;

        // 均匀网格（选邻居用）：cell ≈ 软分离半径，只查相邻 9 格；字典与 bucket 列表复用，稳态无分配。
        private readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>(256);
        private float _cellSize = 1f;

        // 邻居选择的临时缓冲（单线程、循环内复用，不分配）。
        private readonly EnemyAI[] _neighborScratch = new EnemyAI[MaxNeighbors];
        private readonly float[] _neighborDistanceSqr = new float[MaxNeighbors];

        public int ActiveCount => _enemies.Count;

        /// <summary>本局已进行秒数，供压力曲线使用（M4-B1）。</summary>
        public float RoundElapsed => _elapsed;

        /// <summary>
        /// 最近一次邻居刷新时，全场最小的敌人中心距；没有两只在软分离半径内时返回软分离半径。
        /// 30Hz 更新（比帧率低），压测探针读的是“最后一个 tick 的值”——一次 33ms 的瞬时穿透可能漏采，
        /// 但验收口径是“不长期 &lt;0.3”，这个精度足够。
        /// </summary>
        public float MinPairDistance { get; private set; }

        /// <summary>
        /// 全场最小中心距（**包含**正在 Dash 的 Charger 参与的 pair）。冲刺按设计穿阵，
        /// 这个值会被刻意压得很低；验收看 <see cref="MinPairDistance"/>（非冲刺 pair）。
        /// </summary>
        public float MinPairDistanceAll { get; private set; }

        /// <summary>最近一次刷新时「含冲刺 Charger」的 pair 数（每对计一次）；证明指标没把数据做没。</summary>
        public int DashPairCount { get; private set; }

        /// <summary>只读列表，供压测探针读取；调用方不得增删。</summary>
        public IReadOnlyList<EnemyAI> ActiveEnemies => _enemies;

        /// <summary>只给 PerfProbe 用：强制生成某种敌人（C3/C4 单类型探针）；正常游玩不调用。</summary>
        public void SetPerfForcedKind(EnemyKind kind) => _perfForcedKind = kind;

        /// <summary>只给 PerfProbe 的 ?enemyMix=1：从 0s 起用晚期权重生成三种敌人（压测/证据用）。</summary>
        public void SetPerfForceMix(bool value) => _perfForceMix = value;

        /// <summary>只给 PerfProbe 的 ?noSpawn=1：完全停止生成，用于 alloc「0 敌」对照；正常游玩不调用。</summary>
        public void SetPerfSuppressSpawn(bool value) => _perfSuppressSpawn = value;

        /// <summary>
        /// 只给 PerfProbe 的 ?spawnOps= 位掩码：逐个关掉生成时的健壮性操作，用于 alloc 归因。
        /// bit0=跳过 renderer.color、bit1=跳过 localScale、bit2=不传 renderer（走 GetComponent 回退）。
        /// 正常游玩 mask=0（全部执行），所以这里只影响归因跑法，不改默认行为。
        /// </summary>
        public void SetPerfSpawnOps(int skipMask) => _perfSpawnOpsMask = Mathf.Max(0, skipMask);

        /// <summary>到顶时找不到可回收目标（未进过视野 / 都在视野内）而跳过的生成次数；探针只读。</summary>
        public int CapBlockedCount { get; private set; }

        /// <summary>到顶时为补位回收的敌人数量；探针只读。生成不会静默停刷，被挡下时计入 CapBlockedCount。</summary>
        public int CapRecycledCount { get; private set; }

        /// <summary>本局所有登记后回收（销毁）的敌人总数，含到顶补位；探针只读。
        /// 视野超时回收 = RecycleCount − CapRecycledCount。</summary>
        public int RecycleCount { get; private set; }

        /// <summary>本局累计生成数量（按类型）；探针只读，用来验证权重而不是只看存活快照。</summary>
        public int SpawnedChaserCount { get; private set; }
        public int SpawnedChargerCount { get; private set; }
        public int SpawnedFlankerCount { get; private set; }

        private void Awake()
        {
            _root = new GameObject("Enemies").transform;
            _root.SetParent(transform, false);

            if (_camera == null)
            {
                _camera = Camera.main;
            }

            CachePlayerRadius();
            CacheDefinitions();
            MinPairDistance = _config != null ? _config.EnemySeparationRadius : 1f;
        }

        /// <summary>按 Kind 缓存定义；缺失的类别权重按 0 处理（旧场景只有 Chaser 也能跑）。</summary>
        private void CacheDefinitions()
        {
            if (_enemyDefinitions == null)
            {
                return;
            }

            for (int i = 0; i < _enemyDefinitions.Length; i++)
            {
                EnemyDefinition definition = _enemyDefinitions[i];
                if (definition == null)
                {
                    continue;
                }

                switch (definition.Kind)
                {
                    case EnemyKind.Chaser:
                        if (_chaserDefinition == null)
                        {
                            _chaserDefinition = definition;
                        }

                        break;
                    case EnemyKind.Charger:
                        if (_chargerDefinition == null)
                        {
                            _chargerDefinition = definition;
                        }

                        break;
                    case EnemyKind.Flanker:
                        if (_flankerDefinition == null)
                        {
                            _flankerDefinition = definition;
                        }

                        break;
                }
            }
        }

        private void Update()
        {
            if (!_running || _config == null || Time.deltaTime <= 0f)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            _spawnTimer += Time.deltaTime;

            if (_spawnTimer >= GetCurrentInterval())
            {
                _spawnTimer = 0f;
                if (!_perfSuppressSpawn)
                {
                    if (_enemies.Count < _config.EnemyLimit)
                    {
                        SpawnEnemy();
                    }
                    else if (TryRecycleFarthestOffscreen())
                    {
                        // 同帧补位：回收后立刻生成。生成点仍在视野外，玩家看到的只是“视野外换了一只”。
                        SpawnEnemy();
                    }
                    else
                    {
                        // 没有合格候选（都在视野内，或还没进过视野）：记账后跳过，探针能看到 blocked 而不是静默停刷。
                        CapBlockedCount++;
                    }
                }
            }

            // 邻居表 30Hz：只决定“看得见谁”；扣速度/积分仍在 EnemyAI 里逐帧做。
            float interval = 1f / Mathf.Max(1f, _config.EnemySeparationHz);
            _separationAccumulator += Time.deltaTime;
            if (_separationAccumulator >= interval)
            {
                // 用减法保留余数（高刷新率下不会漂到低于目标频率）；卡顿帧最多补 2 个 tick。
                _separationAccumulator -= interval;
                if (_separationAccumulator > interval)
                {
                    _separationAccumulator = interval;
                }

                RefreshNeighbors();
            }
        }

        /// <summary>
        /// 均匀网格 + 每敌最近 ≤8 个邻居（存引用，AI 逐帧读实时位置）：
        /// 只查相邻 9 格，O(n × 邻居) 而不是 O(n²)；同时统计全场最小中心距。
        /// </summary>
        private void RefreshNeighbors()
        {
            int count = _enemies.Count;
            if (count == 0)
            {
                MinPairDistance = _config.EnemySeparationRadius;
                MinPairDistanceAll = _config.EnemySeparationRadius;
                DashPairCount = 0;
                return;
            }

            _cellSize = Mathf.Max(0.5f, _config.EnemySeparationRadius);
            BuildGrid(count);

            float radius = _config.EnemySeparationRadius;
            float radiusSqr = radius * radius;
            float minSqr = float.MaxValue;
            float minSqrAll = float.MaxValue;
            int dashPairs = 0;

            for (int i = 0; i < count; i++)
            {
                EnemyAI enemy = _enemies[i];
                if (enemy == null)
                {
                    continue;
                }

                Vector2 position = enemy.transform.position;
                int cellX = Mathf.FloorToInt(position.x / _cellSize);
                int cellY = Mathf.FloorToInt(position.y / _cellSize);
                int neighborCount = 0;

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (!_grid.TryGetValue(CellKey(cellX + dx, cellY + dy), out List<int> bucket))
                        {
                            continue;
                        }

                        for (int b = 0; b < bucket.Count; b++)
                        {
                            int j = bucket[b];
                            if (j == i)
                            {
                                continue;
                            }

                            EnemyAI other = _enemies[j];
                            if (other == null)
                            {
                                continue;
                            }

                            Vector2 otherPosition = other.transform.position;
                            float deltaX = otherPosition.x - position.x;
                            float deltaY = otherPosition.y - position.y;
                            float sqr = deltaX * deltaX + deltaY * deltaY;
                            if (sqr >= radiusSqr)
                            {
                                continue;
                            }

                            if (sqr < minSqrAll)
                            {
                                minSqrAll = sqr;
                            }

                            // 冲刺中的 Charger 按设计穿阵：这些 pair 不计入验收指标 minPairDist，
                            // 但单独计数，避免把指标“做没”。i < j 保证同一对只计一次。
                            if (enemy.IsCharging || other.IsCharging)
                            {
                                if (i < j)
                                {
                                    dashPairs++;
                                }
                            }
                            else if (sqr < minSqr)
                            {
                                minSqr = sqr;
                            }

                            neighborCount = InsertNeighbor(other, sqr, neighborCount);
                        }
                    }
                }

                enemy.SetNeighbors(_neighborScratch, neighborCount);
            }

            MinPairDistance = minSqr < float.MaxValue ? Mathf.Sqrt(minSqr) : radius;
            MinPairDistanceAll = minSqrAll < float.MaxValue ? Mathf.Sqrt(minSqrAll) : radius;
            DashPairCount = dashPairs;
        }

        /// <summary>把候选按距离插入最近 MaxNeighbors 个（小数组插入排序，无分配）。</summary>
        private int InsertNeighbor(EnemyAI candidate, float distanceSqr, int count)
        {
            int limit = Mathf.Min(count, MaxNeighbors);
            if (limit == MaxNeighbors && distanceSqr >= _neighborDistanceSqr[MaxNeighbors - 1])
            {
                return count;
            }

            int insert = limit;
            while (insert > 0 && _neighborDistanceSqr[insert - 1] > distanceSqr)
            {
                insert--;
            }

            int last = Mathf.Min(limit, MaxNeighbors - 1);
            for (int k = last; k > insert; k--)
            {
                _neighborScratch[k] = _neighborScratch[k - 1];
                _neighborDistanceSqr[k] = _neighborDistanceSqr[k - 1];
            }

            _neighborScratch[insert] = candidate;
            _neighborDistanceSqr[insert] = distanceSqr;
            return Mathf.Min(limit + 1, MaxNeighbors);
        }

        private void BuildGrid(int count)
        {
            foreach (List<int> bucket in _grid.Values)
            {
                bucket.Clear();
            }

            for (int i = 0; i < count; i++)
            {
                EnemyAI enemy = _enemies[i];
                if (enemy == null)
                {
                    continue;
                }

                Vector2 position = enemy.transform.position;
                long key = CellKey(Mathf.FloorToInt(position.x / _cellSize), Mathf.FloorToInt(position.y / _cellSize));
                if (!_grid.TryGetValue(key, out List<int> bucket))
                {
                    bucket = new List<int>(4);
                    _grid[key] = bucket;
                }

                bucket.Add(i);
            }
        }

        private static long CellKey(int cellX, int cellY)
        {
            return ((long)cellX << 32) ^ (uint)cellY;
        }

        /// <summary>开始新一局：清场 + 计时归零 + 开始生成。</summary>
        public void BeginRound()
        {
            ClearEnemies();
            _elapsed = 0f;
            _spawnTimer = 0f;
            _separationAccumulator = 0f;
            CapBlockedCount = 0;
            CapRecycledCount = 0;
            RecycleCount = 0;
            SpawnedChaserCount = 0;
            SpawnedChargerCount = 0;
            SpawnedFlankerCount = 0;
            MinPairDistance = _config != null ? _config.EnemySeparationRadius : 1f;
            _running = true;

            // 每局一行，方便核对档位是否真的生效（间隔/上限是难度最容易配错的两项）。
            // C5 起再带上解锁时间，自然混合跑完后能和 [PERF-SUMMARY] 的 per-kind 数量对上。
            if (_config != null)
            {
                DifficultyProfile profile = _config.ActiveProfile;
                string unlock = profile != null
                    ? $" chargerUnlock={profile.ChargerUnlockSeconds:F0}s flankerUnlock={profile.FlankerUnlockSeconds:F0}s"
                    : string.Empty;
                Debug.Log($"[EnemySpawner] begin difficulty={_config.ActiveDifficulty} interval={GetCurrentInterval():F3}s limit={_config.EnemyLimit}{unlock}");
            }
        }

        public void StopSpawning()
        {
            _running = false;
        }

        public void ClearEnemies()
        {
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                EnemyAI enemy = _enemies[i];
                if (enemy != null)
                {
                    Destroy(enemy.gameObject);
                }
            }

            _enemies.Clear();
        }

        /// <summary>新敌人登记；重复登记直接忽略（幂等）。</summary>
        public void Register(EnemyAI enemy)
        {
            if (enemy != null && !_enemies.Contains(enemy))
            {
                _enemies.Add(enemy);
            }
        }

        /// <summary>
        /// 视野外回收：从活动列表移除并销毁，释放名额（M4-B0 2.4）。
        /// 幂等：不在列表里（已回收过）直接返回，不会误删别的对象。
        /// </summary>
        public void Unregister(EnemyAI enemy)
        {
            if (enemy == null || !_enemies.Remove(enemy))
            {
                return;
            }

            RecycleCount++;
            Destroy(enemy.gameObject);
        }

        /// <summary>只从列表移除、不销毁（对象已在销毁流程中时用，避免重复 Destroy）。</summary>
        public void Forget(EnemyAI enemy)
        {
            if (enemy != null)
            {
                _enemies.Remove(enemy);
            }
        }

        private void SpawnEnemy()
        {
            EnemyDefinition definition = PickDefinition();
            if (definition == null)
            {
                Debug.LogError("[EnemySpawner] 没有可用的 EnemyDefinition，停止生成。");
                _running = false;
                return;
            }

            var go = new GameObject("Enemy");
            go.transform.SetParent(_root, false);
            go.transform.position = GetSpawnPosition();

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = definition.Sprite;
            renderer.sortingOrder = 1;
            if ((_perfSpawnOpsMask & 1) == 0)
            {
                renderer.color = definition.Tint;
            }

            if ((_perfSpawnOpsMask & 2) == 0)
            {
                go.transform.localScale = Vector3.one * definition.VisualScale;
            }

            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;

            var collider = go.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = definition.ColliderRadius;

            // 停距按“本敌人”的碰撞半径算：以后加冲锋/绕行等变体时不用改生成器。
            float enemyRadius = definition.ColliderRadius > 0f ? definition.ColliderRadius : 0.38f;
            float stopRadius = _playerRadius + enemyRadius - StopDistancePadding;

            var ai = go.AddComponent<EnemyAI>();
            ai.Initialize(_config, definition, _playerTarget, _playerHealth, this, _camera, stopRadius,
                (_perfSpawnOpsMask & 4) != 0 ? null : renderer);

            switch (definition.Kind)
            {
                case EnemyKind.Chaser:
                    SpawnedChaserCount++;
                    break;
                case EnemyKind.Charger:
                    SpawnedChargerCount++;
                    break;
                case EnemyKind.Flanker:
                    SpawnedFlankerCount++;
                    break;
            }

            Register(ai);
        }

        /// <summary>
        /// 每 tick 出一只，类型按 5-tick 袋轮换：2 追击 + 2 绕行 + 1 冲锋（全解锁后 40/40/20）。
        /// 未解锁的类型用追击补位；每轮 Fisher–Yates 洗牌避免固定顺序。
        /// 权重字段仍保留在 DifficultyProfile 里，但不再参与自然生成。
        /// PerfProbe 的 enemyKind 强制单类型优先；enemyMix=1 从 0s 起按同一袋强制三类参与（仅压测）。
        /// </summary>
        private EnemyDefinition PickDefinition()
        {
            if (_enemyDefinitions == null || _enemyDefinitions.Length == 0)
            {
                return null;
            }

            if (_perfForcedKind.HasValue)
            {
                for (int i = 0; i < _enemyDefinitions.Length; i++)
                {
                    EnemyDefinition forced = _enemyDefinitions[i];
                    if (forced != null && forced.Kind == _perfForcedKind.Value)
                    {
                        return forced;
                    }
                }

                Debug.LogError($"[EnemySpawner] 找不到强制类型 {_perfForcedKind.Value} 的 EnemyDefinition。");
                return null;
            }

            DifficultyProfile profile = _config != null ? _config.ActiveProfile : null;
            if (profile == null || profile.NeedsSpawnWeightMigration)
            {
                // 没挂难度档案或旧资产没迁移：保持 v1.2 行为（只生成 Chaser）。
                return FallbackDefinition();
            }

            if (_spawnBagIndex >= SpawnBagSize)
            {
                RefillSpawnBag(profile, _elapsed);
            }

            switch (_spawnBag[_spawnBagIndex++])
            {
                case EnemyKind.Flanker:
                    return _flankerDefinition != null ? _flankerDefinition : FallbackDefinition();
                case EnemyKind.Charger:
                    return _chargerDefinition != null ? _chargerDefinition : FallbackDefinition();
                default:
                    return _chaserDefinition != null ? _chaserDefinition : FallbackDefinition();
            }
        }

        /// <summary>
        /// 重填 5-tick 袋：2 追击 + 2 绕行 + 1 冲锋；未解锁位置用追击补位。
        /// 洗牌用原地交换（无分配）；袋只在重填时按当前局内时间判断解锁，轮内不跳变。
        /// </summary>
        private void RefillSpawnBag(DifficultyProfile profile, float time)
        {
            bool chargerUnlocked = _perfForceMix || time >= profile.ChargerUnlockSeconds;
            bool flankerUnlocked = _perfForceMix || time >= profile.FlankerUnlockSeconds;

            _spawnBag[0] = EnemyKind.Chaser;
            _spawnBag[1] = EnemyKind.Chaser;
            _spawnBag[2] = flankerUnlocked ? EnemyKind.Flanker : EnemyKind.Chaser;
            _spawnBag[3] = flankerUnlocked ? EnemyKind.Flanker : EnemyKind.Chaser;
            _spawnBag[4] = chargerUnlocked ? EnemyKind.Charger : EnemyKind.Chaser;

            for (int i = SpawnBagSize - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                EnemyKind swap = _spawnBag[i];
                _spawnBag[i] = _spawnBag[j];
                _spawnBag[j] = swap;
            }

            _spawnBagIndex = 0;
        }

        private EnemyDefinition FallbackDefinition()
        {
            if (_chaserDefinition != null)
            {
                return _chaserDefinition;
            }

            if (_chargerDefinition != null)
            {
                return _chargerDefinition;
            }

            if (_flankerDefinition != null)
            {
                return _flankerDefinition;
            }

            return _enemyDefinitions.Length > 0 ? _enemyDefinitions[0] : null;
        }

        /// <summary>
        /// 到顶补位（M6 §3.3）：回收「进过视野 + 当前在视野外」里离玩家最远的一只。
        /// 只回收进过视野的，避免误杀刚出生、还在视野外赶来的敌人；补位生成点仍在视野外。
        /// 找到就 Unregister（释放名额）并返回 true；没有候选返回 false（调用方计 CapBlockedCount）。
        /// </summary>
        private bool TryRecycleFarthestOffscreen()
        {
            if (_camera == null || _playerTarget == null || _enemies.Count == 0)
            {
                return false;
            }

            EnemyAI candidate = null;
            float farthestSqr = -1f;
            Vector2 playerPosition = _playerTarget.position;

            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyAI enemy = _enemies[i];
                if (enemy == null || !enemy.HasBeenSeen)
                {
                    continue;
                }

                Vector3 position = enemy.transform.position;
                if (IsInsideView(position))
                {
                    continue;
                }

                float deltaX = position.x - playerPosition.x;
                float deltaY = position.y - playerPosition.y;
                float sqr = deltaX * deltaX + deltaY * deltaY;
                if (sqr > farthestSqr)
                {
                    farthestSqr = sqr;
                    candidate = enemy;
                }
            }

            if (candidate == null)
            {
                return false;
            }

            Unregister(candidate);
            CapRecycledCount++;
            return true;
        }

        /// <summary>
        /// 每 10 秒递减，不低于下限；难度倍率乘在「当前间隔」上再钳下限（M6 §4：困难下限仍 0.5s）。
        /// </summary>
        private float GetCurrentInterval()
        {
            float steps = Mathf.Floor(_elapsed / TickSeconds);
            float interval = _config.SpawnIntervalStart - steps * _config.SpawnIntervalDecreasePerTenSeconds;
            interval *= _config.SpawnIntervalMultiplier;
            return Mathf.Max(_config.SpawnIntervalMin, interval);
        }

        // ---------- 出生点 ----------

        /// <summary>
        /// 出生点：以玩家为中心、半径约 1.15×相机半对角线的随机点，且必须在相机视野外（M4-B0）。
        /// 相机跟随有滞后，只按相机视口算会在相机前进方向的边缘「脸上刷怪」，所以以玩家为圆心。
        /// 允许落在地图外（虚空），敌人走进来即可。
        /// 候选点与现有敌人 &lt;0.45 时重试，避免一出生就完全重合（重合兜底只留给异常）。
        /// </summary>
        private Vector3 GetSpawnPosition()
        {
            if (_camera == null || _playerTarget == null)
            {
                return Vector3.zero;
            }

            Vector3 center = _playerTarget.position;
            float radius = GetSpawnRadius();
            Vector3 fallback = center;
            float fallbackScore = -1f;

            for (int attempt = 0; attempt < SpawnAttempts; attempt++)
            {
                Vector2 direction = Random.insideUnitCircle;
                if (direction.sqrMagnitude < 0.0001f)
                {
                    direction = Vector2.up;
                }

                Vector3 candidate = center + (Vector3)(direction.normalized * radius);
                candidate.z = 0f;

                if (!IsInsideView(candidate) && IsSpawnPositionClear(candidate))
                {
                    return candidate;
                }

                // 兜底：留一个离视野中心最远的候选（常规 16:9 下圆周半径大于半对角线，通常用不到）。
                float score = ViewportDistanceSqr(candidate);
                if (score > fallbackScore)
                {
                    fallbackScore = score;
                    fallback = candidate;
                }
            }

            return fallback;
        }

        private bool IsSpawnPositionClear(Vector3 candidate)
        {
            float minSqr = SpawnMinEnemyDistance * SpawnMinEnemyDistance;
            for (int i = 0; i < _enemies.Count; i++)
            {
                EnemyAI enemy = _enemies[i];
                if (enemy == null)
                {
                    continue;
                }

                Vector2 position = enemy.transform.position;
                float deltaX = position.x - candidate.x;
                float deltaY = position.y - candidate.y;
                if (deltaX * deltaX + deltaY * deltaY < minSqr)
                {
                    return false;
                }
            }

            return true;
        }

        private void CachePlayerRadius()
        {
            _playerRadius = 0.4f;
            if (_playerHealth == null)
            {
                return;
            }

            var circle = _playerHealth.GetComponent<CircleCollider2D>();
            if (circle == null)
            {
                return;
            }

            Vector3 scale = circle.transform.lossyScale;
            _playerRadius = circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        }

        private float GetSpawnRadius()
        {
            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;
            return Mathf.Sqrt(halfWidth * halfWidth + halfHeight * halfHeight) * SpawnRadiusCameraRatio;
        }

        private bool IsInsideView(Vector3 world)
        {
            Vector3 viewport = _camera.WorldToViewportPoint(world);
            return viewport.z > 0f
                   && viewport.x >= -SpawnViewMargin && viewport.x <= 1f + SpawnViewMargin
                   && viewport.y >= -SpawnViewMargin && viewport.y <= 1f + SpawnViewMargin;
        }

        private float ViewportDistanceSqr(Vector3 world)
        {
            Vector3 viewport = _camera.WorldToViewportPoint(world);
            float dx = viewport.x - 0.5f;
            float dy = viewport.y - 0.5f;
            return dx * dx + dy * dy;
        }
    }
}
