using System;
using System.Runtime.InteropServices;
using Survival.Core;
using UnityEngine;

/// <summary>
/// H5 ↔ Unity 的唯一出口。GameObject 必须叫 `WebGLBridge` 且保持激活，
/// 否则 H5 侧的 SendMessage 找不到目标。
/// 只做边界转换（JSON 编解码 + 状态转发），不写任何玩法逻辑。
/// 协议见 `docs/bridge-protocol.md`。
/// </summary>
public class WebGLBridge : MonoBehaviour
{
    private const int ProtocolVersion = 2;
    private const int MaxNicknameLength = 64;

    [SerializeField] private GameManager _gameManager;

    private string _nickname = string.Empty;
    private bool _readySent;
    private bool _reported;
    private bool _pausedByVisibility;
    private bool _subscribed;

    /// <summary>
    /// H5 嵌入模式：WebGL 构建下为 true，结算与重开交给页面，Unity 不显示 ResultPanel
    /// （M4 第 1.3 节）。Editor 里保持 false，方便自测 Unity 结算面板。
    /// </summary>
    public bool IsEmbedded { get; private set; }

    private void Awake()
    {
        if (_gameManager == null)
        {
            _gameManager = FindFirstObjectByType<GameManager>();
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        IsEmbedded = true;
#endif
    }

    private void Start()
    {
        if (_gameManager == null)
        {
            Debug.LogError("[Bridge] GameManager 引用缺失，H5 指令将无法驱动状态机。");
            return;
        }

        _gameManager.OnStateChanged += HandleStateChanged;
        _subscribed = true;
        Debug.Log($"[Bridge] subscribed to GameManager (state={_gameManager.State})");
    }

    private void OnDestroy()
    {
        if (_subscribed && _gameManager != null)
        {
            _gameManager.OnStateChanged -= HandleStateChanged;
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
        string json = JsonUtility.ToJson(new ReadyMessage { protocol = ProtocolVersion });
        JsNotifyReady(json);
    }

    private void HandleStateChanged(GameState state)
    {
        Debug.Log($"[Bridge] stateChange -> {state}");

        // 新一局开始：允许下一次结算再次上报（H5 的 RestartGame 与 Unity 结算面板的重开都会走到这里）。
        if (state == GameState.Playing)
        {
            _reported = false;
        }

        string stateJson = JsonUtility.ToJson(new StateMessage
        {
            protocol = ProtocolVersion,
            state = state.ToString(),
        });
        JsNotifyStateChange(stateJson);

        if ((state == GameState.Won || state == GameState.Lost) && !_reported)
        {
            _reported = true;
            JsNotifyGameOver(BuildGameOverJson(state));
        }
    }

    private string BuildGameOverJson(GameState state)
    {
        bool won = state == GameState.Won;
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
                // 回传「Unity 实际采用」的档位（非法/缺省已被回落为 normal），H5 以它分档记分。
                difficulty = DifficultyToWire(_gameManager.Config.ActiveDifficulty),
                breakdown = new ScoreBreakdown
                {
                    // survival 已含难度倍率；H5 只展示，不再换算（协议 v2 §3）。
                    survival = score.SurvivalScore,
                    kills = score.KillScore,
                    winBonus = score.WinBonus,
                },
            },
        };

        // JsonUtility 会把浮点数 60 写成 60，H5 侧统一按 number 处理。
        return JsonUtility.ToJson(envelope);
    }

    // ---------- H5 → Unity（SendMessage 只能带一个 string 参数） ----------

    /// <summary>
    /// Ready → Playing。参数：JSON `{"nickname":"...","difficulty":"easy|normal|hard"}`；
    /// 兼容 v1 的直接传昵称字符串（难度按 normal）。难度只在这里解析一次，局内锁定（协议 v2 §2.2）。
    /// </summary>
    public void StartGame(string payload)
    {
        if (!EnsureManager("StartGame"))
        {
            return;
        }

        if (_gameManager.State != GameState.Ready)
        {
            Debug.LogWarning($"[Bridge] StartGame ignored in state {_gameManager.State}");
            return;
        }

        ParseStartGame(payload, out string nickname, out Difficulty difficulty);
        _nickname = nickname;
        _pausedByVisibility = false;
        _reported = false;

        // 能收到 StartGame 就说明页面在驱动游戏：结算交给 H5（ticket M4 1.3）。
        IsEmbedded = true;
        _gameManager.StartGame(difficulty);
    }

