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
        [Tooltip("软墙：两只敌人中心距小于该值就停止靠近（允许约 25% 精灵重叠，而不是被推开）。")]
        [SerializeField] private float _enemySoftWallDistance = 0.70f;
        [Tooltip("开始减速的邻居距离：0.90→0.70 之间线性扣掉逼近速度；≥0.90 完全不干预。")]
        [SerializeField] private float _enemySeparationRadius = 0.90f;
        [Tooltip("邻居表刷新频率（Hz）。移动/积分仍逐帧，这里只决定“看得见谁”。")]
        [SerializeField] private float _enemySeparationHz = 30f;
        [Tooltip("速度指数平滑（1/s）：16 ≈ 62ms 时间常数，越小越软、越大越跟手。")]
        [SerializeField] private float _enemyVelocitySmoothing = 16f;
        [Tooltip("追击比例增益（1/s）：离停距的偏差换算成速度，0.2u 内从 0 到满速。")]
        [SerializeField] private float _enemySeekGain = 5f;
        [Tooltip("停距内的让位速度（×当前速度）：玩家压进人群时敌人最多以该比例退开。")]
        [SerializeField] private float _enemyBackOutRatio = 0.6f;
        [Tooltip("被邻居挡住时的切向让位速度上限（u/s）。")]
        [SerializeField] private float _enemySidestepSpeed = 0.35f;
        [Tooltip("被后面的人顶着时，给挡路者让开的侧移速度上限（u/s）：只在前排/被压时生效，不主动绕圈。")]
        [SerializeField] private float _enemyYieldSpeed = 0.2f;
        [Tooltip("左右压力差滞回：差值超过该值才换边，避免每帧翻向。")]
        [SerializeField] private float _enemySideMargin = 0.15f;
        [Tooltip("玩家移动穿过人群时的侧向闪避强度（×玩家接近速度）。")]
        [SerializeField] private float _enemyPlayerDodgeFactor = 0.5f;
        [Tooltip("防重合硬下限：中心距低于它持续外推；软墙 0.70 负责正常停距，40~50 只的密集人群会稳定压到这个下限附近（C5 实测）。")]
        [SerializeField] private float _enemyEmergencyDistance = 0.5f;
        [Tooltip("兜底最大外推速度（u/s）。")]
        [SerializeField] private float _enemyEmergencyStrength = 2.0f;
        [Tooltip("兜底速度的逼近加速度（u/s²）：3.6 → 60fps 下每帧 Δv ≤0.06，保证连续。")]
        [SerializeField] private float _enemyEmergencyAccel = 3.6f;
        [Tooltip("总速度上限 = 该比例 × EnemySpeedMax（1.2 → 2.64，仍低于玩家 3.2）。")]
        [SerializeField] private float _enemyMaxVelocityRatio = 1.2f;
        [Tooltip("离开相机视野（含 0.05 margin）持续 N 秒则回收，释放同屏名额；只对进过视野的敌人生效（M4-B0 / C5 1.5s）。")]
        [SerializeField] private float _enemyRecycleSeconds = 1.5f;
        [Tooltip("压力曲线终值：敌人速度随局内时间从 1.0 倍升到该倍率，作用于已存在的敌人（M4-B1）。")]
        [SerializeField] private float _enemyPressureMaxMultiplier = 1.15f;

        [Header("Spawn / Intercept（M6 C7.5 围堵）")]
        [Tooltip("前向出生偏置概率：约该比例的出生点落在玩家前进方向 ±半角的视野外锥形内；其余照旧随机。")]
        [SerializeField] private float _forwardSpawnChance = 0.35f;
        [Tooltip("前向出生锥的半角（度）。")]
        [SerializeField] private float _forwardSpawnHalfAngleDegrees = 60f;
        [Tooltip("前向出生点距地图边缘的内缩（世界单位）：前向出生必须仍在地图内。")]
        [SerializeField] private float _forwardSpawnMapMargin = 0.5f;
        [Tooltip("Chaser 槽中拦截者（Interceptor）的比例，约一半；不新增敌人类型/颜色。")]
        [SerializeField] private float _interceptorShare = 0.5f;
        [Tooltip("提前点基准距离 = 玩家 + 平滑前进方向 × 该距离（每只再 ±LeadJitter）。")]
        [SerializeField] private float _interceptorLeadDistance = 5f;
        [Tooltip("每只拦截者的提前距离抖动（4.2~5.8 = 5 ±0.8），避免整齐划一。")]
        [SerializeField] private float _interceptorLeadJitter = 0.8f;
        [Tooltip("提前点相对前进轴的侧偏基准；每只再 ±SideJitter（0.4~1.2 = 0.8 ±0.4）。")]
        [SerializeField] private float _interceptorSideOffset = 0.8f;
        [SerializeField] private float _interceptorSideJitter = 0.4f;
        [Tooltip("每只拦截者的前往速度范围（u/s）：显式超速，不受 EnemyMaxVelocity 限制。")]
        [SerializeField] private float _interceptorSpeedMin = 2.8f;
        [SerializeField] private float _interceptorSpeedMax = 3.1f;
        [Tooltip("到提前点附近的最低巡航速度（u/s）：只减速、不急停到 0，并轻微绕点。")]
        [SerializeField] private float _interceptorArriveSpeedMin = 0.5f;
        [SerializeField] private float _interceptorArriveSpeedMax = 0.8f;
        [Tooltip("每只拦截者的最大转向角速度（度/秒），180~240，避免像被遥控一样瞬间掉头。")]
        [SerializeField] private float _interceptorTurnRateMin = 180f;
        [SerializeField] private float _interceptorTurnRateMax = 240f;
        [Tooltip("出生时的反应延迟范围（秒）：第一目标更新前的停顿，每只随机。")]
        [SerializeField] private float _interceptorReactionMin = 0.15f;
        [SerializeField] private float _interceptorReactionMax = 0.3f;
        [Tooltip("提前点更新间隔（秒）：中间保持旧目标，不吃每帧玩家输入。")]
        [SerializeField] private float _interceptorTargetUpdateSeconds = 0.25f;
        [Tooltip("提前量预测时间上限（秒）：lead = min(配置距离, 平滑玩家速度 × 该值)。")]
        [SerializeField] private float _interceptorPredictionMaxSeconds = 1.2f;
        [Tooltip("玩家速度平滑时间常数（秒），约 0.25s 的滞后。")]
        [SerializeField] private float _interceptorVelocitySmoothingSeconds = 0.25f;
        [Tooltip("到提前点该距离内进入 Arrive：速度压到 ArriveSpeed 并轻微绕点，不站定。")]
        [SerializeField] private float _interceptorArriveRadius = 2.5f;

        [Header("World")]
        [Tooltip("地图尺寸（世界单位）：大于视野，相机跟随并钳制在地图内（M4-B0）。")]
        [SerializeField] private float _mapWidth = 36f;
        [SerializeField] private float _mapHeight = 20f;

        [Header("Difficulty")]
        [Tooltip("三档难度配置，顺序固定 Easy / Normal / Hard（SceneSetup 生成并挂上）。缺档位时回落到本资产字段（等价普通档）。")]
        [SerializeField] private DifficultyProfile[] _difficulties = new DifficultyProfile[3];

        [Header("Round")]
        [SerializeField] private float _roundDuration = 60f;

        [Header("Score")]
        // M6 §5（C6）：分段存活分（0.1s 粒度）+ 剩余血量分 + 难度倍率 + 固定胜利奖励。
        // 无击杀、无擦身：kills 只在协议里保留恒 0，不参与任何公式。
        [Tooltip("每个存活分段的秒数：15 → 0-15 / 15-30 / 30-45 / 45-60。")]
        [SerializeField] private float _survivalSegmentSeconds = 15f;
        [Tooltip("每段每秒得分，按段顺序（10/12/15/20 = 满 60 秒 855）。防御旧资产：数组为空时按 10/秒。")]
        [SerializeField] private float[] _survivalRatesPerSecond = { 10f, 12f, 15f, 20f };
        [Tooltip("结算剩余 HP 换算成分的比例；1 → 每点剩余 HP 1 分（结算时才计入，HUD 不显示）。")]
        [SerializeField] private float _scorePerHealthPoint = 1f;
        [Tooltip("胜利奖励，固定值，不乘难度倍率。")]
        [SerializeField] private int _winBonus = 200;

        // 压测覆盖（M6 §3.4）：只有 PerfProbe 在 URL 带 ?perf=1 时写入，0 = 不覆盖。
        // 放运行时字段而不是改资产：同一个构建能用 enemyLimit=20/30/40 跑完，不必为每个上限重建。
        private int _perfEnemyLimitOverride;
        private float _perfRoundDurationOverride;
        // C7.5 压测归因：-1 = 用资产值；0 = 关闭对应行为（只给 ?interceptorShare= / ?forwardSpawn=）。
        private float _perfInterceptorShareOverride = -1f;
        private float _perfForwardSpawnOverride = -1f;

        // 活动难度（M6 §7.2）：同样是运行时字段，**不加 [SerializeField]、不写回资产**。
        // 只在 GameManager.StartGame 时设置一次、局内锁定（RestartGame 沿用）；Editor 试玩不会弄脏 cfg_GameConfig.asset。
        private Difficulty _activeDifficulty = Difficulty.Normal;

        public int PlayerMaxHealth => _playerMaxHealth;
        public float PlayerSpeed => _playerSpeed;
        public float InvincibleDuration => _invincibleDuration;

        public float SpawnIntervalStart => _spawnIntervalStart;
        public float SpawnIntervalDecreasePerTenSeconds => _spawnIntervalDecreasePerTenSeconds;
        public float SpawnIntervalMin => _spawnIntervalMin;
        public int EnemyLimit
        {
            get
            {
                if (_perfEnemyLimitOverride > 0)
                {
                    return _perfEnemyLimitOverride;
                }

                DifficultyProfile profile = ActiveProfile;
                return profile != null ? profile.EnemyLimit : _enemyLimit;
            }
        }
        public float EnemySpeedJitter => _enemySpeedJitter;
        public float EnemySpeedMax => _enemySpeedMax;
        public float EnemySoftWallDistance => _enemySoftWallDistance;
        public float EnemySeparationRadius => _enemySeparationRadius;
        public float EnemySeparationHz => _enemySeparationHz;
        public float EnemyVelocitySmoothing => _enemyVelocitySmoothing;
        public float EnemySeekGain => _enemySeekGain;
        public float EnemyBackOutRatio => _enemyBackOutRatio;
        public float EnemySidestepSpeed => _enemySidestepSpeed;
        public float EnemyYieldSpeed => _enemyYieldSpeed;
        public float EnemySideMargin => _enemySideMargin;
        public float EnemyPlayerDodgeFactor => _enemyPlayerDodgeFactor;
        public float EnemyEmergencyDistance => _enemyEmergencyDistance;
        public float EnemyEmergencyStrength => _enemyEmergencyStrength;
        public float EnemyEmergencyAccel => _enemyEmergencyAccel;
        public float EnemyMaxVelocityRatio => _enemyMaxVelocityRatio;
        public float EnemyMaxVelocity => _enemySpeedMax * _enemyMaxVelocityRatio;
        public float EnemyRecycleSeconds => _enemyRecycleSeconds;

        public float ForwardSpawnChance =>
            _perfForwardSpawnOverride >= 0f ? _perfForwardSpawnOverride : Mathf.Clamp01(_forwardSpawnChance);

        public float ForwardSpawnHalfAngleDegrees => Mathf.Clamp(_forwardSpawnHalfAngleDegrees, 0f, 180f);
        public float ForwardSpawnMapMargin => Mathf.Max(0f, _forwardSpawnMapMargin);
        public float InterceptorShare =>
            _perfInterceptorShareOverride >= 0f ? _perfInterceptorShareOverride : Mathf.Clamp01(_interceptorShare);
        public float InterceptorLeadDistance => Mathf.Max(0.5f, _interceptorLeadDistance);
        public float InterceptorLeadJitter => Mathf.Max(0f, _interceptorLeadJitter);
        public float InterceptorSideOffset => Mathf.Max(0f, _interceptorSideOffset);
        public float InterceptorSideJitter => Mathf.Max(0f, _interceptorSideJitter);
        public float InterceptorSpeedMin => Mathf.Max(0.1f, Mathf.Min(_interceptorSpeedMin, _interceptorSpeedMax));
        public float InterceptorSpeedMax => Mathf.Max(InterceptorSpeedMin, _interceptorSpeedMax);
        public float InterceptorArriveSpeedMin => Mathf.Max(0.1f, Mathf.Min(_interceptorArriveSpeedMin, _interceptorArriveSpeedMax));
        public float InterceptorArriveSpeedMax => Mathf.Max(InterceptorArriveSpeedMin, _interceptorArriveSpeedMax);
        public float InterceptorTurnRateMin => Mathf.Max(1f, Mathf.Min(_interceptorTurnRateMin, _interceptorTurnRateMax));
        public float InterceptorTurnRateMax => Mathf.Max(InterceptorTurnRateMin, _interceptorTurnRateMax);
        public float InterceptorReactionMin => Mathf.Max(0f, Mathf.Min(_interceptorReactionMin, _interceptorReactionMax));
        public float InterceptorReactionMax => Mathf.Max(InterceptorReactionMin, _interceptorReactionMax);
        public float InterceptorTargetUpdateSeconds => Mathf.Max(0.05f, _interceptorTargetUpdateSeconds);
        public float InterceptorPredictionMaxSeconds => Mathf.Max(0.1f, _interceptorPredictionMaxSeconds);
        public float InterceptorVelocitySmoothingSeconds => Mathf.Max(0.01f, _interceptorVelocitySmoothingSeconds);
        public float InterceptorArriveRadius => Mathf.Max(0.1f, _interceptorArriveRadius);

        public float EnemyPressureMaxMultiplier
        {
            get
            {
                DifficultyProfile profile = ActiveProfile;
                return profile != null ? profile.PressureMaxMultiplier : _enemyPressureMaxMultiplier;
            }
        }

        public float MapWidth => _mapWidth;
        public float MapHeight => _mapHeight;

        public float RoundDuration =>
            _perfRoundDurationOverride > 0f ? _perfRoundDurationOverride : _roundDuration;

        public float SurvivalSegmentSeconds => _survivalSegmentSeconds > 0f ? _survivalSegmentSeconds : 15f;
        public float ScorePerHealthPoint => _scorePerHealthPoint;
        public int WinBonus => _winBonus;

        /// <summary>
        /// 第 <paramref name="segment"/> 段（0 起）的每秒存活分。越界时重复最后一段，
        /// 旧资产数组比回合时长短也不会把后段计成 0（ScoreService 在热路径逐帧调用，无分配）。
        /// </summary>
        public float SurvivalRateForSegment(int segment)
        {
            if (_survivalRatesPerSecond == null || _survivalRatesPerSecond.Length == 0)
            {
                return 10f;
            }

            int index = Mathf.Clamp(segment, 0, _survivalRatesPerSecond.Length - 1);
            return _survivalRatesPerSecond[index];
        }

        // ---------- 难度（M6 §7.2） ----------

        /// <summary>
        /// 本局实际采用的难度。只在 Ready → Playing 时由 GameManager 设置一次；
        /// 结算回传（onGameOver.difficulty）也读这里，所以是「Unity 真正用的值」而不是 H5 发来的值。
        /// </summary>
        public Difficulty ActiveDifficulty => _activeDifficulty;

        /// <summary>当前档位的配置；资产缺失/数组没挂时返回 null，各乘区 getter 会回落到本资产字段。</summary>
        public DifficultyProfile ActiveProfile => ResolveProfile(_activeDifficulty);

        /// <summary>按档位取配置（主菜单倍率文案用）；缺失返回 null，调用方回落到 1.0。</summary>
        public DifficultyProfile GetProfile(Difficulty difficulty) => ResolveProfile(difficulty);

        public float EnemySpeedMultiplier => ActiveProfile != null ? ActiveProfile.EnemySpeedMultiplier : 1f;
        public float SpawnIntervalMultiplier => ActiveProfile != null ? ActiveProfile.SpawnIntervalMultiplier : 1f;
        public float DamageMultiplier => ActiveProfile != null ? ActiveProfile.DamageMultiplier : 1f;
        public float ScoreMultiplier => ActiveProfile != null ? ActiveProfile.ScoreMultiplier : 1f;

        /// <summary>只在开局设置（GameManager.StartGame）；局内不可改，RestartGame 沿用。</summary>
        public void SetActiveDifficulty(Difficulty difficulty)
        {
            _activeDifficulty = difficulty;
        }

        private DifficultyProfile ResolveProfile(Difficulty difficulty)
        {
            int index = (int)difficulty;
            if (_difficulties == null || index < 0 || index >= _difficulties.Length)
            {
                return null;
            }

            return _difficulties[index];
        }

        /// <summary>只给压测探针用（M6 §3.4）：覆盖同屏上限，不改资产默认值。</summary>
        public void SetPerfEnemyLimitOverride(int value) => _perfEnemyLimitOverride = Mathf.Max(0, value);

        /// <summary>只给压测探针用（C7.5 归因）：覆盖拦截者比例；0 = 关闭拦截者。</summary>
        public void SetPerfInterceptorShareOverride(float value) => _perfInterceptorShareOverride = Mathf.Clamp01(value);

        /// <summary>只给压测探针用（C7.5 归因）：覆盖前向出生概率；0 = 关闭前向偏置。</summary>
        public void SetPerfForwardSpawnChanceOverride(float value) => _perfForwardSpawnOverride = Mathf.Clamp01(value);

        /// <summary>
        /// 只给压测探针用：覆盖回合时长，让压测能长时间维持同屏上限。
        /// 注意 EnemyAI 的压力曲线也读 RoundDuration，压测时速度增长会被拉平（对同屏成本影响很小）。
        /// </summary>
        public void SetPerfRoundDurationOverride(float value) => _perfRoundDurationOverride = Mathf.Max(0f, value);
    }
}
