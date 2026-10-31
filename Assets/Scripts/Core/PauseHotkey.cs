using UnityEngine;

namespace Survival.Core
{
    /// <summary>
    /// 桌面键盘暂停：Esc / P 在 Playing ↔ Paused 之间切换（M4 第 1.7 节，补 M2 删掉 Esc 的漏项）。
    /// 只发暂停/恢复请求，不改 Time.timeScale、不碰 UI——状态权威仍在 <see cref="GameManager"/>。
    /// 用 GetKeyDown 而不是 GetKey：按住不连发；Ready（H5 昵称输入）/结算状态一律忽略。
    /// Esc 在浏览器全屏时可能被用于退出全屏，所以 P 必须同时可用，不能只绑 Esc。
    /// </summary>
    public class PauseHotkey : MonoBehaviour
    {
        [SerializeField] private GameManager _gameManager;

        private void Update()
        {
            if (_gameManager == null)
            {
                return;
            }

            if (!Input.GetKeyDown(KeyCode.Escape) && !Input.GetKeyDown(KeyCode.P))
            {
                return;
            }

            if (_gameManager.State == GameState.Playing)
            {
                _gameManager.PauseGame();
            }
            else if (_gameManager.State == GameState.Paused)
            {
                // 自动暂停的 3-2-1 正在走时按 P：立即恢复；PausePanel 在状态离开 Paused 时会停掉倒计时。
                _gameManager.ResumeGame();
            }
        }
    }
}
