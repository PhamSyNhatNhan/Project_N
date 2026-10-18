#if UNITY_5_3_OR_NEWER
#pragma warning disable CS0618 // legacy FramingTransposer on the ErMode rig
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;

// State snapshot, bounds (confiner), follow tuning, free orbit and the Brain clock choice.
public sealed partial class PNCameraDirector
{
    private sealed class BoundsRecord { public PNCameraBounds Bounds; }
    private readonly PNCameraRequestStack<BoundsRecord> boundsRequests = new PNCameraRequestStack<BoundsRecord>(8);
    private readonly Dictionary<CinemachineVirtualCameraBase, CinemachineConfiner3D> confiners = new Dictionary<CinemachineVirtualCameraBase, CinemachineConfiner3D>();
    private GameObject boundsObject;
    private BoxCollider boundsCollider;

    private readonly PNCameraRequestStack<PNFollowTuning> tunings = new PNCameraRequestStack<PNFollowTuning>(8);

    private readonly PNCameraRequestStack<PNOrbitRequest> orbits = new PNCameraRequestStack<PNOrbitRequest>(4);
    private float orbitYaw, orbitPitch, orbitFromYaw, orbitFromPitch, orbitTargetYaw, orbitTargetPitch, orbitT = 1f, orbitBlend = .4f;
    private PNCameraEase orbitEase = PNCameraEase.EaseInOut;
    private bool orbitHasTarget;

    private bool brainClockOverridden, savedIgnoreTimeScale;

    public bool OrbitYawOverridden => Mathf.Abs(orbitYaw) > .01f;
    public float OrbitYaw => orbitYaw;
    public float OrbitPitch => orbitPitch;
    public bool BoundsActive => boundsObject != null && boundsObject.activeSelf;

    // ---- state ---------------------------------------------------------------------------------------------------------------------

    public PNCameraState State
    {
        get
        {
            if (released || output == null) return PNCameraState.None;
            var kind = PNCameraRigKind.None;
            var active = brain != null ? brain.ActiveVirtualCamera as CinemachineVirtualCameraBase : null;
            if (active != null)
            {
                if (active == cinematicRig) kind = PNCameraRigKind.Cinematic;
                else if (active == lockRig) kind = PNCameraRigKind.LockOn;
                else if (active == fallback) kind = PNCameraRigKind.Fallback;
                else if (active is CinemachineExternalCamera) kind = PNCameraRigKind.External;
                else kind = PNCameraRigKind.World;
            }
            var mode = covered ? PNCameraMode.Transition : viewed == null ? PNCameraMode.Background : PNCameraMode.Normal;
            return new PNCameraState(true, output.transform.position, output.transform.rotation, output.orthographic, output.fieldOfView, output.orthographicSize, kind, ViewCapabilities, mode,
                brain != null && brain.IsBlending, lockActive, cinematicLive, YawOverridden, distanceScale, lensScale, viewed != null ? viewed.InstanceId : Guid.Empty);
        }
    }

    // ---- bounds --------------------------------------------------------------------------------------------------------------------

    public IPNCameraHandle RequestBounds(UnityEngine.Object owner, string key, PNCameraBounds bounds)
    {
        if (released || output == null || viewed == null) return new PNRefusedCameraHandle(PNCameraRefusal.NoCamera);
        if (owner == null) return new PNRefusedCameraHandle(PNCameraRefusal.OwnerGone);
        if (string.IsNullOrEmpty(key) || key.Length > 128 || !(bounds.Size.x > 0f)) return new PNRefusedCameraHandle(PNCameraRefusal.InvalidParameter);
        if (!admitted.Keys.Any(v => v != null && v != cinematicRig)) return new PNRefusedCameraHandle(PNCameraRefusal.NotSupported);
        try { return boundsRequests.Request(owner, key, 0, viewed.InstanceId, new BoundsRecord { Bounds = bounds }); }
        catch (InvalidOperationException) { return new PNRefusedCameraHandle(PNCameraRefusal.LimitReached); }
    }

