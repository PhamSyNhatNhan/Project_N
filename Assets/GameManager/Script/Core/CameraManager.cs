using UnityEngine;
using Unity.Cinemachine;

[RequireComponent(typeof(CinemachineImpulseSource))]
public class CameraManager : MonoBehaviour
{
    // ── Config ────────────────────────────────────────────────────
    [SerializeField] private CinemachineCamera virtualCamera;
    [SerializeField] private string playerTag = "Player";

    // ── State ─────────────────────────────────────────────────────
    private CinemachineImpulseSource impulseSource;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        impulseSource = GetComponent<CinemachineImpulseSource>();
        if (impulseSource == null)
            Debug.LogWarning("[CameraManager] Thiếu CinemachineImpulseSource trên GameObject này.");

        if (virtualCamera == null)
            virtualCamera = FindFirstObjectByType<CinemachineCamera>();

        if (virtualCamera == null)
        {
            Debug.LogWarning("[CameraManager] Không tìm thấy CinemachineCamera trong scene.");
            return;
        }

        FindAndFollowPlayer();
    }

    private void OnEnable()
    {
        EventManager.Gm.OnCameraShake.Get().AddListener(OnShakeReceived);
    }

    private void OnDisable()
    {
        EventManager.Gm.OnCameraShake.Get().RemoveListener(OnShakeReceived);
    }

    // ── Player ────────────────────────────────────────────────────
    private void FindAndFollowPlayer()
    {
        if (virtualCamera.Follow != null) return;

        GameObject player = GameObject.FindWithTag(playerTag);
        if (player == null)
        {
            Debug.LogWarning($"[CameraManager] Không tìm thấy GameObject với tag '{playerTag}'");
            return;
        }

        virtualCamera.Follow = player.transform;
        virtualCamera.LookAt = player.transform;
    }

    public void SetFollowTarget(Transform target)
    {
        virtualCamera.Follow = target;
        virtualCamera.LookAt = target;
    }

    // ── Event ─────────────────────────────────────────────────────
    private void OnShakeReceived(Component sender, object data)
    {
        if (data is not ShakeData shakeData) return;
        Shake(shakeData);
    }

    // ── Shake ─────────────────────────────────────────────────────
    public void Shake(ShakeData data)
    {
        if (impulseSource == null) return;
        impulseSource.DefaultVelocity = new Vector3(data.amplitude, data.amplitude, 0f);
        impulseSource.GenerateImpulse(data.frequency);
    }
}