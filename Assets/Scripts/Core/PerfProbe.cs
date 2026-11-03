using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Survival.Enemy;
using Survival.Player;
using UnityEngine;

namespace Survival.Core
{
    /// <summary>
    /// 压测探针（M6 §3.4），只在页面 URL 带 <c>?perf=1</c> 时创建，普通游玩完全不创建、不参与。
    ///
    /// 支持参数（全部可选）：
    /// - <c>enemyLimit=N</c>：覆盖同屏上限（压测 20/30/40 用；不用为每个上限重新构建）
    /// - <c>roundDuration=秒</c>：覆盖回合时长（默认 60 会提前结算，压测要长时间维持上限）
    /// - <c>invincible=1</c>：玩家不受伤（否则站着不动约 20 秒就死，到不了高上限稳态）
    /// - <c>perfSeconds=秒</c>：开始后多久打一行 [PERF-SUMMARY]（默认 90）
    /// - <c>hud=1</c>：在屏幕上画调试条（真机看不到 console 时用；桌面压测不要开，避免 IMGUI 扰动采样）
    /// - <c>autostart=1</c>：跳过 H5 点击，直接进 Playing（真机一键跑）
    /// - <c>enemyKind=chaser|charger|flanker</c>：强制只生成该类型（C3/C4 单类型探针；正常游玩不生效）
    ///
    /// 输出：每 2 秒一行 <c>[PERF] {json}</c>，结束时一行 <c>[PERF-SUMMARY] {json}</c>，都进浏览器 console，
    /// 由 tools/e2e/perf.mjs 收集；真机看屏幕上的 hud。
    ///
    /// 为什么用 URL 参数而不是改 GameConfig 资产：同一个构建能一次跑完 20/30/40，
    /// 避免“到底测的是哪一版”的混淆；普通玩家不会带这些参数，逻辑与数值不变。
    ///
    /// 采样口径：只在 Playing 时累计；帧时间用 unscaledDeltaTime（暂停/结算不计）；
    /// 分配量用 GC.GetTotalMemory 的正增量（负增量是回收，不计），WASM 内存直接读 HEAPU8。
    /// </summary>
    public class PerfProbe : MonoBehaviour
    {
        private const int MaxSamples = 16384; // 165 FPS 下约 99 秒；够一次 90 秒压测
        private const float WindowSeconds = 2f;
        private const float SteadyWindowSeconds = 20f;

        private readonly float[] _ringDt = new float[MaxSamples];
        private readonly float[] _ringTime = new float[MaxSamples];
        private readonly int[] _ringAlive = new int[MaxSamples];
        private readonly int[] _ringAlloc = new int[MaxSamples];
        private readonly float[] _ringMotion = new float[MaxSamples];
        private readonly int[] _ringSettled = new int[MaxSamples];
        private readonly float[] _ringMinPair = new float[MaxSamples];
        private readonly float[] _scratch = new float[MaxSamples];
        private readonly StringBuilder _line = new StringBuilder(384);

        // 拥挤度指标（M6 拥挤修复验收）：每帧累加所有敌人的位移，换算成"每只敌人每秒移动多少世界单位"。
        // 新口径下敌群稳态应**持续有小幅挤动**（motionUps > 0），0 不再是目标；1 单位 = 32px。
        private readonly Vector2[] _lastPositions = new Vector2[128];
        private int _trackedCount = -1;
        private float _frameMotion;
        private int _frameSettled;
        private float _windowUnsettledMax;
        private float _windowPlayerPeakUps;

        // 10 秒挤动窗口（[PERF-CROWD]）：分 blocked / unblocked 两组报告，避免“整群恒定侧移”冒充挤动。
        private const float CrowdWindowSeconds = 10f;
        private const int CrowdCapacity = 128;
        private const float CrowdSwapInterval = 0.1f;
        private readonly Vector2[] _crowdStartPositions = new Vector2[CrowdCapacity];
        private readonly Vector2[] _crowdLastPositions = new Vector2[CrowdCapacity];
        private readonly float[] _crowdLateralPath = new float[CrowdCapacity];
        private readonly bool[] _crowdBlocked = new bool[CrowdCapacity];
        private readonly bool[] _crowdYielding = new bool[CrowdCapacity];
        private readonly sbyte[] _crowdOrderSign = new sbyte[CrowdCapacity * CrowdCapacity];
        private int _crowdTrackedCount = -1;
        private float _crowdElapsed;
        private float _crowdMaxFrameDisp;
        private float _crowdPlayerPeakUps;
        private float _crowdMinPair = float.MaxValue;
        private Vector2 _crowdLastPlayer;
        private int _crowdSwapCount;
        private float _crowdSwapTimer;

