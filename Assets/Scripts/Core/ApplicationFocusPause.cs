using UnityEngine;

namespace Survival.Core
{
    /// <summary>
    /// 独立运行（WebGL / Standalone）的失焦兜底：失去焦点时暂停，切回**不自动恢复**（M4.6）。
    /// WebGL 里浏览器切标签/最小化会触发 OnApplicationFocus(false)（页面 visibilitychange），
    /// OnApplicationPause 覆盖移动端浏览器的切后台路径；两个回调都走同一处理，重复调用由
    /// GameManager 的状态守卫吸收（幂等）。
    ///
    /// Editor 内直接 return：编辑器失焦会停下 Play 循环（遗留问题 §4），自动暂停会让自测误判。
    /// H5 若仍发 SetPausedByVisibility，Bridge 走同一套状态守卫，不会与这里“打架”。
    ///
    /// Starting 期间失焦：取消 3-2-1 回来源态（与 H5 信号一致），不能在玩家看不见时进 Playing。
    /// </summary>
    public class ApplicationFocusPause : MonoBehaviour
    {
        [SerializeField] private GameManager _gameManager;

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                HandleLostFocus();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                HandleLostFocus();
            }
        }

        private void HandleLostFocus()
        {
            if (Application.isEditor || _gameManager == null)
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
    }
}
