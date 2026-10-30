using UnityEngine;

namespace Survival.Enemy
{
    /// <summary>
    /// 单个敌人类型的静态配置（玩法增强第 4.1 节的接缝）。
    /// M4 先只放一个基础追击定义；生成节奏、上限、扰动等全局参数仍归 <see cref="Survival.Core.GameConfig"/>。
    /// 新增变体时复制资产、换 sprite/速度/伤害即可，不改生成器代码。
    /// </summary>
    [CreateAssetMenu(fileName = "def_Enemy", menuName = "Survival/Enemy Definition")]
    public class EnemyDefinition : ScriptableObject
    {
        [SerializeField] private string _displayName = "追击者";
        [SerializeField] private Sprite _sprite;
        [SerializeField] private float _moveSpeed = 1.8f;
        [SerializeField] private int _contactDamage = 12;
        [SerializeField] private float _colliderRadius = 0.38f;

        public string DisplayName => _displayName;
        public Sprite Sprite => _sprite;
        public float MoveSpeed => _moveSpeed;
        public int ContactDamage => _contactDamage;
        public float ColliderRadius => _colliderRadius;
    }
}