        private int _ringHead;
        private int _ringCount;

        // 当前窗口统计（Collect 填）
        private long _collectAliveSum;
        private long _collectAlloc;
        private float _collectMotion;
        private int _collectSettled;
        private int _collectCount;
        private float _collectMinPair;

        private GameManager _manager;
        private EnemySpawner _spawner;
        private PlayerHealth _health;

        private int _limitOverride;
        private float _roundOverride;
        private EnemyKind? _forcedKind;
        private bool _invincible;
        private bool _drawHud;
        private bool _autoStart;
        private float _perfSeconds = 90f;

        private float _elapsed;
        private float _autoStartCountdown = 1f;
        private bool _autoStarted;
        private int _lastAlive;

        private long _heapPrev;
        private long _allocTotal;
        private int _peakAlive;
        private float _capReachedAt = -1f;
        private float _nextWindowAt = WindowSeconds;
        private int _activeLimit;
        private bool _summaryLogged;
        private bool _collectionSupported = true;

        private GUIStyle _hudStyle;
        private string _hudText = string.Empty;
        private float _hudRefreshAt;

        // ---------- 启动 ----------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            Dictionary<string, string> query = ParseQuery(Application.absoluteURL);
            if (!query.TryGetValue("perf", out string value) || value != "1")
            {
                return;
            }

            var host = new GameObject("PerfProbe");
            DontDestroyOnLoad(host);
            host.AddComponent<PerfProbe>().ApplyQuery(query);
        }

        private void ApplyQuery(Dictionary<string, string> query)
        {
            _limitOverride = ParseInt(query, "enemyLimit");
            _roundOverride = ParseFloat(query, "roundDuration");
            _invincible = ParseInt(query, "invincible") == 1;
            _drawHud = ParseInt(query, "hud") == 1;
            _autoStart = ParseInt(query, "autostart") == 1;
            _forcedKind = ParseEnemyKind(query);
            float seconds = ParseFloat(query, "perfSeconds");
            if (seconds > 0f)
            {
                _perfSeconds = seconds;
            }
        }

        private void Start()
        {
            _manager = FindFirstObjectByType<GameManager>();
            _spawner = FindFirstObjectByType<EnemySpawner>();
            _health = FindFirstObjectByType<PlayerHealth>();

            if (_manager == null || _manager.Config == null)
            {
                Debug.LogError("[PERF] GameManager / GameConfig 未找到，压测探针停用。");
                enabled = false;
                return;
            }

            GameConfig config = _manager.Config;
            if (_limitOverride > 0)
            {
                config.SetPerfEnemyLimitOverride(_limitOverride);
            }

            if (_roundOverride > 0f)
            {
                config.SetPerfRoundDurationOverride(_roundOverride);
            }

            if (_health != null)
            {
                _health.SetPerfInvincible(_invincible);
            }

            if (_spawner != null && _forcedKind.HasValue)
            {
                _spawner.SetPerfForcedKind(_forcedKind.Value);
                // 单类型视觉探针（enemyLimit=1）才打状态日志；40 只压测不打，避免日志分配污染采样。
                if (_forcedKind.Value == EnemyKind.Charger && _limitOverride > 0 && _limitOverride <= 2)
                {
                    EnemyAI.SetLogChargerStates(true);
                }

                if (_forcedKind.Value == EnemyKind.Flanker && _limitOverride > 0 && _limitOverride <= 2)
                {
                    EnemyAI.SetLogFlankerStates(true);
                }

                Debug.Log($"[PERF] forced enemy kind = {_forcedKind.Value}");
            }

            _activeLimit = config.EnemyLimit;
            _heapPrev = GC.GetTotalMemory(false);
            Debug.Log("[PERF] probe start " + BuildEnvironmentJson());
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // 延时 1 秒再自动开局：等 WebGLBridge.Start 订阅状态，避免 H5 收不到 Playing。
            if (_autoStart && !_autoStarted)
            {
                _autoStartCountdown -= dt;
                if (_autoStartCountdown <= 0f)
                {
                    _autoStarted = true;
                    if (_manager != null && _manager.State == GameState.Menu)
                    {
                        // 压测口径：绕过 3-2-1，直接 Playing（策划裁决 2026-11-01 B）。
                        _manager.StartGameImmediate(Difficulty.Normal);
                        Debug.Log("[PERF] autostart -> Playing (immediate)");
                    }
                }
            }

            if (_manager == null || _manager.State != GameState.Playing || dt <= 0f)
            {
                return;
            }

            _elapsed += dt;
            _lastAlive = _spawner != null ? _spawner.ActiveCount : 0;
            if (_lastAlive > _peakAlive)
            {
                _peakAlive = _lastAlive;
            }

            if (_capReachedAt < 0f && _activeLimit > 0 && _lastAlive >= _activeLimit)
            {
                _capReachedAt = _elapsed;
                Debug.Log($"[PERF] cap reached limit={_activeLimit} at={F(_capReachedAt, 2)}s");
            }

            long heap = GC.GetTotalMemory(false);
            long delta = heap - _heapPrev;
            _heapPrev = heap;

            int alloc = 0;
            if (delta > 0)
            {
                _allocTotal += delta;
                alloc = delta > int.MaxValue ? int.MaxValue : (int)delta;
            }

            TrackEnemyMotion(dt);
            TrackCrowdMetrics(dt);
            Push(dt, _lastAlive, alloc, _frameMotion, _frameSettled);

            if (_elapsed >= _nextWindowAt)
            {
                _nextWindowAt += WindowSeconds;
                LogWindow();
            }

            if (!_summaryLogged && _elapsed >= _perfSeconds)
            {
                _summaryLogged = true;
                LogSummary();
            }
        }

