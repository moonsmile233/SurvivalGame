using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Survival.Core;
using UnityEngine;

/// <summary>
/// H5 ↔ Unity 的唯一出口（protocol 3，v1.2 游戏优先）。
/// GameObject 必须叫 `WebGLBridge` 且保持激活，否则 H5 侧的 SendMessage 找不到目标。
/// 只做边界转换（JSON 编解码 + 状态转发），不写任何玩法逻辑。
///
/// v3 边界：
/// - H5 → Unity 只保留 `EnterGame`（昵称仅日志）；旧 `StartGame` 当 EnterGame 兼容；
///   旧局内指令 `PauseGame/ResumeGame/RestartGame/ReturnToMenu` 是 no-op（一次性 warning）；
///   `SetPausedByVisibility` 保留为失焦兜底（与 ApplicationFocusPause 幂等）。
/// - Unity → H5：`onReady`(protocol=3 + capabilities)、`onStateChange`(menu/playing/paused/result)、
///   `onGameOver`（每局一次）。`Starting` 是内部过渡态，不发状态事件。
/// 协议见 `docs/bridge-protocol.md`。
/// </summary>
public class WebGLBridge : MonoBehaviour
{
    private const int ProtocolVersion = 3;
    private const int MaxNicknameLength = 64;

    [SerializeField] private GameManager _gameManager;

    private string _nickname = string.Empty;
    private bool _readySent;
    private bool _reported;

    // 旧指令每个方法只告警一次（旧页可能反复发 StartGame/PauseGame；一个 bool 会漏报后面的方法）。
    private readonly HashSet<string> _legacyWarned = new HashSet<string>(StringComparer.Ordinal);

    // EnterGame 回填 lastDifficulty 时会触发 GameManager.OnMenuDifficultyChanged；
    // 这次变化来自 H5 自己，不能再发 onDifficultyChanged 回去（避免循环）。
    private bool _applyingRemoteDifficulty;

    private void Awake()
    {
        if (_gameManager == null)
        {
            _gameManager = FindFirstObjectByType<GameManager>();
        }
    }

    private void Start()
    {
        if (_gameManager == null)
        {
            Debug.LogError("[Bridge] GameManager 引用缺失，H5 指令将无法驱动状态机。");
            return;
        }

        _gameManager.OnStateChanged += HandleStateChanged;
        _gameManager.OnMenuDifficultyChanged += HandleMenuDifficultyChanged;
        Debug.Log($"[Bridge] subscribed to GameManager (state={_gameManager.State}, protocol={ProtocolVersion})");
    }

    private void OnDestroy()
    {
        if (_gameManager != null)
        {
            _gameManager.OnStateChanged -= HandleStateChanged;
            _gameManager.OnMenuDifficultyChanged -= HandleMenuDifficultyChanged;
        }
    }

    // ---------- Unity → H5 ----------

    /// <summary>首帧后通知 H5 可以开始；重复调用只发一次。</summary>
    public void NotifyReady()
    {
        if (_readySent)
        {
            return;
        }

        _readySent = true;
        string json = JsonUtility.ToJson(new ReadyMessage
        {
            protocol = ProtocolVersion,
            capabilities = new[] { "enterGame", "stateChange", "gameOver", "difficultyChanged" },
        });
        JsNotifyReady(json);
    }

    private void HandleStateChanged(GameState state)
    {
        // Starting 是内部过渡态：不发 onStateChange（策划裁决 2026-11-01）。
        if (state == GameState.Starting)
        {
            return;
        }

        // 新一局开始：允许下一次结算再次上报（重开/再来一局都会走到 Playing）。
        if (state == GameState.Playing)
        {
            _reported = false;
        }

        string wire = MapToWire(state);
        if (wire != null)
        {
            string stateJson = JsonUtility.ToJson(new StateMessage
            {
                protocol = ProtocolVersion,
                state = wire,
            });
            JsNotifyStateChange(stateJson);
            Debug.Log($"[Bridge] stateChange -> {wire}");
        }

        if (state == GameState.Result && !_reported)
        {
            _reported = true;
            JsNotifyGameOver(BuildGameOverJson());
        }
    }

    /// <summary>菜单难度选择变化 → H5 代存（H5 不提供选择 UI）。回填产生的回声已抑制。</summary>
    private void HandleMenuDifficultyChanged(Difficulty difficulty)
    {
        if (_applyingRemoteDifficulty)
        {
            return;
        }

        string json = JsonUtility.ToJson(new DifficultyMessage
        {
            protocol = ProtocolVersion,
            difficulty = DifficultyToWire(difficulty),
        });
        JsNotifyDifficultyChanged(json);
        Debug.Log($"[Bridge] difficultyChanged -> {difficulty}");
    }

    /// <summary>唯一的状态→线格式表；新增状态必须在这里显式决定是否发给 H5。</summary>
    private static string MapToWire(GameState state)
    {
        switch (state)
        {
            case GameState.Menu:
                return "menu";
            case GameState.Playing:
                return "playing";
            case GameState.Paused:
                return "paused";
            case GameState.Result:
                return "result";
            default:
                return null;
        }
    }

