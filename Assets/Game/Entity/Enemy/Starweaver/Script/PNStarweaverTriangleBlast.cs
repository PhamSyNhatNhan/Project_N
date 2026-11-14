#if UNITY_5_3_OR_NEWER
using Newtonsoft.Json.Linq;
using UnityEngine;

// The Starweaver's constellation (registered `project_n:starweaver_triangle_blast`): a triangle Polygon Blast. It is created when the cast starts, so its telegraph (PNStarweaverTelegraph)
// and its detonation run on the same blast clock: a time effect that slows or stops the blast moves both. `blast_scale` scales the triangle and its telegraph together.
public sealed class PNStarweaverTriangleBlast : PNPolygonBlast
{
    private PNStarweaverTelegraph marker;
    protected override void OnBegin(JObject input) { marker?.Dispose(); marker = new PNStarweaverTelegraph(Center, Radius, gameObject.scene, Delay); }
    protected override void OnPhase(float time, float delay) { marker?.SetPhase(time / delay, time >= delay); }
    protected override void OnEnded() { marker?.Dispose(); marker = null; }
    public override void DisposePooledResources() { marker?.Dispose(); marker = null; }
}
#endif
