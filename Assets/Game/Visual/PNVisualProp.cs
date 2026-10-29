#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;
using UnityEngine;

// EN: A rig puppet placed in a scene by hand (the Hall's Starweaver): when the content session is ready it builds a PNVisualActor from the named appearance, standing with its feet at this
//     object's position, and plays one state. It is only a picture: no entity, no collider, no gameplay. The puppet is destroyed when this object is disabled and built again when enabled.
//     (Characters use PNVisualBody through the entity factory instead.)
// VI: Con rối rig đặt tay trong cảnh (Starweaver ở Hall): khi phiên nội dung sẵn sàng thì dựng PNVisualActor từ appearance đã đặt tên, chân tại vị trí object này, chạy một trạng thái. Chỉ là hình.
[DisallowMultipleComponent]
public sealed class PNVisualProp : MonoBehaviour
{
    [SerializeField] private string appearance = "project_n:starweaver";
    [SerializeField] private string state = "idle";
    [SerializeField] private bool facingLeft;

    private PNVisualActor actor; private bool failed;
    public PNVisualActor Actor => actor;

    private void Update()
    {
        if (actor != null || failed || !EveRegistrySession.IsReady) return;
        try { Build(EveRegistrySession.Current); }
        catch (Exception exception) { failed = true; Debug.LogError("PNVisualProp " + name + " could not build " + appearance + ": " + exception.Message, this); }
    }

    // Public so a native check can build it without a running session.
    public void Build(EveRegistryHub hub)
    {
        if (actor != null) return;
        var definition = hub.Get<PNVisualAppearanceDefinition>().Get(EveId.Parse(appearance));
        var child = new GameObject("PN Visual Actor"); child.transform.SetParent(transform, false);
        var created = child.AddComponent<PNVisualActor>();
        try { created.Bind(definition, hub, Vector3.zero); }
        catch { DestroyChild(child); throw; }
        created.SetFacingLeft(facingLeft); created.Play(state); actor = created;
    }

    private void OnDisable()
    {
        if (actor != null) { DestroyChild(actor.gameObject); actor = null; }
        failed = false;
    }

    private static void DestroyChild(GameObject child) { if (Application.isPlaying) Destroy(child); else DestroyImmediate(child); }
}
#endif