        // ---------- 采样 ----------

        private void Push(float dt, int alive, int alloc, float motion, int settled)
        {
            _ringDt[_ringHead] = dt;
            _ringTime[_ringHead] = _elapsed;
            _ringAlive[_ringHead] = alive;
            _ringAlloc[_ringHead] = alloc;
            _ringMotion[_ringHead] = motion;
            _ringSettled[_ringHead] = settled;
            _ringMinPair[_ringHead] = _spawner != null ? _spawner.MinPairDistance : 0f;
            _ringHead = (_ringHead + 1) % MaxSamples;
            if (_ringCount < MaxSamples)
            {
                _ringCount++;
            }
        }

        /// <summary>
        /// 每帧统计敌人位移总量与"已站定"数量。生成/回收导致数量变化的帧直接重置基准，
        /// 避免索引错位把别人的位移算到自己头上。
        /// </summary>
        private void TrackEnemyMotion(float dt)
        {
            _frameMotion = 0f;
            _frameSettled = 0;

            if (_spawner == null || dt <= 0f)
            {
                return;
            }

            IReadOnlyList<EnemyAI> enemies = _spawner.ActiveEnemies;
            int count = enemies.Count;
            int tracked = Mathf.Min(count, _lastPositions.Length);

            if (count != _trackedCount)
            {
                _trackedCount = count;
                for (int i = 0; i < tracked; i++)
                {
                    _lastPositions[i] = enemies[i] != null ? (Vector2)enemies[i].transform.position : Vector2.zero;
                }

                return;
            }

            for (int i = 0; i < tracked; i++)
            {
                EnemyAI enemy = enemies[i];
                if (enemy == null)
                {
                    continue;
                }

                Vector2 position = enemy.transform.position;
                _frameMotion += (position - _lastPositions[i]).magnitude;
                _lastPositions[i] = position;
                if (enemy.IsAtFront)
                {
                    _frameSettled++;
                }
                else if (enemy.ExcessDistance > _windowUnsettledMax)
                {
                    _windowUnsettledMax = enemy.ExcessDistance;
                }
            }
        }

