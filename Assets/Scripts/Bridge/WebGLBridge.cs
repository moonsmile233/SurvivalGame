using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// H5 ↔ Unity 的唯一出口。GameObject 必须叫 `WebGLBridge` 且保持激活，
/// 否则 H5 侧的 SendMessage 找不到目标。
/// 只做边界转换，不写任何玩法逻辑。
/// </summary>
public class WebGLBridge : MonoBehaviour
{
    /// <summary>收到 H5 消息并回发后触发，供场景内 UI 显示。</summary>
    public static event Action<string> PongReceived;

    /// <summary>最近一次 Pong 内容，供晚于事件订阅的对象查询。</summary>
    public static string LastPong { get; private set; } = string.Empty;

    public static void NotifyReady()
    {
        JsNotifyReady();
        Debug.Log("[Bridge] ready");
    }

    public static void NotifyPong(string message)
    {
        LastPong = message ?? string.Empty;
        PongReceived?.Invoke(LastPong);
        JsNotifyPong(LastPong);
        Debug.Log($"[Bridge] pong: {LastPong}");
    }

    /// <summary>H5 → Unity 入口。SendMessage 只允许一个 string 参数。</summary>
    public void Ping(string message)
    {
        Debug.Log($"[Bridge] ping: {message}");
        NotifyPong(message);
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void JsNotifyReady();
    [DllImport("__Internal")] private static extern void JsNotifyPong(string message);
#else
    private static void JsNotifyReady() => Debug.Log("[Bridge] ready (editor)");
    private static void JsNotifyPong(string m) => Debug.Log($"[Bridge] pong (editor): {m}");
#endif
}
