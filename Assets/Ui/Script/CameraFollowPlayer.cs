using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Gắn trên CinemachineVirtualCamera trong scene combat/boss.
/// Tự động follow player sau khi PlayerSpawner spawn xong.
/// </summary>
public class CameraFollowPlayer : MonoBehaviour
{
    private CinemachineVirtualCamera _vcam;

    private void Awake()
    {
        _vcam = GetComponent<CinemachineVirtualCamera>();
        EventManager.Gm.OnPlayerSpawned.Get().AddListener(OnPlayerSpawned);
    }

    private void OnDestroy()
    {
        EventManager.Gm.OnPlayerSpawned.Get().RemoveListener(OnPlayerSpawned);
    }

    private void OnPlayerSpawned(Component sender, object data)
    {
        if (data is not Stat stat) return;
        if (_vcam == null) return;

        _vcam.Follow = stat.transform;
        _vcam.LookAt = stat.transform;

        // Snap camera đến vị trí player ngay lập tức, không lerp
        _vcam.ForceCameraPosition(stat.transform.position, Quaternion.identity);
    }
}