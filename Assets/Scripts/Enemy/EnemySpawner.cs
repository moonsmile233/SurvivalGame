using System.Collections.Generic;
using Survival.Core;
using Survival.Player;
using UnityEngine;

namespace Survival.Enemy
{
    /// <summary>
    /// 定时生成敌人、间隔递减、同屏上限、清场。不追踪玩家（规范 5.2）。
    /// 敌人是运行时动态物体，场景里不预摆（CLAUDE.md 硬约束 1）。
    /// </summary>
    public class EnemySpawner : MonoBehaviour
    {
        private const float TickSeconds = 10f;

        [SerializeField] private GameConfig _config;
        [SerializeField] private Transform _playerTarget;
        [SerializeField] private PlayerHealth _playerHealth;
        [SerializeField] private Camera _camera;
        [SerializeField] private Sprite _enemySprite;
        [SerializeField] private float _spawnOutsideMargin = 0.15f;

        private readonly List<EnemyAI> _enemies = new List<EnemyAI>(24);
        private Transform _root;
        private float _cameraDistance;
        private float _elapsed;
        private float _spawnTimer;
        private bool _running;

        public int ActiveCount => _enemies.Count;

        private void Awake()
        {
            _root = new GameObject("Enemies").transform;
            _root.SetParent(transform, false);

            if (_camera == null)
            {
                _camera = Camera.main;
            }

            if (_camera != null)
            {
                _cameraDistance = Mathf.Abs(_camera.transform.position.z);
            }
        }

        private void Update()
        {
            if (!_running || _config == null)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            _spawnTimer += Time.deltaTime;

            if (_spawnTimer < GetCurrentInterval())
            {
                return;
            }

            _spawnTimer = 0f;
            if (_enemies.Count < _config.EnemyLimit)
            {
                SpawnEnemy();
            }
        }

        /// <summary>开始新一局：清场 + 计时归零 + 开始生成。</summary>
        public void BeginRound()
        {
            ClearEnemies();
            _elapsed = 0f;
            _spawnTimer = 0f;
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

        private void SpawnEnemy()
        {
            var go = new GameObject("Enemy");
            go.transform.SetParent(_root, false);
            go.transform.position = GetSpawnPosition();

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = _enemySprite;
            renderer.sortingOrder = 1;

            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;

            var collider = go.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.38f;

            var ai = go.AddComponent<EnemyAI>();
            ai.Initialize(_config, _playerTarget, _playerHealth);

            _enemies.Add(ai);
        }

        /// <summary>每 10 秒递减，不低于下限。</summary>
        private float GetCurrentInterval()
        {
            float steps = Mathf.Floor(_elapsed / TickSeconds);
            float interval = _config.SpawnIntervalStart - steps * _config.SpawnIntervalDecreasePerTenSeconds;
            return Mathf.Max(_config.SpawnIntervalMin, interval);
        }

        /// <summary>相机视野外一圈的随机点（视口 ±0.15）。</summary>
        private Vector3 GetSpawnPosition()
        {
            if (_camera == null)
            {
                return Vector3.zero;
            }

            float margin = _spawnOutsideMargin;
            float side = Random.value * 4f;

            Vector3 viewportPoint;
            if (side < 1f)
            {
                viewportPoint = new Vector3(-margin, Random.value, 0f);
            }
            else if (side < 2f)
            {
                viewportPoint = new Vector3(1f + margin, Random.value, 0f);
            }
            else if (side < 3f)
            {
                viewportPoint = new Vector3(Random.value, -margin, 0f);
            }
            else
            {
                viewportPoint = new Vector3(Random.value, 1f + margin, 0f);
            }

            Vector3 world = _camera.ViewportToWorldPoint(new Vector3(viewportPoint.x, viewportPoint.y, _cameraDistance));
            world.z = 0f;
            return world;
        }
    }
}
