using UnityEngine;

namespace Survival.Enemy
{
    /// <summary>
    /// 单个敌人类型的静态配置（玩法增强第 4.1 节的接缝）。
    /// 现有三类：追击 Chaser / 冲锋 Charger / 绕行 Flanker；全局生成节奏、上限、难度乘区仍归
    /// <see cref="Survival.Core.GameConfig"/> 与 <see cref="Survival.Core.DifficultyProfile"/>。
    ///
    /// 约定：
    /// - 速度/伤害/外观只在这里；EnemyAI 只读，不写按类型的字面量。
    /// - 三类的碰撞半径都保持 0.38（停距/拥挤手感不变）；<c>_visualScale</c> 只影响 Sprite 表现。
    /// - 冲锋的冲刺速度 <c>_chargeSpeed</c> 不受 GameConfig.EnemySpeedMax（2.2）限制，但仍乘难度速度倍率。
    /// </summary>
    [CreateAssetMenu(fileName = "def_Enemy", menuName = "Survival/Enemy Definition")]
    public class EnemyDefinition : ScriptableObject
    {
        [Header("通用")]
        [SerializeField] private EnemyKind _kind = EnemyKind.Chaser;
        [SerializeField] private string _displayName = "追击者";
        [SerializeField] private Sprite _sprite;
        [Tooltip("SpriteRenderer.color；三种敌人靠颜色区分，不新增素材。")]
        [SerializeField] private Color _tint = new Color(1f, 1f, 1f, 1f);
        [Tooltip("仅视觉缩放；碰撞半径/停距不变。")]
        [SerializeField] private float _visualScale = 1f;
        [SerializeField] private float _moveSpeed = 1.8f;
        [SerializeField] private int _contactDamage = 12;
        [SerializeField] private float _colliderRadius = 0.38f;

        [Header("冲锋 Charger（仅 _kind=Charger 使用）")]
        [Tooltip("距离玩家 ≤ 该值进入前摇；必须 ≤ 冲刺速度×冲刺时长，否则会停在射程外打空。")]
        [SerializeField] private float _chargeTriggerDistance = 2.8f;
        [Tooltip("前摇秒数；≥0.4s 才能读。前摇中不锁方向。")]
        [SerializeField] private float _chargeWindupSeconds = 0.45f;
        [Tooltip("冲刺秒数；方向在冲刺开始时锁定。")]
        [SerializeField] private float _chargeDurationSeconds = 0.45f;
        [Tooltip("冲刺速度；不受 EnemySpeedMax 限制，仍乘难度速度倍率。")]
        [SerializeField] private float _chargeSpeed = 5f;
        [Tooltip("冲刺后恢复秒数（停止、恢复颜色/缩放）。")]
        [SerializeField] private float _chargeRecoverSeconds = 0.8f;
        [Tooltip("恢复后的冷却秒数；冷却中只接近，不进入前摇。")]
        [SerializeField] private float _chargeCooldownSeconds = 1.5f;
        [Tooltip("冲刺接触伤害；同一次冲刺最多结算一次。")]
        [SerializeField] private int _chargeDamage = 15;

        [Header("绕行 Flanker（仅 _kind=Flanker 使用）")]
        [Tooltip("侧后方目标点半径。")]
        [SerializeField] private float _flankRadius = 2.4f;
        [Tooltip("目标点相对玩家朝向的侧后方角度（度）。")]
        [SerializeField] private float _flankAngleDegrees = 135f;
        [Tooltip("距玩家 ≤ 该值直接扑。")]
        [SerializeField] private float _flankPounceDistance = 1.2f;
        [Tooltip("侧绕超时秒数；超时转直追，避免无限绕圈。")]
        [SerializeField] private float _flankTimeoutSeconds = 6f;

        public EnemyKind Kind => _kind;
        public string DisplayName => _displayName;
        public Sprite Sprite => _sprite;
        public Color Tint => _tint;
        public float VisualScale => _visualScale;
        public float MoveSpeed => _moveSpeed;
        public int ContactDamage => _contactDamage;
        public float ColliderRadius => _colliderRadius;

        public float ChargeTriggerDistance => _chargeTriggerDistance;
        public float ChargeWindupSeconds => _chargeWindupSeconds;
        public float ChargeDurationSeconds => _chargeDurationSeconds;
        public float ChargeSpeed => _chargeSpeed;
        public float ChargeRecoverSeconds => _chargeRecoverSeconds;
        public float ChargeCooldownSeconds => _chargeCooldownSeconds;
        public int ChargeDamage => _chargeDamage;

        public float FlankRadius => _flankRadius;
        public float FlankAngleDegrees => _flankAngleDegrees;
        public float FlankPounceDistance => _flankPounceDistance;
        public float FlankTimeoutSeconds => _flankTimeoutSeconds;
    }
}
