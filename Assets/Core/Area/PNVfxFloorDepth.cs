#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// A registered draw source for VFX-only depth. No pass is drawn into scene Z.
[ExecuteAlways, DisallowMultipleComponent]
public sealed class PNVfxFloorDepth : MonoBehaviour
{
    private static readonly HashSet<PNVfxFloorDepth> sources = new HashSet<PNVfxFloorDepth>();
    private MeshFilter mesh;
    private MeshRenderer rendererSource;
    public Mesh Mesh => mesh != null ? mesh.sharedMesh : null;
    public Material Material => rendererSource != null ? rendererSource.sharedMaterial : null;
    private void OnEnable() { mesh = GetComponent<MeshFilter>(); rendererSource = GetComponent<MeshRenderer>(); sources.Add(this); }
    private void OnDisable() => sources.Remove(this);
    private void OnDestroy() => sources.Remove(this);
    public static PNVfxFloorDepth[] ForCamera(Camera camera) => sources.Where(x => x != null && x.isActiveAndEnabled && x.rendererSource != null && x.rendererSource.enabled && x.Mesh != null && x.Material != null &&
        (camera.cullingMask & (1 << x.gameObject.layer)) != 0 && (!camera.scene.IsValid() || camera.scene == x.gameObject.scene)).ToArray();
}
#endif