        /// <summary>
        /// 10 秒挤动窗口：分 blocked / unblocked 两组统计横向位移，另记换位次数、单帧位移（归一化到 1/60s）、
        /// 全场最小中心距、玩家实际速度峰值。只读敌人位置/BlockW，不参与玩法。
        /// </summary>
        private void TrackCrowdMetrics(float dt)
        {
            if (_spawner == null || _health == null || dt <= 0f)
            {
                return;
            }

            IReadOnlyList<EnemyAI> enemies = _spawner.ActiveEnemies;
            int count = Mathf.Min(enemies.Count, CrowdCapacity);
            Vector2 playerPosition = _health.transform.position;

            if (count != _crowdTrackedCount)
            {
                ResetCrowdWindow(enemies, count, playerPosition);
                return;
            }

            for (int i = 0; i < count; i++)
            {
                EnemyAI enemy = enemies[i];
                if (enemy == null)
                {
                    continue;
                }

                Vector2 position = enemy.transform.position;
                Vector2 delta = position - _crowdLastPositions[i];
                float displacement = delta.magnitude;

                // 归一化到 60fps 的单帧位移：30fps 或卡顿帧不会被误判成“瞬移”。
                float normalized = displacement * (1f / 60f) / dt;
                if (normalized > _crowdMaxFrameDisp)
                {
                    _crowdMaxFrameDisp = normalized;
                }

                Vector2 toPlayer = playerPosition - position;
                float distance = toPlayer.magnitude;
                if (distance > 0.0001f)
                {
                    Vector2 perpendicular = new Vector2(-toPlayer.y, toPlayer.x) / distance;
                    _crowdLateralPath[i] += Mathf.Abs(Vector2.Dot(delta, perpendicular));
                }

                if (enemy.BlockW > 0.05f)
                {
                    _crowdBlocked[i] = true;
                }

                if (enemy.BehindBlock > 0.05f && enemy.FrontBlock <= 0.05f)
                {
                    _crowdYielding[i] = true;
                }

                _crowdLastPositions[i] = position;
            }

            float playerSpeed = (playerPosition - _crowdLastPlayer).magnitude / dt;
            if (playerSpeed > _crowdPlayerPeakUps)
            {
                _crowdPlayerPeakUps = playerSpeed;
            }

            if (playerSpeed > _windowPlayerPeakUps)
            {
                _windowPlayerPeakUps = playerSpeed;
            }

            _crowdLastPlayer = playerPosition;

            float minPair = _spawner.MinPairDistance;
            if (minPair < _crowdMinPair)
            {
                _crowdMinPair = minPair;
            }

            _crowdSwapTimer += dt;
            if (_crowdSwapTimer >= CrowdSwapInterval)
            {
                _crowdSwapTimer = 0f;
                CrowdSwapTick(enemies, count, playerPosition);
            }

            _crowdElapsed += dt;
            if (_crowdElapsed >= CrowdWindowSeconds)
            {
                LogCrowdWindow(enemies, count, playerPosition);
                ResetCrowdWindow(enemies, count, playerPosition);
            }
        }

        private void ResetCrowdWindow(IReadOnlyList<EnemyAI> enemies, int count, Vector2 playerPosition)
        {
            _crowdTrackedCount = count;
            _crowdElapsed = 0f;
            _crowdMaxFrameDisp = 0f;
            _crowdPlayerPeakUps = 0f;
            _crowdMinPair = float.MaxValue;
            _crowdSwapCount = 0;
            _crowdSwapTimer = 0f;
            _crowdLastPlayer = playerPosition;

            for (int i = 0; i < count; i++)
            {
                EnemyAI enemy = enemies[i];
                Vector2 position = enemy != null ? (Vector2)enemy.transform.position : playerPosition;
                _crowdStartPositions[i] = position;
                _crowdLastPositions[i] = position;
                _crowdLateralPath[i] = 0f;
                _crowdBlocked[i] = false;
                _crowdYielding[i] = false;
            }

            Array.Clear(_crowdOrderSign, 0, _crowdOrderSign.Length);
        }

