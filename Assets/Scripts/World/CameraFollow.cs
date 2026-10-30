using Survival.Core;
using UnityEngine;

namespace Survival.World
{
    /// <summary>
    /// 相机跟随（M4-B0）：地图 36×20 大于视野，相机在 LateUpdate 平滑跟随玩家，
    /// 并钳制在地图范围内，保证地图边缘外是 clear color，不出现「看不见的空气墙」。
    ///
    /// 平滑用指数形式 1 - exp(-k*dt)：帧率变化时收敛速度一致；
    /// 不要用 Lerp(a, b, 0.1f)（帧率相关，掉帧时跟随变慢）。
    /// 重开时玩家瞬移回原点，距离超过 SnapDistance 直接 snap，避免长距离回拉。
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        private const float Smoothing = 8f;
        private const float SnapDistance = 8f;

        [SerializeField] private GameConfig _config;
        [SerializeField] private Transform _target;
        [SerializeField] private Camera _camera;

        private void Awake()
        {
            if (_camera == null)
            {
                _camera = GetComponent<Camera>();
            }
        }

        private void LateUpdate()
        {
            if (_config == null || _target == null || _camera == null)
            {
                return;
            }

            // 暂停 / 结算时 timeScale = 0：相机完全冻结（不要在暂停时把相机吸附到目标）。
            float delta = Time.deltaTime;
            if (delta <= 0f)
            {
                return;
            }

            Vector3 targetPosition = _target.position;
            Vector3 current = transform.position;
            Vector3 desired = new Vector3(targetPosition.x, targetPosition.y, current.z);

            Vector3 next = Vector3.Distance(current, desired) > SnapDistance
                ? desired
                : Vector3.Lerp(current, desired, 1f - Mathf.Exp(-Smoothing * delta));

            // 视口半宽/半高由正交尺寸和当前宽高比决定；相机钳在地图内，玩家在地图边缘时仍可见。
            float halfHeight = _camera.orthographicSize;
            float halfWidth = halfHeight * _camera.aspect;
            float maxX = Mathf.Max(0f, _config.MapWidth * 0.5f - halfWidth);
            float maxY = Mathf.Max(0f, _config.MapHeight * 0.5f - halfHeight);

            next.x = Mathf.Clamp(next.x, -maxX, maxX);
            next.y = Mathf.Clamp(next.y, -maxY, maxY);
            next.z = current.z;

            transform.position = next;
        }
    }
}
