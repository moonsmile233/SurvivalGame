using UnityEngine;

namespace Survival.Core
{
    /// <summary>
    /// 桌面键盘暂停：Esc / P 在 Playing ↔ Paused 之间切换（M4 §1.7，M4.6 调整恢复语义）。
    /// 只发暂停/恢复请求，不改 Time.timeScale、不碰 UI——状态权威仍在 <see cref="GameManager"/>。
    /// 恢复走 RequestCountdownResume（玩家主动恢复才 3-2-1），不直接 ResumeGame；
    /// PausePanel 在倒计时中会忽略重复请求，所以按住/连按不会加速。
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
                // 玩家主动恢复：先 3-2-1 再 ResumeGame（M4.6）。倒计时中重复按 P 由 PausePanel 忽略。
                _gameManager.RequestCountdownResume();
            }
        }
    }
}
