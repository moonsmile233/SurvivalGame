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
        // 单个敌人的速度/伤害/外观在 EnemyDefinition 资产里；这里只放全局生成与群体行为参数。
        [SerializeField] private float _spawnIntervalStart = 1.5f;
        [SerializeField] private float _spawnIntervalDecreasePerTenSeconds = 0.15f;
        [SerializeField] private float _spawnIntervalMin = 0.5f;
        [SerializeField] private int _enemyLimit = 20;
        [Tooltip("生成时速度随机扰动比例，±12% → 到达时间有前后梯队（玩法增强 2.2）。")]
        [SerializeField] private float _enemySpeedJitter = 0.12f;
        [Tooltip("扰动后的速度上限；必须明显低于玩家速度，否则无法躲开。")]
        [SerializeField] private float _enemySpeedMax = 2.2f;
        [Tooltip("敌人之间的最小间距；Spawner 每帧做两两位置修正（M4-B1 2.2）。")]
        [SerializeField] private float _enemySpacing = 0.95f;
        [Tooltip("两两位置修正的迭代次数；1～2 次足够，多了会抖。")]
        [SerializeField, Range(1, 4)] private int _enemySpacingPasses = 2;
        [Tooltip("离开相机视野（含 0.05 margin）持续 N 秒则回收，释放 20 只名额（M4-B0）。")]
        [SerializeField] private float _enemyRecycleSeconds = 3f;
        [Tooltip("压力曲线终值：敌人速度随局内时间从 1.0 倍升到该倍率，作用于已存在的敌人（M4-B1）。")]
        [SerializeField] private float _enemyPressureMaxMultiplier = 1.15f;

        [Header("World")]
        [Tooltip("地图尺寸（世界单位）：大于视野，相机跟随并钳制在地图内（M4-B0）。")]
        [SerializeField] private float _mapWidth = 36f;
        [SerializeField] private float _mapHeight = 20f;

        [Header("Round")]
        [SerializeField] private float _roundDuration = 60f;

        [Header("Score")]
        [SerializeField] private int _scorePerSurvivedSecond = 10;
        [SerializeField] private int _scorePerKill = 20;
        [SerializeField] private int _winBonus = 200;

        public int PlayerMaxHealth => _playerMaxHealth;
        public float PlayerSpeed => _playerSpeed;
        public float InvincibleDuration => _invincibleDuration;

        public float SpawnIntervalStart => _spawnIntervalStart;
        public float SpawnIntervalDecreasePerTenSeconds => _spawnIntervalDecreasePerTenSeconds;
        public float SpawnIntervalMin => _spawnIntervalMin;
        public int EnemyLimit => _enemyLimit;
        public float EnemySpeedJitter => _enemySpeedJitter;
        public float EnemySpeedMax => _enemySpeedMax;
        public float EnemySpacing => _enemySpacing;
        public int EnemySpacingPasses => _enemySpacingPasses;
        public float EnemyRecycleSeconds => _enemyRecycleSeconds;
        public float EnemyPressureMaxMultiplier => _enemyPressureMaxMultiplier;

        public float MapWidth => _mapWidth;
        public float MapHeight => _mapHeight;

        public float RoundDuration => _roundDuration;

        public int ScorePerSurvivedSecond => _scorePerSurvivedSecond;
        public int ScorePerKill => _scorePerKill;
        public int WinBonus => _winBonus;
    }
}