    private void UpdateBounds()
    {
        boundsRequests.OwnerAlive = owner => !(owner is UnityEngine.Object o) || o != null;
        var winner = boundsRequests.Winner();
        foreach (var pair in confiners.ToArray())
            if (pair.Key == null || pair.Value == null || !admitted.ContainsKey(pair.Key) || pair.Key == cinematicRig || winner == null) { if (pair.Value != null) Kill(pair.Value); confiners.Remove(pair.Key); }
        if (winner == null) { if (boundsObject != null) boundsObject.SetActive(false); return; }
        if (boundsObject == null)
        {
            boundsObject = new GameObject("PN Camera Bounds"); boundsObject.transform.SetParent(transform, false); boundsObject.layer = 2;   // Ignore Raycast
            boundsCollider = boundsObject.AddComponent<BoxCollider>(); boundsCollider.isTrigger = true;
        }
        boundsObject.SetActive(true);
        boundsObject.transform.position = winner.Payload.Bounds.Center; boundsCollider.size = winner.Payload.Bounds.Size;
        foreach (var vcam in admitted.Keys.ToArray())
        {
            if (vcam == null || vcam == cinematicRig || confiners.ContainsKey(vcam)) continue;
            var confiner = vcam.gameObject.AddComponent<CinemachineConfiner3D>();
            confiner.BoundingVolume = boundsCollider; confiner.SlowingDistance = 0f;
            confiners[vcam] = confiner;
        }
    }

    private void ReleaseBoundsSession()
    {
        boundsRequests.Clear();
        foreach (var confiner in confiners.Values) if (confiner != null) Kill(confiner);
        confiners.Clear();
        if (boundsObject != null) Kill(boundsObject);
        boundsObject = null; boundsCollider = null;
    }

    // ---- follow tuning -------------------------------------------------------------------------------------------------------------

    public IPNCameraHandle RequestFollowTuning(UnityEngine.Object owner, string key, PNFollowTuning tuning)
    {
        if (released || output == null || viewed == null) return new PNRefusedCameraHandle(PNCameraRefusal.NoCamera);
        if (owner == null) return new PNRefusedCameraHandle(PNCameraRefusal.OwnerGone);
        if (string.IsNullOrEmpty(key) || key.Length > 128 || tuning == null) return new PNRefusedCameraHandle(PNCameraRefusal.InvalidParameter);
        try { tuning.Validate(); } catch (Exception) { return new PNRefusedCameraHandle(PNCameraRefusal.InvalidParameter); }
        if ((ViewCapabilities & PNCameraRigCapabilities.Follow) == 0) return new PNRefusedCameraHandle(PNCameraRefusal.NotSupported);
        try { return tunings.Request(owner, key, 0, viewed.InstanceId, tuning); }
        catch (InvalidOperationException) { return new PNRefusedCameraHandle(PNCameraRefusal.LimitReached); }
    }

    private void UpdateFollowTuning()
    {
        tunings.OwnerAlive = owner => !(owner is UnityEngine.Object o) || o != null;
        var winner = tunings.Winner()?.Payload;
        foreach (var pair in admitted)
        {
            if (pair.Value.Owned) continue;
            var rig = ZoomOf(pair.Key);
            if (rig == null || rig.Composer == null && rig.Legacy == null) continue;
            if (winner == null) { RestoreTuning(rig); continue; }
            if (!rig.Tuned) CaptureTuning(rig);
            ApplyTuning(rig, winner);
        }
    }

    private static void CaptureTuning(RigZoom rig)
    {
        rig.Tuned = true;
        if (rig.Composer != null)
        {
            var c = rig.Composer;
            rig.AuthoredOffset = c.TargetOffset; rig.AuthoredDamping = c.Damping; rig.AuthoredDeadZone = c.Composition.DeadZone.Size; rig.AuthoredDeadZoneEnabled = c.Composition.DeadZone.Enabled;
            rig.AuthoredLookahead = c.Lookahead.Time; rig.AuthoredLookaheadEnabled = c.Lookahead.Enabled;
        }
        else if (rig.Legacy != null)
        {
            var l = rig.Legacy;
            rig.AuthoredOffset = l.m_TrackedObjectOffset; rig.AuthoredDamping = new Vector3(l.m_XDamping, l.m_YDamping, l.m_ZDamping); rig.AuthoredDeadZone = new Vector2(l.m_DeadZoneWidth, l.m_DeadZoneHeight);
            rig.AuthoredDeadZoneEnabled = l.m_DeadZoneWidth > 0f || l.m_DeadZoneHeight > 0f; rig.AuthoredLookahead = l.m_LookaheadTime; rig.AuthoredLookaheadEnabled = l.m_LookaheadTime > 0f;
        }
    }

