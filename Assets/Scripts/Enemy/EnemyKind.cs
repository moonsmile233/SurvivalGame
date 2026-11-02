namespace Survival.Enemy
{
    /// <summary>
    /// 敌人行为类型。资产里的 <c>EnemyDefinition._kind</c> 决定 EnemyAI 走哪套近身行为。
    /// 新增类型时必须同步：Spawner 权重/解锁、PerfProbe 的强制类型参数、SceneSetup 的资产创建。
    /// </summary>
    public enum EnemyKind
    {
        Chaser = 0,
        Charger = 1,
        Flanker = 2,
    }
}
