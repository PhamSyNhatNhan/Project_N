#if UNITY_5_3_OR_NEWER
#pragma warning disable CS0618 // the ErMode rig still uses the legacy FramingTransposer
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;

// Zoom: player base level + gameplay modifiers + (K4) cinematic override, composed from the base every frame, clamped by the profile,
// smoothed, and applied to the live rigs by capability (distance for composer rigs, lens scale for any other rig). Requests may be scales
// (relative to the authored rig) or real units (distance, field of view, orthographic size).
public sealed partial class PNCameraDirector
{
    private sealed class ZoomRecord { public PNZoomRequest Request; public float Age; }
    private sealed class RigZoom
    {
        public CinemachineVirtualCameraBase Vcam;
        public PNCameraRigCapabilities Capabilities;
        public float BaseDistance;
        public CinemachinePositionComposer Composer;
        public CinemachineFramingTransposer Legacy;
        public CinemachineRecomposer Recomposer;
        public bool AddedRecomposer;
        // Authored lens captured before any zoom scale was applied (lazily, when a lens-unit request first needs it).
        public float BaseFov, BaseOrtho;
        // Follow tuning: authored values to restore.
        public bool Tuned;
        public Vector3 AuthoredOffset, AuthoredDamping; public Vector2 AuthoredDeadZone; public bool AuthoredDeadZoneEnabled; public float AuthoredLookahead; public bool AuthoredLookaheadEnabled;
        // Orbit: authored rotation to restore.
        public bool Orbited; public Quaternion AuthoredRotation;
    }
    private readonly PNCameraProfileStack profiles = new PNCameraProfileStack();
    private readonly PNCameraRequestStack<ZoomRecord> zooms = new PNCameraRequestStack<ZoomRecord>(32);
    private readonly Dictionary<CinemachineVirtualCameraBase, RigZoom> rigZooms = new Dictionary<CinemachineVirtualCameraBase, RigZoom>();
    private float playerBaseZoom = 1f, distanceScale = 1f, lensScale = 1f;

    public PNCameraProfile Profile => profiles.Current;
    public float BaseZoom => playerBaseZoom;
    public float CurrentDistanceScale => distanceScale;
    public float CurrentLensScale => lensScale;
    public int ActiveZoomRequests { get { zooms.ReleaseWhere(e => e.Payload.Request.Duration > 0 && e.Payload.Age >= e.Payload.Request.Duration, PNCameraHandleEnd.Completed); return zooms.Count; } }

    public PNCameraRigCapabilities ViewCapabilities
    {
        get
        {
            var caps = PNCameraRigCapabilities.None;
            foreach (var rig in rigZooms.Values) caps |= rig.Capabilities;
            return caps;
        }
    }

    // Replaces one profile layer. An invalid layer throws and leaves the previous profile untouched.
    public void SetProfileLayer(PNCameraProfileSource source, PNCameraProfileLayer layer)
    {
        profiles.Set(source, layer);
        playerBaseZoom = Mathf.Clamp(playerBaseZoom, Mathf.Max(Profile.DistanceScaleMin, Profile.LensScaleMin), Mathf.Min(Profile.DistanceScaleMax, Profile.LensScaleMax));
    }

    // The player's own base level; always inside the profile range for both quantities.
    public float SetBaseZoom(float scale)
    {
        if (float.IsNaN(scale) || float.IsInfinity(scale)) throw new ArgumentOutOfRangeException(nameof(scale));
        var profile = Profile;
        playerBaseZoom = Mathf.Clamp(scale, Mathf.Max(profile.DistanceScaleMin, profile.LensScaleMin), Mathf.Min(profile.DistanceScaleMax, profile.LensScaleMax));
        return playerBaseZoom;
    }

    public IPNCameraHandle RequestZoom(object owner, string key, PNZoomRequest request)
    {
        if (released || output == null || viewed == null || rigZooms.Count == 0) return new PNRefusedCameraHandle(PNCameraRefusal.NoCamera);
        if (owner == null) return new PNRefusedCameraHandle(PNCameraRefusal.OwnerGone);
        if (owner is UnityEngine.Object unityOwner && unityOwner == null) return new PNRefusedCameraHandle(PNCameraRefusal.OwnerGone);
        if (string.IsNullOrEmpty(key) || key.Length > 128) return new PNRefusedCameraHandle(PNCameraRefusal.InvalidParameter);
        if (ViewCapabilities == PNCameraRigCapabilities.None) return new PNRefusedCameraHandle(PNCameraRefusal.NotSupported);
        if (!(request.Scale >= .1f)) return new PNRefusedCameraHandle(PNCameraRefusal.InvalidParameter);
        // Real-unit requests need a rig that has that quantity.
        if (request.Unit == PNZoomUnit.Distance && (ViewCapabilities & PNCameraRigCapabilities.DistanceZoom) == 0) return new PNRefusedCameraHandle(PNCameraRefusal.NotSupported);
        if (request.Unit == PNZoomUnit.FieldOfView && projection == PNCameraProjection.Orthographic) return new PNRefusedCameraHandle(PNCameraRefusal.NotSupported);
        if (request.Unit == PNZoomUnit.OrthographicSize && projection != PNCameraProjection.Orthographic) return new PNRefusedCameraHandle(PNCameraRefusal.NotSupported);
        try { return zooms.Request(owner, key, request.Priority, viewed.InstanceId, new ZoomRecord { Request = request }); }
        catch (InvalidOperationException) { return new PNRefusedCameraHandle(PNCameraRefusal.LimitReached); }
    }

