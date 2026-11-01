using UnityEngine;

namespace Survival.Core
{
    /// <summary>
    /// 单档难度的乘区与上限（M6 §4）。只放数据，不写逻辑。
    /// 资产由 <c>SceneSetup</c> 生成：**已存在时不覆盖**，否则每次重建场景都会把 Inspector 里调好的平衡改回默认。
    /// 这里只放「本步真正被消费」的字段；冲锋/绕行的解锁时间在第 4/5 步引入对应敌人时再加，避免死字段。
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

        public Difficulty Id => _id;
        public float EnemySpeedMultiplier => _enemySpeedMultiplier;
        public float SpawnIntervalMultiplier => _spawnIntervalMultiplier;
        public float DamageMultiplier => _damageMultiplier;
        public int EnemyLimit => _enemyLimit;
        public float PressureMaxMultiplier => _pressureMaxMultiplier;
        public float ScoreMultiplier => _scoreMultiplier;
    }
}
