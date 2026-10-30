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
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        private const float TickSeconds = 10f;

        // 出生点相对玩家的半径系数：约 1.15 × 相机半对角线（16:9、ortho 5 时 ≈11.7）。
        private const float SpawnRadiusCameraRatio = 1.15f;

        // 出生点必须落在相机视野外（含该 margin），避免「脸上刷怪」。
        private const float SpawnViewMargin = 0.05f;
        private const int SpawnAttempts = 8;

        // 站位槽位数：新敌人轮流分配，围成玩家的站位环（M4-B1 2.2：8～12 个）。
        private const int StanceSlotCount = 12;

        [SerializeField] private GameConfig _config;
        [SerializeField] private Transform _playerTarget;
        [SerializeField] private PlayerHealth _playerHealth;
        [SerializeField] private Camera _camera;
        [SerializeField] private EnemyDefinition[] _enemyDefinitions;

        private readonly List<EnemyAI> _enemies = new List<EnemyAI>(24);
        private Transform _root;
        private float _elapsed;
        private float _spawnTimer;
        private int _nextStanceSlot;
        private bool _running;

        public int ActiveCount => _enemies.Count;

        /// <summary>本局已进行秒数，供压力曲线使用（M4-B1）。</summary>
        public float RoundElapsed => _elapsed;

        /// <summary>只读列表，供 EnemyAI 做 O(n²) 软分离（玩法增强 2.1）；调用方不得增删。</summary>
        public IReadOnlyList<EnemyAI> ActiveEnemies => _enemies;

        private void Awake()
        {
            _root = new GameObject("Enemies").transform;
            _root.SetParent(transform, false);

            if (_camera == null)
            {
                _camera = Camera.main;
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
                if (_enemies.Count < _config.EnemyLimit)
                {
                    SpawnEnemy();
                }
            }

            // 敌人之间的间距修正放在 Spawner：EnemyAI 只负责走到自己的站位点（ticket 2.2）。
            ApplySpacingCorrection();
        }

        /// <summary>
        /// 两两位置修正：距离小于 EnemySpacing 的一对，各推开一半。
        /// O(n²)，n ≤ 20；1～2 轮足够。只用 struct Vector2，无每帧分配。
        /// 在 timeScale = 0（暂停/结算）时不执行，避免暂停时敌人还在挪动。
        /// </summary>
        private void ApplySpacingCorrection()
        {
            if (_enemies.Count < 2)
            {
                return;
            }

            float minDistance = _config.EnemySpacing;
            float minDistanceSqr = minDistance * minDistance;

            for (int pass = 0; pass < _config.EnemySpacingPasses; pass++)
            {
                for (int i = 0; i < _enemies.Count; i++)
                {
                    EnemyAI first = _enemies[i];
                    if (first == null)
                    {
                        continue;
                    }

                    Vector2 firstPosition = first.transform.position;

                    for (int j = i + 1; j < _enemies.Count; j++)
                    {
                        EnemyAI second = _enemies[j];
                        if (second == null)
                        {
                            continue;
                        }

                        Vector2 secondPosition = second.transform.position;
                        Vector2 delta = secondPosition - firstPosition;
                        float sqrDistance = delta.sqrMagnitude;
                        if (sqrDistance >= minDistanceSqr)
                        {
                            continue;
                        }

                        float distance = Mathf.Sqrt(sqrDistance);
                        Vector2 direction = distance > 0.0001f ? delta / distance : Vector2.right;
                        float correction = (minDistance - distance) * 0.5f;

                        firstPosition -= direction * correction;
                        secondPosition += direction * correction;
                        first.transform.position = firstPosition;
                        second.transform.position = secondPosition;
                    }
                }
            }
        }

        /// <summary>开始新一局：清场 + 计时归零 + 开始生成。</summary>
        public void BeginRound()
        {
            ClearEnemies();
            _elapsed = 0f;
            _spawnTimer = 0f;
            _nextStanceSlot = 0;
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

            int stanceSlot = _nextStanceSlot;
            _nextStanceSlot = (_nextStanceSlot + 1) % StanceSlotCount;

            var ai = go.AddComponent<EnemyAI>();
            ai.Initialize(_config, definition, _playerTarget, _playerHealth, this, _camera, stanceSlot);

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