    private string BuildGameOverJson()
    {
        bool won = _gameManager.LastRoundWon;
        float survived = Mathf.Min(_gameManager.ElapsedTime, _gameManager.Config.RoundDuration);
        ScoreService score = _gameManager.Score;

        var envelope = new GameOverEnvelope
        {
            protocol = ProtocolVersion,
            @event = "gameOver",
            payload = new GameOverPayload
            {
                result = won ? "win" : "lose",
                score = score.Score,
                survivedSeconds = Mathf.Round(survived * 10f) / 10f,
                kills = score.Kills,
                nickname = _nickname,
                // 回传「Unity 实际采用」的档位，H5 以它记分（协议 v3）。
                difficulty = DifficultyToWire(_gameManager.Config.ActiveDifficulty),
                breakdown = new ScoreBreakdown
                {
                    survival = score.SurvivalScore,
                    kills = score.KillScore,
                    winBonus = score.WinBonus,
                },
            },
        };

        return JsonUtility.ToJson(envelope);
    }

    // ---------- H5 → Unity（SendMessage 只能带一个 string 参数） ----------

    /// <summary>
    /// 进入游戏（H5「开始游戏」）。参数 JSON `{"nickname":"...","lastDifficulty":"easy|normal|hard"}`
    /// 或纯昵称字符串（v1 写法）。昵称只保存不渲染；lastDifficulty 只作菜单初始选中，不自动开局。
    /// 旧页 v2 的 `difficulty` 作为 lastDifficulty 的别名兼容（解析顺序 lastDifficulty ?? difficulty ?? normal）。
    /// H5 回填难度时不发 onDifficultyChanged 回声（避免循环）。
    /// </summary>
    public void EnterGame(string payload)
    {
        if (!EnsureManager("EnterGame"))
        {
            return;
        }

        // EnterGame 只属于“进入游戏”这一次：局内/倒计时/结算里再来一次说明 H5 状态过期，
        // 忽略而不是悄悄改难度偏好（与 bridge-protocol §2 的守卫一致）。
        if (_gameManager.State != GameState.Menu)
        {
            Debug.LogWarning($"[Bridge] EnterGame ignored in state {_gameManager.State}");
            return;
        }

        ParseEnterGame(payload, out string nickname, out Difficulty difficulty);
        _nickname = nickname;
        _reported = false;

        _applyingRemoteDifficulty = true;
        try
        {
            _gameManager.SetMenuDifficulty(difficulty);
        }
        finally
        {
            _applyingRemoteDifficulty = false;
        }

        Debug.Log($"[Bridge] EnterGame nickname='{_nickname}' lastDifficulty={difficulty} state={_gameManager.State}");
    }

    /// <summary>v2 旧入口：按 EnterGame 处理，不自动开局（方案 §7 兼容口径）。</summary>
    public void StartGame(string payload)
    {
        WarnLegacy("StartGame（按 EnterGame 处理）");
        EnterGame(payload);
    }

    /// <summary>v2 旧指令：v1.2 起局内全部由 Unity UI 处理，这里只告警不执行。</summary>
    public void PauseGame(string _) => WarnLegacy("PauseGame");

    /// <summary>v2 旧指令：no-op。</summary>
    public void ResumeGame(string _) => WarnLegacy("ResumeGame");

    /// <summary>v2 旧指令：no-op。</summary>
    public void RestartGame(string _) => WarnLegacy("RestartGame");

    /// <summary>v2 旧指令：no-op。</summary>
    public void ReturnToMenu(string _) => WarnLegacy("ReturnToMenu");

    /// <summary>
    /// 失焦/标签页隐藏（环境信号，不是局内操作）：Playing → Paused；Starting → 取消倒计时回来源态。
    /// 切回 `"false"` 直接忽略（Unity 不存标记；H5 自己按最后发送值去重），保持 Paused、不自动恢复（M4.6）。
    /// 与 <see cref="ApplicationFocusPause"/> 幂等：重复 true 由 GameManager 状态守卫吸收。
    /// </summary>
    public void SetPausedByVisibility(string payload)
    {
        if (!EnsureManager("SetPausedByVisibility"))
        {
            return;
        }

        bool hidden = string.Equals(payload, "true", StringComparison.OrdinalIgnoreCase);
        if (!hidden)
        {
            return;
        }

        if (_gameManager.State == GameState.Playing)
        {
            _gameManager.PauseGame();
        }
        else if (_gameManager.State == GameState.Starting)
        {
            _gameManager.CancelStartCountdown();
        }
    }

    private void WarnLegacy(string method)
    {
        if (!_legacyWarned.Add(method))
        {
            return;
        }

        Debug.LogWarning($"[Bridge] {method}：v2 旧指令已废弃，v1.2 由 Unity 内部处理；请刷新到 v1.2 H5");
    }

    private bool EnsureManager(string caller)
    {
        if (_gameManager != null)
        {
            return true;
        }

        Debug.LogError($"[Bridge] {caller} 无法执行：GameManager 引用缺失。");
        return false;
    }