    private static void ApplyTuning(RigZoom rig, PNFollowTuning t)
    {
        var offset = t.TargetOffset ?? rig.AuthoredOffset; var damping = t.Damping ?? rig.AuthoredDamping; var dead = t.DeadZone ?? rig.AuthoredDeadZone;
        float look = t.LookaheadTime ?? rig.AuthoredLookahead; bool deadEnabled = t.DeadZone.HasValue || rig.AuthoredDeadZoneEnabled; bool lookEnabled = t.LookaheadTime.HasValue ? t.LookaheadTime.Value > 0f : rig.AuthoredLookaheadEnabled;
        if (rig.Composer != null)
        {
            var c = rig.Composer; c.TargetOffset = offset; c.Damping = damping;
            var composition = c.Composition; composition.DeadZone.Enabled = deadEnabled; composition.DeadZone.Size = dead; c.Composition = composition;
            var lookahead = c.Lookahead; lookahead.Enabled = lookEnabled; lookahead.Time = look; c.Lookahead = lookahead;
        }
        else if (rig.Legacy != null)
        {
            var l = rig.Legacy; l.m_TrackedObjectOffset = offset; l.m_XDamping = damping.x; l.m_YDamping = damping.y; l.m_ZDamping = damping.z;
            l.m_DeadZoneWidth = deadEnabled ? dead.x : 0f; l.m_DeadZoneHeight = deadEnabled ? dead.y : 0f; l.m_LookaheadTime = lookEnabled ? look : 0f;
        }
    }

    private static void RestoreTuning(RigZoom rig)
    {
        if (!rig.Tuned) return;
        rig.Tuned = false;
        if (rig.Composer != null)
        {
            var c = rig.Composer; c.TargetOffset = rig.AuthoredOffset; c.Damping = rig.AuthoredDamping;
            var composition = c.Composition; composition.DeadZone.Enabled = rig.AuthoredDeadZoneEnabled; composition.DeadZone.Size = rig.AuthoredDeadZone; c.Composition = composition;
            var lookahead = c.Lookahead; lookahead.Enabled = rig.AuthoredLookaheadEnabled; lookahead.Time = rig.AuthoredLookahead; c.Lookahead = lookahead;
        }
        else if (rig.Legacy != null)
        {
            var l = rig.Legacy; l.m_TrackedObjectOffset = rig.AuthoredOffset; l.m_XDamping = rig.AuthoredDamping.x; l.m_YDamping = rig.AuthoredDamping.y; l.m_ZDamping = rig.AuthoredDamping.z;
            l.m_DeadZoneWidth = rig.AuthoredDeadZone.x; l.m_DeadZoneHeight = rig.AuthoredDeadZone.y; l.m_LookaheadTime = rig.AuthoredLookahead;
        }
    }

    // ---- orbit -------------------------------------------------------------------------------------------------------------------------

    public IPNCameraHandle RequestOrbit(UnityEngine.Object owner, string key, PNOrbitRequest request)
    {
        if (released || output == null || viewed == null) return new PNRefusedCameraHandle(PNCameraRefusal.NoCamera);
        if (owner == null) return new PNRefusedCameraHandle(PNCameraRefusal.OwnerGone);
        if (string.IsNullOrEmpty(key) || key.Length > 128 || request.BlendSeconds < 0f) return new PNRefusedCameraHandle(PNCameraRefusal.InvalidParameter);
        if ((ViewCapabilities & PNCameraRigCapabilities.Follow) == 0) return new PNRefusedCameraHandle(PNCameraRefusal.NotSupported);
        try { return orbits.Request(owner, key, 0, viewed.InstanceId, request); }
        catch (InvalidOperationException) { return new PNRefusedCameraHandle(PNCameraRefusal.LimitReached); }
    }