    /// <summary>Playing → Paused（H5 手动暂停）。</summary>
    public void PauseGame(string _)
    {
        if (!EnsureManager("PauseGame"))
        {
            return;
        }

        _pausedByVisibility = false;
        _gameManager.PauseGame();
    }

    /// <summary>Paused → 先 3-2-1 再 Playing（H5 手动继续；M4.6 起不直接恢复）。</summary>
    public void ResumeGame(string _)
    {
        if (!EnsureManager("ResumeGame"))
        {
            return;
        }

        _pausedByVisibility = false;
        _gameManager.RequestCountdownResume();
    }

    /// <summary>Won / Lost → Playing，全量重置；昵称沿用。</summary>
    public void RestartGame(string _)
    {
        if (!EnsureManager("RestartGame"))
        {
            return;
        }

        _pausedByVisibility = false;
        _gameManager.RestartGame();
    }

    /// <summary>
    /// 中止本局回到 Ready（H5「返回主菜单」，协议 v2 §2）。不计分、不发 onGameOver。
    /// 允许从 Playing / Paused / Won / Lost 调用。
    /// </summary>
    public void ReturnToMenu(string _)
    {
        if (!EnsureManager("ReturnToMenu"))
        {
            return;
        }

        _pausedByVisibility = false;
        _gameManager.ReturnToMenu();
    }

    /// <summary>
    /// 失焦/标签页隐藏时自动暂停，切回**保持暂停**，恢复必须由玩家主动触发（M4.6）。
    /// 切回只清标记，不请求倒计时、不 ResumeGame；否则玩家看不见时游戏会自己继续。
    /// 倒计时中再次失焦：停表并保持暂停。
    /// </summary>
    public void SetPausedByVisibility(string payload)
    {
        if (!EnsureManager("SetPausedByVisibility"))
        {
            return;
        }

        bool hidden = string.Equals(payload, "true", StringComparison.OrdinalIgnoreCase);

        if (hidden)
        {
            if (_gameManager.State == GameState.Playing)
            {
                _gameManager.PauseGame();
                _pausedByVisibility = true;
            }
            else if (_gameManager.State == GameState.Paused)
            {
                // 不管是失焦自动暂停还是玩家主动恢复的倒计时，只要再次隐藏就停表、保持暂停。
                _gameManager.CancelCountdownResume();
            }

            return;
        }

        // 切回：只清标记，等玩家点「继续」或按 P 再倒数。
        _pausedByVisibility = false;
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
    /// 解析昵称 + 难度（一次解析、开局快照）。昵称只保存不渲染，超长截断，避免把异常大的字符串带进结算 JSON。
    /// 非法/缺省难度 → normal；纯昵称字符串（v1 写法）→ 昵称 + normal。
    /// </summary>
    private static void ParseStartGame(string payload, out string nickname, out Difficulty difficulty)
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
                    var message = JsonUtility.FromJson<StartGameMessage>(trimmed);
                    if (message != null)
                    {
                        if (message.nickname != null)
                        {
                            nickname = message.nickname;
                        }

                        // 缺省（v1 H5）静默按 normal；有值但认不出才告警，方便排查前端拼错。
                        if (!string.IsNullOrEmpty(message.difficulty) &&
                            !TryParseDifficulty(message.difficulty, out difficulty))
                        {
                            Debug.LogWarning($"[Bridge] 未知难度 '{message.difficulty}'，按 normal 处理");
                            difficulty = Difficulty.Normal;
                        }
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Bridge] StartGame JSON 解析失败，按空昵称/普通难度处理: {exception.Message}");
                }
            }
            else
            {
                // 兼容旧协议：直接传昵称字符串（难度 normal）。
                nickname = trimmed;
            }
        }

        nickname = nickname.Trim();
        if (nickname.Length > MaxNicknameLength)
        {
            nickname = nickname.Substring(0, MaxNicknameLength);
        }
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
#else
    private static void JsNotifyReady(string json) => Debug.Log($"[Bridge] ready (editor): {json}");
    private static void JsNotifyStateChange(string json) => Debug.Log($"[Bridge] stateChange (editor): {json}");
    private static void JsNotifyGameOver(string json) => Debug.Log($"[Bridge] gameOver (editor): {json}");
#endif

    // ---------- JSON 消息结构（JsonUtility 只认字段） ----------

    [Serializable]
    private sealed class ReadyMessage
    {
        public int protocol;
    }

    [Serializable]
    private sealed class StateMessage
    {
        public int protocol;
        public string state;
    }

    [Serializable]
    private sealed class StartGameMessage
    {
        public string nickname;
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