    /// <summary>
    /// 解析 EnterGame 载荷：昵称 + lastDifficulty（可选）。昵称只保存不渲染，超长截断；
    /// lastDifficulty 非法/缺省 → normal（与 H5 代存契约一致，协议 v3）。
    /// </summary>
    private static void ParseEnterGame(string payload, out string nickname, out Difficulty difficulty)
    {
        nickname = string.Empty;
        difficulty = Difficulty.Normal;

        if (!string.IsNullOrEmpty(payload))
        {
            string trimmed = payload.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    var message = JsonUtility.FromJson<EnterGameMessage>(trimmed);
                    if (message != null)
                    {
                        if (message.nickname != null)
                        {
                            nickname = message.nickname;
                        }

                        // v3 用 lastDifficulty；旧页 v2 发的是 difficulty。两者都缺省/为空时按 normal。
                        string requested = message.lastDifficulty;
                        if (string.IsNullOrEmpty(requested))
                        {
                            requested = message.difficulty;
                        }

                        if (!string.IsNullOrEmpty(requested) && !TryParseDifficulty(requested, out difficulty))
                        {
                            Debug.LogWarning($"[Bridge] 未知难度 '{requested}'，按 normal 处理");
                            difficulty = Difficulty.Normal;
                        }
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Bridge] EnterGame JSON 解析失败，按空昵称/普通难度处理: {exception.Message}");
                }
            }
            else
            {
                nickname = trimmed;
            }
        }

        nickname = nickname.Trim();
        nickname = TruncateCodePoints(nickname, MaxNicknameLength);
    }

    /// <summary>
    /// 按 Unicode 码点截断（不劈开代理对）。H5 已按 16 码点截过；这里兜住旧页/手工调用发来的长串。
    /// </summary>
    private static string TruncateCodePoints(string value, int maxCodePoints)
    {
        if (string.IsNullOrEmpty(value) || maxCodePoints <= 0)
        {
            return string.Empty;
        }

        int index = 0;
        int count = 0;
        while (index < value.Length && count < maxCodePoints)
        {
            bool surrogatePair = char.IsHighSurrogate(value[index])
                                 && index + 1 < value.Length
                                 && char.IsLowSurrogate(value[index + 1]);
            index += surrogatePair ? 2 : 1;
            count++;
        }

        return index >= value.Length ? value : value.Substring(0, index);
    }

    /// <summary>线格式 → 枚举。只认小写三值；其它一律 false（调用方回落 normal）。</summary>
    private static bool TryParseDifficulty(string value, out Difficulty difficulty)
    {
        switch (value)
        {
            case "easy":
                difficulty = Difficulty.Easy;
                return true;
            case "normal":
                difficulty = Difficulty.Normal;
                return true;
            case "hard":
                difficulty = Difficulty.Hard;
                return true;
            default:
                difficulty = Difficulty.Normal;
                return false;
        }
    }

    /// <summary>枚举 → 线格式（结算回传 / onDifficultyChanged）。</summary>
    private static string DifficultyToWire(Difficulty difficulty)
    {
        switch (difficulty)
        {
            case Difficulty.Easy:
                return "easy";
            case Difficulty.Hard:
                return "hard";
            default:
                return "normal";
        }
    }

    // ---------- 原生桥 ----------

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void JsNotifyReady(string json);
    [DllImport("__Internal")] private static extern void JsNotifyStateChange(string json);
    [DllImport("__Internal")] private static extern void JsNotifyGameOver(string json);
    [DllImport("__Internal")] private static extern void JsNotifyDifficultyChanged(string json);
#else
    private static void JsNotifyReady(string json) => Debug.Log($"[Bridge] ready (editor): {json}");
    private static void JsNotifyStateChange(string json) => Debug.Log($"[Bridge] stateChange (editor): {json}");
    private static void JsNotifyGameOver(string json) => Debug.Log($"[Bridge] gameOver (editor): {json}");
    private static void JsNotifyDifficultyChanged(string json) => Debug.Log($"[Bridge] difficultyChanged (editor): {json}");
#endif

    // ---------- JSON 消息结构（JsonUtility 只认字段） ----------

    [Serializable]
    private sealed class ReadyMessage
    {
        public int protocol;
        public string[] capabilities;
    }

    [Serializable]
    private sealed class StateMessage
    {
        public int protocol;
        public string state;
    }

    [Serializable]
    private sealed class EnterGameMessage
    {
        public string nickname;
        public string lastDifficulty; // v3
        public string difficulty;     // v2 旧页别名（只读兼容）
    }

    [Serializable]
    private sealed class DifficultyMessage
    {
        public int protocol;
        public string difficulty;
    }

    [Serializable]
    private sealed class GameOverEnvelope
    {
        public int protocol;
        public string @event;
        public GameOverPayload payload;
    }

    [Serializable]
    private sealed class GameOverPayload
    {
        public string result;
        public int score;
        public float survivedSeconds;
        public int kills;
        public string nickname;
        public string difficulty;
        public ScoreBreakdown breakdown;
    }

    [Serializable]
    private sealed class ScoreBreakdown
    {
        public int survival;
        public int kills;
        public int winBonus;
    }
}
