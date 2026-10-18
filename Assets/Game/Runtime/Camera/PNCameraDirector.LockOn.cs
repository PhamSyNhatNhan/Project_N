#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;

// Lock-on: camera only. The group is the local actor plus the locked target with an eased weight. If the live world rig already has a
// CinemachineGroupFraming (world- or mod-authored) the lock-on drives that rig (only its Follow/LookAt are pointed at the group and restored
// afterwards, its authored framing settings are respected); otherwise the director's own rig (position composer + group framing) takes over with
// fixed pitch/yaw. It never rotates or aims the character.
public sealed partial class PNCameraDirector
{
    private sealed class LockRecord { public Transform Target; public PNLockOnRequest Request; public float Age; }
    private readonly PNCameraRequestStack<LockRecord> lockOns = new PNCameraRequestStack<LockRecord>(8);
    private GameObject lockObject, lockGroupObject;
    private CinemachineCamera lockRig;
    private CinemachinePositionComposer lockComposer;
    private CinemachineGroupFraming lockFraming;
    private CinemachineTargetGroup lockGroup;
    private Transform groupedTarget;
    private float lockWeight;               // 0..1 weight of the target inside the group (eases in and out)
    private bool lockActive;
    private CinemachineBlendDefinition savedBlend;
    private bool blendSaved;
    private PNCameraClock lockClock;
    private PNCameraEase lockEase = PNCameraEase.EaseInOut;
    private CinemachineVirtualCameraBase lockWorldRig;   // set while a world rig with GroupFraming is driven instead of the director's rig

    public bool LockOnActive => lockActive;
    public Transform LockedTarget => lockActive ? groupedTarget : null;
    public float LockOnWeight => lockWeight;
    // The rig doing the framing: the director's, or the world's own GroupFraming rig.
    public CinemachineVirtualCameraBase LockOnRig => lockActive ? (lockWorldRig != null ? lockWorldRig : lockRig) : lockRig;
    public bool LockOnUsesWorldRig => lockActive && lockWorldRig != null;

    public IPNCameraHandle RequestLockOn(UnityEngine.Object owner, Transform target, PNLockOnRequest request)
    {
        if (released || output == null || viewed == null || local?.Actor == null) return new PNRefusedCameraHandle(PNCameraRefusal.NoCamera);
        if (owner == null || target == null) return new PNRefusedCameraHandle(PNCameraRefusal.OwnerGone);
        if (!local.Contains(target) || !local.Contains(local.Actor)) return new PNRefusedCameraHandle(PNCameraRefusal.OtherWorld);
        if (ReferenceEquals(target, local.Actor.transform)) return new PNRefusedCameraHandle(PNCameraRefusal.InvalidParameter);
        if (reserved == 0) return new PNRefusedCameraHandle(PNCameraRefusal.NotSupported);
        if (!(request.Weight >= .1f)) return new PNRefusedCameraHandle(PNCameraRefusal.InvalidParameter);
        try { return lockOns.Request(owner, "lock-on", 0, viewed.InstanceId, new LockRecord { Target = target, Request = request }); }
        catch (InvalidOperationException) { return new PNRefusedCameraHandle(PNCameraRefusal.LimitReached); }
    }

    private LockRecord ActiveLockOn()
    {
        // Drop requests whose target vanished, left the viewed world, or whose actor is gone.
        lockOns.ReleaseWhere(e => e.Payload.Target == null || local?.Actor == null || !local.Contains(e.Payload.Target) || !ReferenceEquals(viewed, local.World), PNCameraHandleEnd.TargetLost);
        return lockOns.Winner()?.Payload;
    }

