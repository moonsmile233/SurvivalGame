using UnityEngine;

namespace Survival.Core
{
    /// <summary>
    /// 单档难度的乘区与上限（M6 §4）。只放数据，不写逻辑。
    /// 资产由 <c>SceneSetup</c> 生成：**已存在时不覆盖**，否则每次重建场景都会把 Inspector 里调好的平衡改回默认。
    /// 冲锋/绕行的解锁时间与权重是 M6 内容扩展的数据；C1 先落字段，C5 才由 Spawner 消费。
    /// </summary>
    [CreateAssetMenu(fileName = "dif_Difficulty", menuName = "Survival/Difficulty Profile")]
    public class DifficultyProfile : ScriptableObject
    {
        [SerializeField] private Difficulty _id = Difficulty.Normal;

        [Header("敌人")]
        [Tooltip("敌人基础速度乘区。生成时算一次，不进 AI 热路径；最终仍受 GameConfig.EnemySpeedMax 上限约束。")]
        [SerializeField] private float _enemySpeedMultiplier = 1f;

        [Tooltip("生成间隔乘区：>1 更慢、<1 更快；乘完再钳到 GameConfig.SpawnIntervalMin。")]
        [SerializeField] private float _spawnIntervalMultiplier = 1f;

        [Tooltip("接触伤害乘区。生成时四舍五入到整数（最小 1），只影响新生成的敌人。")]
        [SerializeField] private float _damageMultiplier = 1f;

        [Tooltip("同屏上限；压测探针的覆盖值优先。")]
        [SerializeField] private int _enemyLimit = 20;

        [Tooltip("压力曲线终点倍率（局内时间 0 → 回合时长 线性升到该值）。")]
        [SerializeField] private float _pressureMaxMultiplier = 1.15f;

        [Header("计分")]
        [Tooltip("存活分乘区；胜利奖励不乘（M6 §5）。")]
        [SerializeField] private float _scoreMultiplier = 1f;

        [Header("敌人解锁与权重（M6 内容扩展）")]
        // 新字段用 -1 哨兵：旧 dif_*.asset 没有这些字段时，SceneSetup 会一次性写入默认值；
        // 已经迁移过的资产不再被覆盖，Inspector 里调过的权重会保留。
        [Tooltip("冲锋者解锁秒数；<0 表示未迁移。")]
        [SerializeField] private float _chargerUnlockSeconds = -1f;

        [Tooltip("绕行者解锁秒数；<0 表示未迁移。")]
        [SerializeField] private float _flankerUnlockSeconds = -1f;

        [Tooltip("早期（只有追击）权重。")]
        [SerializeField] private float _earlyChaserWeight = -1f;

        [Tooltip("中期（追击 + 冲锋）权重。")]
        [SerializeField] private float _midChaserWeight = -1f;
        [SerializeField] private float _midChargerWeight = -1f;

        [Tooltip("晚期（追击 + 冲锋 + 绕行）权重。")]
        [SerializeField] private float _lateChaserWeight = -1f;
        [SerializeField] private float _lateChargerWeight = -1f;
        [SerializeField] private float _lateFlankerWeight = -1f;

        public Difficulty Id => _id;
        public float EnemySpeedMultiplier => _enemySpeedMultiplier;
        public float SpawnIntervalMultiplier => _spawnIntervalMultiplier;
        public float DamageMultiplier => _damageMultiplier;
        public int EnemyLimit => _enemyLimit;
        public float PressureMaxMultiplier => _pressureMaxMultiplier;
        public float ScoreMultiplier => _scoreMultiplier;

        public float ChargerUnlockSeconds => _chargerUnlockSeconds;
        public float FlankerUnlockSeconds => _flankerUnlockSeconds;
        public float EarlyChaserWeight => _earlyChaserWeight;
        public float MidChaserWeight => _midChaserWeight;
        public float MidChargerWeight => _midChargerWeight;
        public float LateChaserWeight => _lateChaserWeight;
        public float LateChargerWeight => _lateChargerWeight;
        public float LateFlankerWeight => _lateFlankerWeight;

        /// <summary>旧资产是否还没写过解锁/权重字段（SceneSetup 一次性迁移用）。</summary>
        public bool NeedsSpawnWeightMigration => _chargerUnlockSeconds < 0f || _earlyChaserWeight < 0f;
    }
}
