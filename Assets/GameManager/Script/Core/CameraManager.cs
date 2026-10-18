using UnityEngine;
using System.Linq;
using Unity.Cinemachine;

[RequireComponent(typeof(CinemachineImpulseSource))]
public class CameraManager : MonoBehaviour
{
    // ── Config ────────────────────────────────────────────────────
    [SerializeField] private CinemachineVirtualCameraBase virtualCamera;
    [SerializeField] private string playerTag = "Player";

    // ── State ─────────────────────────────────────────────────────
    private CinemachineImpulseSource impulseSource;
    private EveEngine.EveEventSubscription playerBinding,worldBinding;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        impulseSource = GetComponent<CinemachineImpulseSource>();
        if (impulseSource == null)
            Debug.LogWarning("[CameraManager] Thiếu CinemachineImpulseSource trên GameObject này.");

        if (virtualCamera == null)
            virtualCamera = ScopedCamera();

        if (virtualCamera == null)
        {
            Debug.LogWarning("[CameraManager] Không tìm thấy CinemachineVirtualCameraBase trong scene.");
            return;
        }

        FindAndFollowPlayer();
    }

    private void OnEnable()
    {
        playerBinding=EveEngine.EveEventManager.Global.Subscribe<PNLocalPlayerBound>(PNLocalPlayerBound.Event,(_, notice)=>{if(PNLocalWorldContext.Current==null)LocalPlayerChanged(notice.Target);});
        worldBinding=EveEngine.EveEventManager.Global.Subscribe<PNLocalWorldBound>(PNLocalWorldBound.Event,(_, notice)=>{if(notice.Session==PNLocalWorldContext.Current?.Session.SessionId&&notice.Selection==PNLocalWorldContext.Current.SelectionRevision)LocalPlayerChanged(notice.Actor!=null?notice.Actor.transform:null);});
        EventManager.Gm.OnCameraShake.Get().AddListener(OnShakeReceived);
    }

    private void OnDisable()
    {
        playerBinding?.Dispose();playerBinding=null;worldBinding?.Dispose();worldBinding=null;
        EventManager.Gm.OnCameraShake.Get().RemoveListener(OnShakeReceived);
    }

    // ── Player ────────────────────────────────────────────────────
    private void FindAndFollowPlayer()
    {
        if (virtualCamera.Follow != null) return;

        var local=PNLocalWorldContext.Current;
        var target=local?.Actor?.transform;
        if(target==null&&local==null)target=gameObject.scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Transform>(true)).FirstOrDefault(x=>x.CompareTag(playerTag));
        if(target==null)return;
        var player=target.gameObject;

        virtualCamera.Follow = player.transform;
        virtualCamera.LookAt = player.transform;
    }

    private CinemachineVirtualCameraBase ScopedCamera()
    {
        var scene=PNLocalWorldContext.Current?.World?.PhysicsOwner??gameObject.scene;
        return scene.IsValid()&&scene.isLoaded?scene.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<CinemachineVirtualCameraBase>(true)).FirstOrDefault():null;
    }
    private void LocalPlayerChanged(Transform target)
    {CinemachineImpulseManager.Instance.Clear();SetFollowTarget(target);}
    public void SetFollowTarget(Transform target)
    {
        // The persistent manager can awaken before the gameplay scene's camera exists.
        if(virtualCamera==null||PNLocalWorldContext.Current!=null&&virtualCamera.gameObject.scene!=PNLocalWorldContext.Current.World?.PhysicsOwner)virtualCamera=ScopedCamera();
        if(virtualCamera==null)return;
        virtualCamera.Follow = target;
        virtualCamera.LookAt = target;
    }

    // ── Event ─────────────────────────────────────────────────────
    private void OnShakeReceived(Component sender, object data)
    {
        if (data is not ShakeData shakeData) return;
        var local=PNLocalWorldContext.Current;
        if(local!=null&&(!local.Contains(sender)||EveEngine.EveRegistrySession.IsReady&&PNWorldGameplay.ForScene(local.Session,gameObject.scene) is EveEngine.EveWorldContext own&&own!=local.World))return;
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