        /// <summary>输出 10 秒挤动数据；blocked 组看累计横向位移，unblocked 组看净位移（防止整群巡游冒充挤动）。</summary>
        private void LogCrowdWindow(IReadOnlyList<EnemyAI> enemies, int count, Vector2 playerPosition)
        {
            if (count <= 0)
            {
                return;
            }

            int blockedCount = 0;
            int blockedOver03 = 0;
            int unblockedCount = 0;
            int yieldCount = 0;
            float unblockedNetMax = 0f;
            float unblockedPathMax = 0f;
            float yieldNetMax = 0f;

            for (int i = 0; i < count; i++)
            {
                EnemyAI enemy = enemies[i];
                if (enemy == null)
                {
                    continue;
                }

                float path = _crowdLateralPath[i];
                Vector2 position = enemy.transform.position;
                float net = (position - _crowdStartPositions[i]).magnitude;

                if (_crowdBlocked[i])
                {
                    blockedCount++;
                    if (path >= 0.3f)
                    {
                        blockedOver03++;
                    }
                }
                else
                {
                    unblockedCount++;
                    if (net > unblockedNetMax)
                    {
                        unblockedNetMax = net;
                    }

                    if (path > unblockedPathMax)
                    {
                        unblockedPathMax = path;
                    }
                }

                if (_crowdYielding[i])
                {
                    yieldCount++;
                    if (net > yieldNetMax)
                    {
                        yieldNetMax = net;
                    }
                }
            }

            float minPair = _crowdMinPair < float.MaxValue ? _crowdMinPair : 0f;
            float blockedRatio = blockedCount > 0 ? (float)blockedOver03 / blockedCount : 0f;

            _line.Clear();
            _line.Append("[PERF-CROWD] {")
                .Append("\"t\":").Append(F(_elapsed, 1))
                .Append(",\"alive\":").Append(count)
                .Append(",\"blockedN\":").Append(blockedCount)
                .Append(",\"blockedOver03\":").Append(blockedOver03)
                .Append(",\"blockedRatio\":").Append(F(blockedRatio, 2))
                .Append(",\"unblockedN\":").Append(unblockedCount)
                .Append(",\"unblockedNetMax\":").Append(F(unblockedNetMax, 3))
                .Append(",\"unblockedPathMax\":").Append(F(unblockedPathMax, 3))
                .Append(",\"yieldN\":").Append(yieldCount)
                .Append(",\"yieldNetMax\":").Append(F(yieldNetMax, 3))
                .Append(",\"maxFrameDisp\":").Append(F(_crowdMaxFrameDisp, 3))
                .Append(",\"minPairDist\":").Append(F(minPair, 3))
                .Append(",\"swaps\":").Append(_crowdSwapCount)
                .Append(",\"playerPeakUps\":").Append(F(_crowdPlayerPeakUps, 2))
                .Append(",\"playerX\":").Append(F(playerPosition.x, 1))
                .Append(",\"playerY\":").Append(F(playerPosition.y, 1))
                .Append('}');
            Debug.Log(_line.ToString());
        }

        /// <summary>
        /// 换位计数：两只都在人群半径内时，绕玩家的角度顺序翻转一次算一次（“后方绕过前方”的代理指标）。
        /// 10Hz 采样，O(n²) 只在调试探针里跑。
        /// </summary>
        private void CrowdSwapTick(IReadOnlyList<EnemyAI> enemies, int count, Vector2 playerPosition)
        {
            const float crowdRadius = 3f;
            float crowdSqr = crowdRadius * crowdRadius;

            for (int i = 0; i < count; i++)
            {
                EnemyAI first = enemies[i];
                if (first == null)
                {
                    continue;
                }

                Vector2 a = (Vector2)first.transform.position - playerPosition;
                if (a.sqrMagnitude > crowdSqr)
                {
                    continue;
                }

                int row = i * CrowdCapacity;
                for (int j = i + 1; j < count; j++)
                {
                    EnemyAI second = enemies[j];
                    if (second == null)
                    {
                        continue;
                    }

                    Vector2 b = (Vector2)second.transform.position - playerPosition;
                    if (b.sqrMagnitude > crowdSqr)
                    {
                        _crowdOrderSign[row + j] = 0;
                        continue;
                    }

                    float cross = a.x * b.y - a.y * b.x;
                    sbyte sign = cross > 0f ? (sbyte)1 : (cross < 0f ? (sbyte)-1 : (sbyte)0);
                    sbyte previous = _crowdOrderSign[row + j];
                    if (previous != 0 && sign != 0 && previous != sign)
                    {
                        _crowdSwapCount++;
                    }

                    _crowdOrderSign[row + j] = sign;
                }
            }
        }

        /// <summary>
        /// 把 ring 中 t ∈ [from, to] 的帧时间拷到 _scratch（供排序求分位），
        /// 同时统计样本数、敌人数总和、分配字节数。返回样本数。
        /// </summary>
        private int Collect(float from, float to)
        {
            int count = 0;
            long aliveSum = 0;
            long alloc = 0;
            float motionSum = 0f;
            int settledSum = 0;
            float minPair = float.MaxValue;

            for (int k = 0; k < _ringCount; k++)
            {
                int index = (_ringHead - _ringCount + k + MaxSamples) % MaxSamples;
                float t = _ringTime[index];
                if (t < from || t > to)
                {
                    continue;
                }

                _scratch[count++] = _ringDt[index];
                aliveSum += _ringAlive[index];
                alloc += _ringAlloc[index];
                motionSum += _ringMotion[index];
                settledSum += _ringSettled[index];
                float pair = _ringMinPair[index];
                if (pair > 0f && pair < minPair)
                {
                    minPair = pair;
                }
            }

            _collectCount = count;
            _collectAliveSum = aliveSum;
            _collectAlloc = alloc;
            _collectMotion = motionSum;
            _collectSettled = settledSum;
            _collectMinPair = minPair < float.MaxValue ? minPair : 0f;
            return count;
        }

