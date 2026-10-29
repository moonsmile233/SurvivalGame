using System.Collections;
using UnityEngine;

/// <summary>
/// M0 版本只做三件事：等一帧后通知 H5 就绪、左上角显示 Unity Ready、显示 Pong 回显。
/// M1 再换成正式 UI。
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    private GUIStyle _labelStyle;
    private string _pongMessage = string.Empty;

    private IEnumerator Start()
    {
        WebGLBridge.PongReceived += HandlePongReceived;

        // 等一帧，确保画面已经开始渲染再通知 H5
        yield return null;
        WebGLBridge.NotifyReady();
    }

    private void OnDestroy()
    {
        WebGLBridge.PongReceived -= HandlePongReceived;
    }

    private void HandlePongReceived(string message)
    {
        _pongMessage = message;
    }

    private void OnGUI()
    {
        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
            };
        }

        GUI.color = Color.white;
        GUI.Label(new Rect(16f, 16f, 480f, 32f), "Unity Ready", _labelStyle);

        if (!string.IsNullOrEmpty(_pongMessage))
        {
            GUI.Label(new Rect(16f, 52f, 480f, 32f), "Pong: " + _pongMessage, _labelStyle);
        }
    }
}
