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
        [Tooltip("敌人之间的最小间距：站位环按它分环（环半径 = 停距 + ring × 间距），分离修正也以它为目标距离。")]
        [SerializeField] private float _enemySpacing = 0.95f;
        [Tooltip("分离修正频率（Hz）。移动/渲染仍每帧；修正是低频强制定量位移，60Hz 会和 seek 打架。")]
        [SerializeField] private float _enemySeparationHz = 20f;
        [Tooltip("分离阻尼：每次只走重叠量的这个比例，避免来回弹（玻璃球感）。")]
        [SerializeField, Range(0.1f, 1f)] private float _enemySeparationDamping = 0.45f;
        [Tooltip("滞回起点（×间距）：小于 0.9×间距才开始推，推到间距为止，防止在边界反复触发。")]
        [SerializeField, Range(0.5f, 1f)] private float _enemySeparationStartRatio = 0.9f;
        [Tooltip("单次修正的最大位移（世界单位）：一次最多挪这么多，防止一格大跳。")]
        [SerializeField] private float _enemySeparationMaxStep = 0.12f;
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

        // 压测覆盖（M6 §3.4）：只有 PerfProbe 在 URL 带 ?perf=1 时写入，0 = 不覆盖。
        // 放运行时字段而不是改资产：同一个构建能用 enemyLimit=20/30/40 跑完，不必为每个上限重建。
        private int _perfEnemyLimitOverride;
        private float _perfRoundDurationOverride;

        public int PlayerMaxHealth => _playerMaxHealth;
        public float PlayerSpeed => _playerSpeed;
        public float InvincibleDuration => _invincibleDuration;

        public float SpawnIntervalStart => _spawnIntervalStart;
        public float SpawnIntervalDecreasePerTenSeconds => _spawnIntervalDecreasePerTenSeconds;
        public float SpawnIntervalMin => _spawnIntervalMin;
        public int EnemyLimit => _perfEnemyLimitOverride > 0 ? _perfEnemyLimitOverride : _enemyLimit;
        public float EnemySpeedJitter => _enemySpeedJitter;
        public float EnemySpeedMax => _enemySpeedMax;
        public float EnemySpacing => _enemySpacing;
        public float EnemySeparationHz => _enemySeparationHz;
        public float EnemySeparationDamping => _enemySeparationDamping;
        public float EnemySeparationStartRatio => _enemySeparationStartRatio;
        public float EnemySeparationMaxStep => _enemySeparationMaxStep;
        public float EnemyRecycleSeconds => _enemyRecycleSeconds;
        public float EnemyPressureMaxMultiplier => _enemyPressureMaxMultiplier;

        public float MapWidth => _mapWidth;
        public float MapHeight => _mapHeight;

        public float RoundDuration =>
            _perfRoundDurationOverride > 0f ? _perfRoundDurationOverride : _roundDuration;

        public int ScorePerSurvivedSecond => _scorePerSurvivedSecond;
        public int ScorePerKill => _scorePerKill;
        public int WinBonus => _winBonus;

        /// <summary>只给压测探针用（M6 §3.4）：覆盖同屏上限，不改资产默认值。</summary>
        public void SetPerfEnemyLimitOverride(int value) => _perfEnemyLimitOverride = Mathf.Max(0, value);

        /// <summary>
        /// 只给压测探针用：覆盖回合时长，让压测能长时间维持同屏上限。
        /// 注意 EnemyAI 的压力曲线也读 RoundDuration，压测时速度增长会被拉平（对同屏成本影响很小）。
        /// </summary>
        public void SetPerfRoundDurationOverride(float value) => _perfRoundDurationOverride = Mathf.Max(0f, value);
    }
}