    private RigZoom ZoomOf(CinemachineVirtualCameraBase vcam)
    {
        if (vcam == null) return null;
        if (rigZooms.TryGetValue(vcam, out var existing)) return existing;
        var rig = new RigZoom { Vcam = vcam, Capabilities = PNCameraRigCapabilities.Pose | PNCameraRigCapabilities.LensZoom };
        foreach (var component in vcam.GetComponentsInChildren<CinemachineComponentBase>(true))
        {
            if (component is CinemachinePositionComposer composer) { rig.Composer = composer; rig.BaseDistance = composer.CameraDistance; }
            else if (component is CinemachineFramingTransposer legacy) { rig.Legacy = legacy; rig.BaseDistance = legacy.m_CameraDistance; }
        }
        if (rig.Composer != null || rig.Legacy != null) rig.Capabilities |= PNCameraRigCapabilities.DistanceZoom | PNCameraRigCapabilities.Follow;
        rigZooms.Add(vcam, rig);
        return rig;
    }

    private void DropRigZoom(CinemachineVirtualCameraBase vcam)
    {
        if (vcam == null || !rigZooms.TryGetValue(vcam, out var rig)) return;
        rigZooms.Remove(vcam);
        try
        {
            // Hand the rig back exactly as it was authored.
            if (rig.Composer != null) rig.Composer.CameraDistance = rig.BaseDistance;
            if (rig.Legacy != null) rig.Legacy.m_CameraDistance = rig.BaseDistance;
            RestoreTuning(rig); RestoreOrbit(rig);
            if (rig.Recomposer != null) { if (rig.AddedRecomposer) Kill(rig.Recomposer); else rig.Recomposer.ZoomScale = 1f; }
        }
        catch (Exception error) { Debug.LogException(error); }
    }

    private void ForgetWorldZoom(Guid world) { zooms.ReleaseWorld(world); }

    // The world rig requests are resolved against: the live admitted rig that is not a director-owned one.
    private RigZoom LiveWorldRig()
    {
        if (brain != null && brain.ActiveVirtualCamera is CinemachineVirtualCameraBase active && admitted.TryGetValue(active, out var admission) && !admission.Owned && rigZooms.TryGetValue(active, out var live)) return live;
        foreach (var pair in admitted) if (!pair.Value.Owned && rigZooms.TryGetValue(pair.Key, out var any)) return any;
        return null;
    }

    private void CaptureLensBase(RigZoom rig)
    {
        if (rig.BaseFov > 0f && rig.BaseOrtho > 0f) return;
        float scale = rig.Recomposer != null && rig.Recomposer.ZoomScale > .0001f ? rig.Recomposer.ZoomScale : 1f;
        var lens = rig.Vcam.State.Lens;
        if (rig.BaseFov <= 0f) rig.BaseFov = Mathf.Max(1f, lens.FieldOfView / scale);
        if (rig.BaseOrtho <= 0f) rig.BaseOrtho = Mathf.Max(.1f, lens.OrthographicSize / scale);
    }

    private static float Age(PNCameraClock clock, float scaled, float unscaled) => clock == PNCameraClock.Real ? unscaled : scaled;