        private static float PercentileSorted(float[] sorted, int count, float percentile)
        {
            if (count <= 0)
            {
                return 0f;
            }

            int index = Mathf.Clamp(Mathf.CeilToInt(percentile * count) - 1, 0, count - 1);
            return sorted[index];
        }

        private static float SumSeconds(float[] samples, int count)
        {
            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                sum += samples[i];
            }

            return sum;
        }

        private void LogWindow()
        {
            int count = Collect(_elapsed - WindowSeconds, _elapsed);
            if (count < 2)
            {
                _windowUnsettledMax = 0f;
                _windowPlayerPeakUps = 0f;
                return;
            }

            float seconds = SumSeconds(_scratch, count);
            float avgAlive = (float)_collectAliveSum / count;
            float motionUps = avgAlive > 0f && seconds > 0f ? _collectMotion / (seconds * avgAlive) : 0f;
            float settledAvg = (float)_collectSettled / count;
            Array.Sort(_scratch, 0, count);

            _line.Clear();
            _line.Append("[PERF] {\"t\":").Append(F(_elapsed, 2))
                .Append(",\"alive\":").Append(_lastAlive)
                .Append(",\"avgAlive\":").Append(F(avgAlive, 1))
                .Append(",\"settled\":").Append(F(settledAvg, 1))
                .Append(",\"motionUps\":").Append(F(motionUps, 3))
                .Append(",\"unsettledMax\":").Append(F(_windowUnsettledMax, 2))
                .Append(",\"minPairDist\":").Append(F(_collectMinPair, 3))
                .Append(",\"playerUps\":").Append(F(_windowPlayerPeakUps, 2))
                .Append(",\"fps\":").Append(F(seconds > 0f ? count / seconds : 0f, 1))
                .Append(",\"p95ms\":").Append(F(PercentileSorted(_scratch, count, 0.95f) * 1000f, 2))
                .Append(",\"p99ms\":").Append(F(PercentileSorted(_scratch, count, 0.99f) * 1000f, 2))
                .Append(",\"maxms\":").Append(F(_scratch[count - 1] * 1000f, 2))
                .Append(",\"allocKBps\":").Append(F(seconds > 0f ? _collectAlloc / 1024f / seconds : 0f, 1))
                .Append(",\"heapKB\":").Append(GC.GetTotalMemory(false) / 1024)
                .Append(",\"wasmMB\":").Append(F(GetWasmHeapBytes() / 1048576f, 1))
                .Append(",\"gc\":").Append(SafeCollectionCount())
                .Append('}');
            Debug.Log(_line.ToString());
            _windowUnsettledMax = 0f;
            _windowPlayerPeakUps = 0f;
        }

        /// <summary>
        /// 稳态（最后 20 秒，且只取到顶之后）+ 填充期（开局到到顶）各一组数字。
        /// 到顶前就结束的局（上限没摸到）会标 capReached=false，避免把填充期当稳态看。
        /// </summary>
        private void LogSummary()
        {
            float steadyFrom = Mathf.Max(_capReachedAt >= 0f ? _capReachedAt : 0f, _elapsed - SteadyWindowSeconds);
            int steadyCount = Collect(steadyFrom, _elapsed);
            float steadySeconds = SumSeconds(_scratch, steadyCount);
            Array.Sort(_scratch, 0, steadyCount);
            float steadyFps = steadySeconds > 0f ? steadyCount / steadySeconds : 0f;
            float steadyP95 = PercentileSorted(_scratch, steadyCount, 0.95f) * 1000f;
            float steadyP99 = PercentileSorted(_scratch, steadyCount, 0.99f) * 1000f;
            float steadyMax = steadyCount > 0 ? _scratch[steadyCount - 1] * 1000f : 0f;
            float steadyAlloc = steadySeconds > 0f ? _collectAlloc / 1024f / steadySeconds : 0f;
            float steadyAlive = steadyCount > 0 ? (float)_collectAliveSum / steadyCount : 0f;
            // 拥挤度：必须先算（下面的填充期 Collect 会覆盖 _collectMotion/_collectSettled）。
            float steadyMotion = steadyAlive > 0f && steadySeconds > 0f
                ? _collectMotion / (steadySeconds * steadyAlive)
                : 0f;
            float steadySettled = steadyCount > 0 ? (float)_collectSettled / steadyCount : 0f;
            float steadyMinPair = _collectMinPair;

            float fillSeconds = 0f;
            float fillFps = 0f;
            long fillAlloc = 0;
            if (_capReachedAt > 0f)
            {
                int fillCount = Collect(0f, _capReachedAt);
                fillSeconds = SumSeconds(_scratch, fillCount);
                fillFps = fillSeconds > 0f ? fillCount / fillSeconds : 0f;
                fillAlloc = _collectAlloc;
            }

            _line.Clear();
            _line.Append("[PERF-SUMMARY] {")
                .Append("\"limit\":").Append(_activeLimit)
                .Append(",\"t\":").Append(F(_elapsed, 2))
                .Append(",\"peakAlive\":").Append(_peakAlive)
                .Append(",\"capReached\":").Append(_capReachedAt >= 0f ? "true" : "false")
                .Append(",\"capAt\":").Append(F(_capReachedAt, 2))
                .Append(",\"fillSec\":").Append(F(fillSeconds, 2))
                .Append(",\"fillFps\":").Append(F(fillFps, 1))
                .Append(",\"fillAllocMB\":").Append(F(fillAlloc / 1048576f, 2))
                .Append(",\"steadySec\":").Append(F(steadySeconds, 2))
                .Append(",\"steadyAvgAlive\":").Append(F(steadyAlive, 1))
                .Append(",\"steadyFps\":").Append(F(steadyFps, 1))
                .Append(",\"steadyP95ms\":").Append(F(steadyP95, 2))
                .Append(",\"steadyP99ms\":").Append(F(steadyP99, 2))
                .Append(",\"steadyMaxMs\":").Append(F(steadyMax, 2))
                .Append(",\"steadyAllocKBps\":").Append(F(steadyAlloc, 1))
                .Append(",\"steadyMotionUps\":").Append(F(steadyMotion, 3))
                .Append(",\"steadySettled\":").Append(F(steadySettled, 1))
                .Append(",\"steadyMinPairDist\":").Append(F(steadyMinPair, 3))
                .Append(",\"totalAllocMB\":").Append(F(_allocTotal / 1048576f, 2))
                .Append(",\"heapKB\":").Append(GC.GetTotalMemory(false) / 1024)
                .Append(",\"wasmMB\":").Append(F(GetWasmHeapBytes() / 1048576f, 1))
                .Append(",\"gc\":").Append(SafeCollectionCount());
            AppendEnvironmentFields(_line);
            _line.Append('}');
            Debug.Log(_line.ToString());
        }

        private string BuildEnvironmentJson()
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            AppendEnvironmentFields(sb);
            sb.Append('}');
            return sb.ToString();
        }

        private void AppendEnvironmentFields(StringBuilder sb)
        {
            sb.Append(",\"screen\":\"").Append(Screen.width).Append('x').Append(Screen.height).Append('"')
                .Append(",\"dpi\":").Append(F(Screen.dpi, 0))
                .Append(",\"gpu\":\"").Append(Escape(SystemInfo.graphicsDeviceName)).Append('"')
                .Append(",\"cpu\":\"").Append(Escape(SystemInfo.processorType)).Append('"')
                .Append(",\"device\":\"").Append(Escape(SystemInfo.deviceModel)).Append('"')
                .Append(",\"os\":\"").Append(Escape(SystemInfo.operatingSystem)).Append('"')
                .Append(",\"invincible\":").Append(_invincible ? "true" : "false")
                .Append(",\"roundDuration\":").Append(F(_manager != null && _manager.Config != null ? _manager.Config.RoundDuration : 0f, 0))
                .Append(",\"perfSeconds\":").Append(F(_perfSeconds, 0))
                .Append(",\"autostart\":").Append(_autoStart ? "true" : "false");
        }

        // ---------- HUD（真机） ----------

        private void OnGUI()
        {
            if (!_drawHud)
            {
                return;
            }

            if (Time.unscaledTime >= _hudRefreshAt)
            {
                _hudRefreshAt = Time.unscaledTime + 0.25f;
                _hudText = BuildHudText();
            }

            if (_hudStyle == null)
            {
                float scale = Mathf.Max(1f, Screen.height / 720f);
                _hudStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.RoundToInt(18f * scale),
                    fontStyle = FontStyle.Bold,
                };
                _hudStyle.normal.textColor = Color.white;
            }

            float hudScale = Mathf.Max(1f, Screen.height / 720f);
            var box = new Rect(0f, 0f, Mathf.Min(Screen.width, 640f * hudScale), 132f * hudScale);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.Box(box, GUIContent.none);
            GUI.color = previous;
            GUI.Label(new Rect(12f * hudScale, 6f * hudScale, box.width - 24f * hudScale, box.height - 12f * hudScale),
                _hudText, _hudStyle);
        }

        /// <summary>HUD 用最近 2 秒的窗口；字符串每 0.25 秒才重建，避免每帧分配污染 GC 采样。</summary>
        private string BuildHudText()
        {
            int count = Collect(_elapsed - WindowSeconds, _elapsed);
            float seconds = SumSeconds(_scratch, count);
            Array.Sort(_scratch, 0, count);
            float fps = seconds > 0f && count > 0 ? count / seconds : 0f;
            float p95 = PercentileSorted(_scratch, count, 0.95f) * 1000f;
            float alloc = seconds > 0f ? _collectAlloc / 1024f / seconds : 0f;
            float wasm = GetWasmHeapBytes() / 1048576f;

            float minPair = _spawner != null ? _spawner.MinPairDistance : 0f;
            return "PERF limit " + _activeLimit + "  alive " + _lastAlive + "  peak " + _peakAlive
                   + "\nfps " + F(fps, 0) + "   p95 " + F(p95, 1) + "ms"
                   + "\nminPair " + F(minPair, 2) + "   front " + _frameSettled
                   + "\nalloc " + F(alloc, 0) + " KB/s   heap " + F(GC.GetTotalMemory(false) / 1048576f, 1)
                   + " MB   wasm " + F(wasm, 0) + " MB";
        }

        // ---------- 平台桥 ----------

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int JsGetWasmHeapBytes();
#else
        private static int JsGetWasmHeapBytes() => 0;
