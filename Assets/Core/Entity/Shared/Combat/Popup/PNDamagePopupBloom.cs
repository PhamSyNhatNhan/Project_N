#if UNITY_5_3_OR_NEWER
using EveEngine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public sealed class PNDamagePopupBloom : MonoBehaviour
{
    private static PNDamagePopupBloom current;
    public static PNDamagePopupBloom Ensure(Camera camera)
    {
        using var diagnostic = EveHotPathMetrics.Measure("popup.bloom-ensure");
        camera.allowHDR = true;
        var data = camera.GetComponent<UniversalAdditionalCameraData>();
        if (data == null) data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        camera.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.EveryFrame);
        if (current != null)
        {
            data.volumeLayerMask |= 1 << current.gameObject.layer;
            return current;
        }
        var prefab = Resources.Load<GameObject>("PNUI/PNDamagePopupBloom")?.GetComponent<PNDamagePopupBloom>();
        if (prefab == null) throw new System.InvalidOperationException("Missing popup Bloom prefab.");
        current = Instantiate(prefab); SceneManager.MoveGameObjectToScene(current.gameObject, camera.gameObject.scene);
        current.gameObject.layer = FirstLayer(data.volumeLayerMask.value);
        data.volumeLayerMask |= 1 << current.gameObject.layer;
        current.gameObject.SetActive(true);
        return current;
    }
    private static int FirstLayer(int mask)
    {
        for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
        return 0;
    }
    private void Update() { if (!EveRegistrySession.IsReady) Destroy(gameObject); }
    private void OnDestroy() { if (current == this) current = null; }
}
#endif
