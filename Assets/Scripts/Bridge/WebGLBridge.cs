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
    private const int ProtocolVersion = 1;
    private const int MaxNicknameLength = 64;

    [SerializeField] private GameManager _gameManager;

    private string _nickname = string.Empty;
    private bool _readySent;
    private bool _reported;
    private bool _pausedByVisibility;
    private bool _subscribed;

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
                breakdown = new ScoreBreakdown
                {
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

    /// <summary>Ready → Playing。参数：JSON `{"nickname":"..."}`；兼容直接传昵称字符串。</summary>
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

        _nickname = ParseNickname(payload);
        _pausedByVisibility = false;
        _reported = false;
        _gameManager.StartGame();
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

    /// <summary>Paused → Playing（H5 手动继续）。</summary>
    public void ResumeGame(string _)
    {
        if (!EnsureManager("ResumeGame"))
        {
            return;
        }

        _pausedByVisibility = false;
        _gameManager.ResumeGame();
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
    /// 标签页隐藏时自动暂停；恢复时只恢复「因隐藏而暂停」的那一次。
    /// 手动暂停不设置标记，因此切回标签页不会自动继续。
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

            return;
        }

        if (_pausedByVisibility)
        {
            _pausedByVisibility = false;
            if (_gameManager.State == GameState.Paused)
            {
                _gameManager.ResumeGame();
            }
        }
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

    /// <summary>只解析昵称，不渲染；超长直接截断，避免把异常大的字符串带进结算 JSON。</summary>
    private static string ParseNickname(string payload)
    {
        string nickname = string.Empty;

        if (!string.IsNullOrEmpty(payload))
        {
            string trimmed = payload.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    var message = JsonUtility.FromJson<StartGameMessage>(trimmed);
                    if (message != null && message.nickname != null)
                    {
                        nickname = message.nickname;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Bridge] StartGame JSON 解析失败，按空昵称处理: {exception.Message}");
                }
            }
            else
            {
                // 兼容旧协议：直接传昵称字符串。
                nickname = trimmed;
            }
        }

        nickname = nickname.Trim();
        if (nickname.Length > MaxNicknameLength)
        {
            nickname = nickname.Substring(0, MaxNicknameLength);
        }

        return nickname;
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