#endif

        private static int GetWasmHeapBytes() => JsGetWasmHeapBytes();

        /// <summary>IL2CPP/WebGL 上 GC.CollectionCount 不保证可用；不支持就返回 0，不影响其他指标。</summary>
        private int SafeCollectionCount()
        {
            if (!_collectionSupported)
            {
                return 0;
            }

            try
            {
                return GC.CollectionCount(0);
            }
            catch (Exception)
            {
                _collectionSupported = false;
                return 0;
            }
        }

        // ---------- 工具 ----------

        private static Dictionary<string, string> ParseQuery(string url)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(url))
            {
                return result;
            }

            int question = url.IndexOf('?');
            if (question < 0)
            {
                return result;
            }

            string raw = url.Substring(question + 1);
            int hash = raw.IndexOf('#');
            if (hash >= 0)
            {
                raw = raw.Substring(0, hash);
            }

            foreach (string pair in raw.Split('&'))
            {
                if (pair.Length == 0)
                {
                    continue;
                }

                int equals = pair.IndexOf('=');
                string key = equals < 0 ? pair : pair.Substring(0, equals);
                string value = equals < 0 ? string.Empty : pair.Substring(equals + 1);
                result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value);
            }

            return result;
        }

        private static int ParseInt(Dictionary<string, string> query, string key)
        {
            return query.TryGetValue(key, out string value) && int.TryParse(value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : 0;
        }

        private static float ParseFloat(Dictionary<string, string> query, string key)
        {
            return query.TryGetValue(key, out string value) && float.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : 0f;
        }

        /// <summary>?enemyKind=chaser|charger|flanker；非法值忽略并告警（不静默选错类型）。</summary>
        private static EnemyKind? ParseEnemyKind(Dictionary<string, string> query)
        {
            if (!query.TryGetValue("enemyKind", out string value) || string.IsNullOrEmpty(value))
            {
                return null;
            }

            switch (value.ToLowerInvariant())
            {
                case "chaser":
                    return EnemyKind.Chaser;
                case "charger":
                    return EnemyKind.Charger;
                case "flanker":
                    return EnemyKind.Flanker;
                default:
                    Debug.LogWarning($"[PERF] 未知 enemyKind={value}，忽略。");
                    return null;
            }
        }

        private static string F(float value, int decimals)
        {
            return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\"", "'").Replace("\\", "/");
        }
    }
}