    private void UpdateOrbit(float deltaTime, float unscaledDeltaTime)
    {
        orbits.OwnerAlive = owner => !(owner is UnityEngine.Object o) || o != null;
        var winner = orbits.Winner();
        float targetYaw = winner != null ? winner.Payload.Yaw : 0f, targetPitch = winner != null ? winner.Payload.Pitch : 0f;
        var ease = winner != null ? winner.Payload.Ease : orbitEase; float blend = winner != null ? winner.Payload.BlendSeconds : orbitBlend;
        var clock = winner != null ? winner.Payload.Clock : PNCameraClock.Camera;
        if (!orbitHasTarget || !Mathf.Approximately(targetYaw, orbitTargetYaw) || !Mathf.Approximately(targetPitch, orbitTargetPitch))
        { orbitFromYaw = orbitYaw; orbitFromPitch = orbitPitch; orbitT = 0f; orbitTargetYaw = targetYaw; orbitTargetPitch = targetPitch; orbitEase = ease; orbitBlend = blend; orbitHasTarget = true; }
        float dt = clock == PNCameraClock.Real ? unscaledDeltaTime : deltaTime;
        orbitT = blend <= 0f ? 1f : Mathf.Min(1f, orbitT + dt / blend);
        float k = PNCameraEasing.Evaluate(orbitEase, orbitT);
        orbitYaw = Mathf.Lerp(orbitFromYaw, orbitTargetYaw, k); orbitPitch = Mathf.Lerp(orbitFromPitch, orbitTargetPitch, k);
        if (orbitT >= 1f) { orbitYaw = orbitTargetYaw; orbitPitch = orbitTargetPitch; }
        bool active = Mathf.Abs(orbitYaw) > 1e-4f || Mathf.Abs(orbitPitch) > 1e-4f;
        foreach (var pair in admitted)
        {
            if (pair.Value.Owned || pair.Key == null) continue;
            var rig = ZoomOf(pair.Key);
            if (rig == null || rig.Composer == null && rig.Legacy == null) continue;
            if (active)
            {
                if (!rig.Orbited) { rig.Orbited = true; rig.AuthoredRotation = pair.Key.transform.rotation; }
                var euler = rig.AuthoredRotation.eulerAngles;
                pair.Key.transform.rotation = Quaternion.Euler(euler.x + orbitPitch, euler.y + orbitYaw, euler.z);
            }
            else RestoreOrbit(rig);
        }
    }

    private static void RestoreOrbit(RigZoom rig)
    {
        if (!rig.Orbited) return;
        rig.Orbited = false;
        if (rig.Vcam != null) rig.Vcam.transform.rotation = rig.AuthoredRotation;
    }

    // ---- brain clock --------------------------------------------------------------------------------------------------------------------

    // Blends of real-clock lock-ons and cinematics must keep running at time scale 0, which is a Brain-wide setting: set it only while needed.
    private void UpdateBrainClock()
    {
        bool need = lockActive && lockClock == PNCameraClock.Real || cinematicLive && activeCinematic != null && activeCinematic.Shot.Clock == PNCameraClock.Real;
        if (need && !brainClockOverridden) { savedIgnoreTimeScale = brain.IgnoreTimeScale; brain.IgnoreTimeScale = true; brainClockOverridden = true; }
        else if (!need && brainClockOverridden) { brain.IgnoreTimeScale = savedIgnoreTimeScale; brainClockOverridden = false; }
    }

    private void ForgetWorldExtras(Guid world)
    {
        boundsRequests.ReleaseWorld(world); tunings.ReleaseWorld(world); orbits.ReleaseWorld(world);
    }

    private void ReleaseExtrasSession()
    {
        ReleaseBoundsSession(); tunings.Clear(); orbits.Clear();
        foreach (var rig in rigZooms.Values) { RestoreTuning(rig); RestoreOrbit(rig); }
        orbitYaw = orbitPitch = 0f; orbitHasTarget = false; orbitT = 1f;
        if (brainClockOverridden && brain != null) brain.IgnoreTimeScale = savedIgnoreTimeScale;
        brainClockOverridden = false;
    }
}
#endif