    private void UpdateZoom(float deltaTime, float unscaledDeltaTime)
    {
        if (released || output == null) return;
        var profile = Profile;
        foreach (var entry in zooms.Entries.ToArray()) entry.Payload.Age += Age(entry.Payload.Request.Clock, deltaTime, unscaledDeltaTime);
        zooms.ReleaseWhere(e => e.Payload.Request.Duration > 0 && e.Payload.Age >= e.Payload.Request.Duration, PNCameraHandleEnd.Completed);
        var live = LiveWorldRig();
        if (live != null) CaptureLensBase(live);
        var distanceTerms = new List<PNZoomComposer.Term>(); var lensTerms = new List<PNZoomComposer.Term>(); var explicitLens = false;
        foreach (var entry in zooms.Entries)
        {
            var request = entry.Payload.Request;
            float weight = PNZoomComposer.Weight(entry.Payload.Age, request.Duration, request.Blend, request.Ease);
            switch (request.Unit)
            {
                case PNZoomUnit.Scale:
                    distanceTerms.Add(new PNZoomComposer.Term(request, weight, entry.Sequence)); lensTerms.Add(new PNZoomComposer.Term(request, weight, entry.Sequence)); break;
                case PNZoomUnit.Distance:
                    if (live != null && live.BaseDistance > 0f) distanceTerms.Add(new PNZoomComposer.Term(request.AsScale(live.BaseDistance), weight, entry.Sequence)); break;
                case PNZoomUnit.FieldOfView:
                    if (live != null) { lensTerms.Add(new PNZoomComposer.Term(request.AsScale(live.BaseFov), weight, entry.Sequence)); explicitLens = true; } break;
                case PNZoomUnit.OrthographicSize:
                    if (live != null) { lensTerms.Add(new PNZoomComposer.Term(request.AsScale(live.BaseOrtho), weight, entry.Sequence)); explicitLens = true; } break;
            }
        }
        float targetDistance = PNZoomComposer.Target(playerBaseZoom, distanceTerms, null, 0f, PNZoomQuantity.Distance, profile);
        float targetLens = PNZoomComposer.Target(playerBaseZoom, lensTerms, null, 0f, PNZoomQuantity.Lens, profile);
        distanceScale = PNZoomComposer.Smooth(distanceScale, targetDistance, deltaTime, profile.SmoothSpeed);
        lensScale = PNZoomComposer.Smooth(lensScale, targetLens, deltaTime, profile.SmoothSpeed);
        foreach (var vcam in admitted.Keys.ToArray())
        {
            if (vcam == cinematicRig) continue;
            var rig = ZoomOf(vcam);
            if (rig == null) continue;
            bool distance = (rig.Capabilities & PNCameraRigCapabilities.DistanceZoom) != 0;
            if (distance)
            {
                float value = rig.BaseDistance * distanceScale;
                if (rig.Composer != null) rig.Composer.CameraDistance = value;
                if (rig.Legacy != null) rig.Legacy.m_CameraDistance = value;
            }
            // Lens scale: the only quantity on lens-only rigs; on composer rigs only explicit field-of-view / orthographic-size requests use it.
            if (!distance || explicitLens || rig.Recomposer != null)
            {
                float scale = distance ? (explicitLens ? ExplicitLensScale(lensTerms, profile) : 1f) : lensScale;
                if (Mathf.Abs(scale - 1f) > 1e-4f || rig.Recomposer != null) ApplyLensScale(rig, vcam, scale);
            }
        }
    }

    // On composer rigs the scale terms already moved the camera; only the explicit lens-unit terms zoom the lens.
    private float ExplicitLensScale(List<PNZoomComposer.Term> terms, PNCameraProfile profile)
    {
        var explicitTerms = terms.Where(t => t.Request.Absolute).ToArray();
        return PNZoomComposer.Target(1f, explicitTerms, null, 0f, PNZoomQuantity.Lens, profile);
    }

    private void ApplyLensScale(RigZoom rig, CinemachineVirtualCameraBase vcam, float scale)
    {
        if (rig.Recomposer == null)
        {
            rig.Recomposer = vcam.GetComponent<CinemachineRecomposer>();
            if (rig.Recomposer == null) { rig.Recomposer = vcam.gameObject.AddComponent<CinemachineRecomposer>(); rig.AddedRecomposer = true; rig.Recomposer.ZoomScale = 1f; rig.Recomposer.Tilt = 0; rig.Recomposer.Pan = 0; rig.Recomposer.Dutch = 0; rig.Recomposer.FollowAttachment = 1f; rig.Recomposer.LookAtAttachment = 1f; rig.Recomposer.ApplyAfter = CinemachineCore.Stage.Finalize; }
        }
        rig.Recomposer.ZoomScale = scale;
    }

    private void Update() => Advance(Time.deltaTime, Time.unscaledDeltaTime);

    // Per-frame zoom, lock-on, cinematic, follow and orbit step. Public so edit-mode checks can drive it without a player loop.
    // unscaledDeltaTime < 0 means "same as deltaTime" (edit-mode checks).
    public void Advance(float deltaTime, float unscaledDeltaTime = -1f)
    {
        if (released || output == null) return;
        if (unscaledDeltaTime < 0f) unscaledDeltaTime = deltaTime;
        AdvanceShakes(deltaTime, unscaledDeltaTime);
        UpdateZoom(deltaTime, unscaledDeltaTime);
        UpdateFollowTuning();
        UpdateOrbit(deltaTime, unscaledDeltaTime);
        UpdateLockOn(deltaTime, unscaledDeltaTime);
        UpdateCinematic(deltaTime, unscaledDeltaTime);
        UpdateBounds();
        UpdateBrainClock();
    }
}
#endif
