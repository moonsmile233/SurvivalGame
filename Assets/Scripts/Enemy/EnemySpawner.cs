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
    /// M6 拥挤修复（2026-11-01）——解决"玻璃球互推/微抖"：
    /// - **站位槽唯一**：不再 12 槽轮转（20～40 只时一槽 2～4 只共享同一目标点，走到位后又被推开、再走回去，
    ///   形成每帧小抖动）。现在按环半径生成尽量多的槽位（环半径 = 停距 + ring × 间距，环容量 = 周长/间距），
    ///   每只敌人拿到唯一槽位；槽位随回收释放、新敌人优先补内圈空位。
    /// - **分离修正**：改成 20Hz + 均匀网格邻域 + Jacobi（先累加再统一应用）+ 阻尼 + 滞回（< 0.9×间距才推，
    ///   推到间距为止）+ 单次最大位移；已经到位的敌人（IsSettled）不参与位移，站位环被"钉住"。
    /// - 接触伤害仍只在最内环（停距恰好让 CircleCollider2D 触发）；外环只是视觉/包围，不造成伤害。
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        private const float TickSeconds = 10f;

        // 出生点相对玩家的半径系数：约 1.15 × 相机半对角线（16:9、ortho 5 时 ≈11.7）。
        private const float SpawnRadiusCameraRatio = 1.15f;

        // 出生点必须落在相机视野外（含该 margin），避免「脸上刷怪」。
        private const float SpawnViewMargin = 0.05f;
        private const int SpawnAttempts = 8;

        // 内圈停距留的缝隙：玩家半径 + 敌人半径 − 0.08，刚好让 Trigger 接触。
        private const float StopDistancePadding = 0.08f;

        // 槽位总容量上限：环数 × 每环容量，到 80 只压测都够用。
        private const int MaxStanceSlots = 128;

        [SerializeField] private GameConfig _config;
        [SerializeField] private Transform _playerTarget;
        [SerializeField] private PlayerHealth _playerHealth;
        [SerializeField] private Camera _camera;
        [SerializeField] private EnemyDefinition[] _enemyDefinitions;

        private readonly List<EnemyAI> _enemies = new List<EnemyAI>(24);

        // 站位槽：一环一圈，同环内角度均分；相邻环错开半格，避免径向正对。
        private struct StanceSlot
        {
            public float Angle;
            public float Radius;
        }

        private StanceSlot[] _slots;
        private bool[] _slotUsed;
        private int _slotCursor;
        private int _roundGeneration;

        private Transform _root;
        private float _elapsed;
        private float _spawnTimer;
        private bool _running;
        private float _separationAccumulator;

        // 均匀网格（分离修正用）：cell ≈ 间距，只查相邻 9 格；字典与 bucket 列表复用，稳态无分配。
        private readonly Dictionary<long, List<int>> _grid = new Dictionary<long, List<int>>(256);
        private float[] _pushX = new float[MaxStanceSlots];
        private float[] _pushY = new float[MaxStanceSlots];
        private float _cellSize = 1f;

        public int ActiveCount => _enemies.Count;

        /// <summary>本局已进行秒数，供压力曲线使用（M4-B1）。</summary>
        public float RoundElapsed => _elapsed;

        /// <summary>只读列表，供分离修正与压测探针读取；调用方不得增删。</summary>
        public IReadOnlyList<EnemyAI> ActiveEnemies => _enemies;

        private void Awake()
        {
            _root = new GameObject("Enemies").transform;
            _root.SetParent(transform, false);

            if (_camera == null)
            {
                _camera = Camera.main;
            }

            BuildStanceSlots();
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

            // 分离修正是低频的强制定量位移：20Hz 足够，跑 60Hz 反而会和 seek 打架（玻璃球感的来源之一）。
            float interval = 1f / Mathf.Max(1f, _config.EnemySeparationHz);
            _separationAccumulator += Time.deltaTime;
            if (_separationAccumulator >= interval)
            {
                _separationAccumulator = 0f;
                ApplySeparationCorrection();
            }
        }

        /// <summary>
        /// 均匀网格 + Jacobi 分离：先在位置快照上累加每只敌人受到的推力，最后统一应用并钳制单次位移。
        /// 不按顺序两两修改（旧实现顺序修正会把同一只敌人推两三次，越推越抖）。
        /// 只要有一方已经进入站位区（IsNearStance，&lt; 1.0），这一对就不推：
        /// 站位环是钉子（不被推走），同时也不会把赶路的敌人挡在环外（否则新敌人挤不进内圈）。
        /// </summary>
        private void ApplySeparationCorrection()
        {
            int count = _enemies.Count;
            if (count < 2)
            {
                return;
            }

            EnsurePushBuffers(count);
            for (int i = 0; i < count; i++)
            {
                _pushX[i] = 0f;
                _pushY[i] = 0f;
            }

            BuildGrid(count);

            float spacing = _config.EnemySpacing;
            float startDistance = spacing * _config.EnemySeparationStartRatio;
            float startSqr = startDistance * startDistance;
            float damping = _config.EnemySeparationDamping;

            for (int i = 0; i < count; i++)
            {
                EnemyAI first = _enemies[i];
                if (first == null)
                {
                    continue;
                }

                Vector2 firstPosition = first.transform.position;
                int cellX = Mathf.FloorToInt(firstPosition.x / _cellSize);
                int cellY = Mathf.FloorToInt(firstPosition.y / _cellSize);

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
                            if (j <= i)
                            {
                                continue;
                            }

                            EnemyAI second = _enemies[j];
                            if (second == null)
                            {
                                continue;
                            }

                            // 只要有一方已进入站位区（< 1.0），这一对就不推：
                            // - 站位环是钉子，不能被推走（否则「推开—走回」微抖）；
                            // - 环也不能挡住赶路的敌人，否则外环变成不可穿透的壳，后到内圈的敌人
                            //   永远挤不进来（实测会卡在 6.5 单位外）。穿过时短暂交叠可以接受。
                            if (first.IsNearStance || second.IsNearStance)
                            {
                                continue;
                            }

                            Vector2 delta = (Vector2)second.transform.position - firstPosition;
                            float sqrDistance = delta.sqrMagnitude;
                            if (sqrDistance >= startSqr || sqrDistance <= 0.000001f)
                            {
                                continue;
                            }

                            float distance = Mathf.Sqrt(sqrDistance);
                            Vector2 direction = delta / distance;
                            float push = (spacing - distance) * 0.5f * damping;

                            _pushX[i] -= direction.x * push;
                            _pushY[i] -= direction.y * push;
                            _pushX[j] += direction.x * push;
                            _pushY[j] += direction.y * push;
                        }
                    }
                }
            }

            float maxStep = _config.EnemySeparationMaxStep;
            for (int i = 0; i < count; i++)
            {
                EnemyAI enemy = _enemies[i];
                if (enemy == null || enemy.IsNearStance)
                {
                    continue;
                }

                float pushX = _pushX[i];
                float pushY = _pushY[i];
                float magnitude = Mathf.Sqrt(pushX * pushX + pushY * pushY);
                if (magnitude <= 0.00001f)
                {
                    continue;
                }

                if (magnitude > maxStep)
                {
                    float scale = maxStep / magnitude;
                    pushX *= scale;
                    pushY *= scale;
                }

                Vector3 position = enemy.transform.position;
                enemy.transform.position = new Vector3(position.x + pushX, position.y + pushY, position.z);
            }
        }

        private void EnsurePushBuffers(int count)
        {
            if (_pushX.Length >= count)
            {
                return;
            }

            int size = Mathf.Max(count, _pushX.Length * 2);
            _pushX = new float[size];
            _pushY = new float[size];
        }

        private void BuildGrid(int count)
        {
            _cellSize = Mathf.Max(0.5f, _config.EnemySpacing);
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

        /// <summary>开始新一局：清场 + 计时归零 + 开始生成；generation 递增让旧敌人的槽位释放失效。</summary>
        public void BeginRound()
        {
            _roundGeneration++;
            ClearEnemies();
            ResetStanceSlots();
            _elapsed = 0f;
            _spawnTimer = 0f;
            _separationAccumulator = 0f;
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

            int slotIndex = AcquireStanceSlot();
            StanceSlot slot;
            if (slotIndex >= 0)
            {
                slot = _slots[slotIndex];
            }
            else
            {
                // 槽位耗尽（只在压测 >100 只时可能）：退化为随机角度，外环再加一圈。
                slot = new StanceSlot
                {
                    Angle = Random.Range(0f, Mathf.PI * 2f),
                    Radius = GetBaseStopRadius() + 5f * _config.EnemySpacing,
                };
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

            var ai = go.AddComponent<EnemyAI>();
            ai.Initialize(_config, definition, _playerTarget, _playerHealth, this, _camera,
                slot.Angle, slot.Radius, slotIndex, _roundGeneration);

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

        // ---------- 站位槽 ----------

        /// <summary>
        /// 按「环半径 = 基础停距 + ring × 间距、容量 = 周长 / 间距」生成槽位。
        /// 同环角度均分，相邻环错开半格；槽位总数远超常见上限（≥80），保证每只敌人有唯一目标点。
        /// 只在 Awake 构建一次：间距/停距都是本局不变的值。
        /// </summary>
        private void BuildStanceSlots()
        {
            if (_config == null)
            {
                _slots = new StanceSlot[0];
                _slotUsed = new bool[0];
                return;
            }

            float spacing = Mathf.Max(0.1f, _config.EnemySpacing);
            float baseRadius = GetBaseStopRadius();
            var slots = new List<StanceSlot>(MaxStanceSlots);
            const float TwoPi = Mathf.PI * 2f;

            for (int ring = 0; ring < 8 && slots.Count < MaxStanceSlots; ring++)
            {
                float radius = baseRadius + ring * spacing;
                int capacity = Mathf.Max(1, Mathf.FloorToInt(TwoPi * radius / spacing));
                float phase = (ring % 2) * 0.5f;

                for (int k = 0; k < capacity; k++)
                {
                    slots.Add(new StanceSlot
                    {
                        Angle = (k + phase) * (TwoPi / capacity),
                        Radius = radius,
                    });
                }
            }

            _slots = slots.ToArray();
            _slotUsed = new bool[_slots.Length];
        }

        private void ResetStanceSlots()
        {
            if (_slotUsed == null)
            {
                return;
            }

            for (int i = 0; i < _slotUsed.Length; i++)
            {
                _slotUsed[i] = false;
            }

            _slotCursor = 0;
        }

        /// <summary>取一个空槽（从上次位置向后找，尽量把内圈先填满）；满则返回 -1。</summary>
        private int AcquireStanceSlot()
        {
            if (_slotUsed == null || _slotUsed.Length == 0)
            {
                return -1;
            }

            for (int k = 0; k < _slotUsed.Length; k++)
            {
                int index = (_slotCursor + k) % _slotUsed.Length;
                if (_slotUsed[index])
                {
                    continue;
                }

                _slotUsed[index] = true;
                _slotCursor = (index + 1) % _slotUsed.Length;
                return index;
            }

            return -1;
        }

        /// <summary>
        /// 释放槽位。用 generation 防旧回合：清场时旧敌人的 OnDestroy 可能在同一帧晚到，
        /// 此时新回合可能已经重新占用同编号槽位，不能误释放别人的槽。
        /// </summary>
        public void ReleaseStanceSlot(int slotIndex, int generation)
        {
            if (generation != _roundGeneration || _slotUsed == null)
            {
                return;
            }

            if (slotIndex >= 0 && slotIndex < _slotUsed.Length)
            {
                _slotUsed[slotIndex] = false;
            }
        }

        /// <summary>内圈停距：玩家半径 + 敌人半径 − 0.08（与旧 EnemyAI 的算法一致，保证接触触发）。</summary>
        private float GetBaseStopRadius()
        {
            float playerRadius = 0.4f;
            if (_playerHealth != null)
            {
                var circle = _playerHealth.GetComponent<CircleCollider2D>();
                if (circle != null)
                {
                    Vector3 scale = circle.transform.lossyScale;
                    playerRadius = circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                }
            }

            float enemyRadius = 0f;
            if (_enemyDefinitions != null)
            {
                for (int i = 0; i < _enemyDefinitions.Length; i++)
                {
                    if (_enemyDefinitions[i] != null)
                    {
                        enemyRadius = Mathf.Max(enemyRadius, _enemyDefinitions[i].ColliderRadius);
                    }
                }
            }

            if (enemyRadius <= 0f)
            {
                enemyRadius = 0.38f;
            }

            return playerRadius + enemyRadius - StopDistancePadding;
        }

        // ---------- 出生点 ----------

        /// <summary>
        /// 出生点：以玩家为中心、半径约 1.15×相机半对角线的随机点，且必须在相机视野外（M4-B0）。
        /// 相机跟随有滞后，只按相机视口算会在相机前进方向的边缘「脸上刷怪」，所以以玩家为圆心。
        /// 允许落在地图外（虚空），敌人走进来即可。
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

                if (!IsInsideView(candidate))
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
