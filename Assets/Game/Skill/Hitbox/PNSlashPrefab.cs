using System;
using EveEngine;
using UnityEngine;

// Geometry is copied once per invocation. Preview never queries physics or delivers damage.
[ExecuteAlways]
public sealed class PNSlashPrefab : MonoBehaviour
{
    [Range(0f, 1f)] public float PreviewTime = .35f;
    [NonSerialized] public EveHitboxTrackData[] RuntimeTracks;
    public void SetPreviewTime(float time)
    {
        if (Application.isPlaying || RuntimeTracks != null) return;
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Scrub attack preview");
        foreach (var track in GetComponentsInChildren<EveHitboxTrack>(true))
            UnityEditor.Undo.RecordObject(track, "Scrub attack preview");
#endif
        PreviewTime = Mathf.Clamp01(time);
        RefreshPreview();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(this);
#endif
    }
    public EveHitboxTrackData[] Capture()
    {
        var regions = GetComponentsInChildren<EveHitboxTrack>(true);
        if (regions.Length == 0) throw new InvalidOperationException("Attack prefab requires at least one hitbox track.");
        var copies = new EveHitboxTrackData[regions.Length];
        for (int i = 0; i < copies.Length; i++) copies[i] = regions[i].Capture(transform);
        return copies;
    }
    public void RefreshPreview()
    {
        if (Application.isPlaying || RuntimeTracks != null) return;
        var visual = GetComponentInChildren<PNArcSlashVisual>(true);
        if (visual == null) return;
        visual.Tracks = Capture();
        visual.Render(PreviewTime);
        foreach (var track in GetComponentsInChildren<EveHitboxTrack>(true)) track.PreviewTime = PreviewTime;
    }
    private void OnDrawGizmosSelected()
    {
        try
        {
            var tracks = RuntimeTracks ?? Capture();
            float time = RuntimeTracks == null ? PreviewTime : GetComponentInChildren<PNArcSlashVisual>().Progress;
            foreach (var track in tracks)
            {
                var frame = track.Sample(time, new EveHitboxPose(transform.position, transform.rotation));
                var visual = GetComponentInChildren<PNArcSlashVisual>();
                bool damageWindow = RuntimeTracks == null || (time >= visual.HitStart && time <= visual.HitEnd);
                if (frame.Shape != null) EveHitboxGizmos.Draw(frame.Shape, frame.Pose, frame.Enabled && damageWindow ? Color.green : Color.gray);
            }
        }
        catch (ArgumentException) { }
    }
    private void OnEnable() { if (!Application.isPlaying) TryPreview(); }
    private void OnValidate() { if (!Application.isPlaying) TryPreview(); }
    private void TryPreview() { try { RefreshPreview(); } catch (Exception) { /* Inspector displays invalid authoring data. */ } }
}
