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

        /// <summary>只读列表，供压测探针读取；调用方不得增删。</summary>
        public IReadOnlyList<EnemyAI> ActiveEnemies => _enemies;

        private void Awake()
        {
            _root = new GameObject("Enemies").transform;
            _root.SetParent(transform, false);

            if (_camera == null)
            {
                _camera = Camera.main;
            }

            CachePlayerRadius();
            MinPairDistance = _config != null ? _config.EnemySeparationRadius : 1f;
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
                if (_enemies.Count < _config.EnemyLimit)
                {
                    SpawnEnemy();
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
                return;
            }

            _cellSize = Mathf.Max(0.5f, _config.EnemySeparationRadius);
            BuildGrid(count);

            float radius = _config.EnemySeparationRadius;
            float radiusSqr = radius * radius;
            float minSqr = float.MaxValue;

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

                            if (sqr < minSqr)
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
            MinPairDistance = _config != null ? _config.EnemySeparationRadius : 1f;
            _running = true;
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
            ai.Initialize(_config, definition, _playerTarget, _playerHealth, this, _camera, stopRadius);

            Register(ai);
        }

        /// <summary>M4 只有 1 个定义；变体阶段的加权选择在这里扩展。</summary>
        private EnemyDefinition PickDefinition()
        {
            if (_enemyDefinitions == null || _enemyDefinitions.Length == 0)
            {
                return null;
            }

            return _enemyDefinitions[Random.Range(0, _enemyDefinitions.Length)];
        }

        /// <summary>每 10 秒递减，不低于下限。</summary>
        private float GetCurrentInterval()
        {
            float steps = Mathf.Floor(_elapsed / TickSeconds);
            float interval = _config.SpawnIntervalStart - steps * _config.SpawnIntervalDecreasePerTenSeconds;
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