    private void EnsureLockRig()
    {
        if (lockObject != null) return;
        // The target group lives outside the (inactive) rig object so it keeps updating when a world rig is the one driven.
        lockGroupObject = new GameObject("PN Camera Lock-on Group"); lockGroupObject.transform.SetParent(transform, false);
        lockGroup = lockGroupObject.AddComponent<CinemachineTargetGroup>();
        lockGroup.PositionMode = CinemachineTargetGroup.PositionModes.GroupCenter; lockGroup.RotationMode = CinemachineTargetGroup.RotationModes.Manual; lockGroup.UpdateMethod = CinemachineTargetGroup.UpdateMethods.LateUpdate;
        lockObject = new GameObject("PN Camera Lock-on Rig"); lockObject.transform.SetParent(transform, false);
        lockRig = lockObject.AddComponent<CinemachineCamera>();
        lockRig.Priority.Value = 100;
        lockRig.Follow = lockGroupObject.transform; lockRig.LookAt = lockGroupObject.transform;
        lockComposer = lockObject.AddComponent<CinemachinePositionComposer>();
        lockFraming = lockObject.AddComponent<CinemachineGroupFraming>();
        lockFraming.FramingMode = CinemachineGroupFraming.FramingModes.HorizontalAndVertical;
        lockObject.SetActive(false);
    }

    private CinemachineVirtualCameraBase WorldRigWithGroupFraming()
    {
        foreach (var pair in admitted)
            if (pair.Key != null && !pair.Value.Owned && pair.Key.GetComponent<CinemachineGroupFraming>() != null) return pair.Key;
        return null;
    }

    private void StartLockOn(LockRecord record)
    {
        EnsureLockRig();
        var profile = Profile;
        var actor = local.Actor.transform;
        lockGroup.Targets.Clear();
        lockGroup.AddMember(actor, 1f, .5f);
        groupedTarget = record.Target;
        lockGroup.AddMember(record.Target, 0f, record.Request.Radius);
        lockWeight = 0f; lockClock = record.Request.Clock; lockEase = record.Request.Ease;
        var worldRig = WorldRigWithGroupFraming();
        if (worldRig != null)
        {
            // Drive the world's own framing rig: only its targets change, restored in StopLockOn.
            lockWorldRig = worldRig; worldRig.Follow = lockGroupObject.transform; worldRig.LookAt = lockGroupObject.transform;
            lockActive = true; return;
        }
        lockObject.transform.SetPositionAndRotation(output.transform.position, output.transform.rotation);   // pitch/yaw stay exactly as they are
        lockRig.Lens = LensSettings.FromCamera(output);
        lockRig.Lens.ModeOverride = projection == PNCameraProjection.Orthographic ? LensSettings.OverrideModes.Orthographic : LensSettings.OverrideModes.Perspective;
        float baseDistance = Mathf.Max(1f, Vector3.Distance(output.transform.position, actor.position));
        lockComposer.CameraDistance = baseDistance;
        float min = record.Request.MinimumDistance >= 0 ? record.Request.MinimumDistance : profile.LockOnMinimumDistance;
        float max = record.Request.MaximumDistance >= 0 ? record.Request.MaximumDistance : profile.LockOnMaximumDistance;
        if (max < min) max = min;
        lockFraming.FramingSize = record.Request.FramingSize > 0 ? record.Request.FramingSize : profile.LockOnFramingSize;
        lockFraming.Damping = record.Request.Damping >= 0 ? record.Request.Damping : profile.LockOnDamping;
        lockFraming.SizeAdjustment = projection == PNCameraProjection.Orthographic ? CinemachineGroupFraming.SizeAdjustmentModes.ZoomOnly : CinemachineGroupFraming.SizeAdjustmentModes.DollyOnly;
        lockFraming.DollyRange = new Vector2(min - baseDistance, max - baseDistance);
        lockObject.SetActive(true);
        lockRig.OutputChannel = reserved; lockRig.Priority.Value = 100; EnsureListener(lockRig);
        admitted[lockRig] = new Admission { Vcam = lockRig, Channel = OutputChannels.Default, Owned = true };
        float seconds = record.Request.BlendSeconds >= 0 ? record.Request.BlendSeconds : profile.LockOnBlendSeconds;
        SetBrainBlend(seconds, record.Request.Ease);
        lockActive = true;
    }

