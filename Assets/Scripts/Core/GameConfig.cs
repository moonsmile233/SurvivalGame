using UnityEngine;

namespace Survival.Core
{
    /// <summary>
    /// 所有可调数值的唯一出处。场景/组件只持有引用，不藏魔法数字（规范 5.4）。
    /// 只放数据，不写逻辑。
    /// </summary>
    [CreateAssetMenu(fileName = "cfg_GameConfig", menuName = "Survival/Game Config")]
    public class GameConfig : ScriptableObject
    {
        [Header("Player")]
        [SerializeField] private int _playerMaxHealth = 100;
        [SerializeField] private float _playerSpeed = 3.2f;
        [SerializeField] private float _invincibleDuration = 1f;

        [Header("Enemy")]
        [SerializeField] private float _enemySpeed = 1.8f;
        [SerializeField] private int _enemyContactDamage = 12;
        [SerializeField] private float _spawnIntervalStart = 1.5f;
        [SerializeField] private float _spawnIntervalDecreasePerTenSeconds = 0.15f;
        [SerializeField] private float _spawnIntervalMin = 0.5f;
        [SerializeField] private int _enemyLimit = 20;

        [Header("Round")]
        [SerializeField] private float _roundDuration = 60f;

        [Header("Score")]
        [SerializeField] private int _scorePerSurvivedSecond = 10;
        [SerializeField] private int _scorePerKill = 20;
        [SerializeField] private int _winBonus = 200;

        public int PlayerMaxHealth => _playerMaxHealth;
        public float PlayerSpeed => _playerSpeed;
        public float InvincibleDuration => _invincibleDuration;

        public float EnemySpeed => _enemySpeed;
        public int EnemyContactDamage => _enemyContactDamage;
        public float SpawnIntervalStart => _spawnIntervalStart;
        public float SpawnIntervalDecreasePerTenSeconds => _spawnIntervalDecreasePerTenSeconds;
        public float SpawnIntervalMin => _spawnIntervalMin;
        public int EnemyLimit => _enemyLimit;

        public float RoundDuration => _roundDuration;

        public int ScorePerSurvivedSecond => _scorePerSurvivedSecond;
        public int ScorePerKill => _scorePerKill;
        public int WinBonus => _winBonus;
    }
}
