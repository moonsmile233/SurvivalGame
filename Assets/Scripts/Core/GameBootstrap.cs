using System.Collections;
using UnityEngine;

/// <summary>
/// 只负责「等一帧后通知 H5 就绪」。M2 起不再画任何调试 UI（见 ticket M2 第 2 节）。
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    [SerializeField] private WebGLBridge _bridge;

    private void Awake()
    {
        // 低帧率下限制单帧 deltaTime，避免一帧位移过大穿透碰撞体或一帧多次判定。
        Time.maximumDeltaTime = 0.1f;

        if (_bridge == null)
        {
            _bridge = FindFirstObjectByType<WebGLBridge>();
        }
    }

    private IEnumerator Start()
    {
        // 等一帧，确保画面已经开始渲染再通知 H5
        yield return null;

        if (_bridge != null)
        {
            _bridge.NotifyReady();
        }
        else
        {
            Debug.LogError("[Bootstrap] WebGLBridge 引用缺失，无法通知 H5 就绪。");
        }
    }
}