    private void StopLockOn()
    {
        if (!lockActive) return;
        lockActive = false;
        if (lockWorldRig != null)
        {
            var actor = local?.Actor != null ? local.Actor.transform : null;
            lockWorldRig.Follow = actor; lockWorldRig.LookAt = actor; lockWorldRig = null;
        }
        else if (lockRig != null) Unadmit(lockRig);
        if (lockGroup != null) lockGroup.Targets.Clear();
        if (lockObject != null) lockObject.SetActive(false);
        groupedTarget = null; lockWeight = 0f;
        RestoreBrainBlend();
    }

    private static CinemachineBlendDefinition.Styles StyleOf(PNCameraEase ease)
    {
        switch (ease)
        {
            case PNCameraEase.Cut: return CinemachineBlendDefinition.Styles.Cut;
            case PNCameraEase.Linear: return CinemachineBlendDefinition.Styles.Linear;
            case PNCameraEase.EaseIn: return CinemachineBlendDefinition.Styles.EaseIn;
            case PNCameraEase.EaseOut: return CinemachineBlendDefinition.Styles.EaseOut;
            case PNCameraEase.HardIn: return CinemachineBlendDefinition.Styles.HardIn;
            case PNCameraEase.HardOut: return CinemachineBlendDefinition.Styles.HardOut;
            default: return CinemachineBlendDefinition.Styles.EaseInOut;
        }
    }

    private void SetBrainBlend(float seconds, PNCameraEase ease)
    {
        if (!blendSaved) { savedBlend = brain.DefaultBlend; blendSaved = true; }
        brain.DefaultBlend = new CinemachineBlendDefinition(seconds <= 0f ? CinemachineBlendDefinition.Styles.Cut : StyleOf(ease), seconds);
    }
    private void RestoreBrainBlend() { if (blendSaved) { brain.DefaultBlend = savedBlend; blendSaved = false; } }

    private void UpdateLockOn(float deltaTime, float unscaledDeltaTime)
    {
        var record = ActiveLockOn();
        if (record == null)
        {
            if (!lockActive) return;
            // Ease the target out of the group, then hand the camera back to the world's rig.
            float blend = Mathf.Max(.01f, Profile.LockOnBlendSeconds);
            float dtOut = lockClock == PNCameraClock.Real ? unscaledDeltaTime : deltaTime;
            lockWeight = Mathf.Max(0f, lockWeight - dtOut / blend);
            ApplyGroupWeight();
            if (lockWeight <= 0f) StopLockOn();
            return;
        }
        if (lockActive && !ReferenceEquals(groupedTarget, record.Target))
        {
            // Another request won: swap the target inside the running rig.
            lockGroup.Targets.RemoveAll(t => t.Object == groupedTarget); groupedTarget = record.Target; lockGroup.AddMember(record.Target, lockWeight, record.Request.Radius);
        }
        if (!lockActive) StartLockOn(record);
        float seconds = Mathf.Max(.01f, record.Request.BlendSeconds >= 0 ? record.Request.BlendSeconds : Profile.LockOnBlendSeconds);
        float dt = record.Request.Clock == PNCameraClock.Real ? unscaledDeltaTime : deltaTime;
        record.Age += dt;
        // The weight follows the eased 0..1 progress of the blend-in, scaled by the request's weight.
        lockWeight = Mathf.Min(record.Request.Weight, PNCameraEasing.Evaluate(record.Request.Ease, Mathf.Min(1f, record.Age / seconds)) * record.Request.Weight);
        ApplyGroupWeight();
    }

    private void ApplyGroupWeight()
    {
        if (lockGroup == null || groupedTarget == null) return;
        foreach (var member in lockGroup.Targets) if (member.Object == groupedTarget) member.Weight = lockWeight;
    }

    private void ReleaseLockOnSession()
    {
        lockOns.Clear();
        StopLockOn();
        if (lockObject != null) Kill(lockObject);
        if (lockGroupObject != null) Kill(lockGroupObject);
        lockObject = null; lockGroupObject = null; lockRig = null; lockComposer = null; lockFraming = null; lockGroup = null;
    }
}
#endif
