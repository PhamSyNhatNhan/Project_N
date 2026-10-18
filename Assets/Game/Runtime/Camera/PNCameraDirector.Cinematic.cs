#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEngine;

// Cinematic: a director-owned rig above the world rig and the lock-on rig. Plays one shot or a sequence (fixed pose, follow, waypoint path, optional
// aim), blends in and out through a director-owned blender asset created at runtime with the shots' blend styles, ends on its duration, on
// release, when its target is lost, or when the view changes world, and reports each shot start and the end through the handle. Yaw stays 0 unless
// a shot opts in.
public sealed partial class PNCameraDirector
{
    private sealed class CinematicRecord
    {
        public PNCinematicSequence Sequence; public PNCinematicHandle Handle;
        public int Index; public float ShotAge; public bool Started;
        public Vector3 PrevPosition; public Quaternion PrevRotation; public float PrevFov, PrevOrtho; public bool HavePrev;
        public Vector3 LastPosition; public Quaternion LastRotation; public float LastFov, LastOrtho;
        public PNCinematicShot Shot => Sequence.Shots[Index];
    }
    private readonly PNCameraRequestStack<CinematicRecord> cinematics = new PNCameraRequestStack<CinematicRecord>(8);
    private GameObject cinematicObject;
    private CinemachineCamera cinematicRig;
    private CinemachineBlenderSettings blender;
    private CinematicRecord activeCinematic;
    private bool cinematicLive;
    private float cinematicBaseFov, cinematicBaseOrtho;
    // Cinemachine 6.6 CinemachineBlenderSettings.kBlendFromAnyCameraLabel (internal constant, read from the package source).
    private const string AnyCameraLabel = "**ANY CAMERA**";

    public bool CinematicActive => cinematicLive;
    public bool YawOverridden => cinematicLive && activeCinematic != null && activeCinematic.Shot.AllowYaw || OrbitYawOverridden;
    public CinemachineVirtualCameraBase CinematicRig => cinematicRig;
    public CinemachineBlenderSettings Blender => blender;
    public int CinematicShotIndex => cinematicLive && activeCinematic != null ? activeCinematic.Index : -1;

    public IPNCameraHandle RequestCinematic(UnityEngine.Object owner, string key, PNCinematicShot shot)
    {
        if (shot.HoldInput) return new PNRefusedCameraHandle(PNCameraRefusal.NotSupported);
        PNCinematicSequence sequence;
        try { sequence = new PNCinematicSequence(new[] { shot }); } catch (Exception) { return new PNRefusedCameraHandle(PNCameraRefusal.InvalidParameter); }
        return RequestCinematicSequence(owner, key, sequence);
    }

    public IPNCameraHandle RequestCinematicSequence(UnityEngine.Object owner, string key, PNCinematicSequence sequence)
    {
        if (released || output == null || viewed == null || reserved == 0) return new PNRefusedCameraHandle(PNCameraRefusal.NoCamera);
        if (owner == null) return new PNRefusedCameraHandle(PNCameraRefusal.OwnerGone);
        if (string.IsNullOrEmpty(key) || key.Length > 128 || sequence == null || sequence.Shots.Count == 0) return new PNRefusedCameraHandle(PNCameraRefusal.InvalidParameter);
        foreach (var shot in sequence.Shots)
        {
            if (shot.HoldInput) return new PNRefusedCameraHandle(PNCameraRefusal.NotSupported);
            if (!shot.AllowYaw && Mathf.Abs(Mathf.DeltaAngle(0f, shot.Euler.y)) > .001f) return new PNRefusedCameraHandle(PNCameraRefusal.InvalidParameter);
            if (shot.FollowsTarget && (shot.Target == null || !local.Contains(shot.Target))) return new PNRefusedCameraHandle(PNCameraRefusal.OtherWorld);
            if (!shot.FollowsTarget && !shot.HasPath && shot.Position == default && shot.Euler == default && shot.FieldOfView == 0f && shot.OrthographicSize == 0f && shot.Duration == 0f && !shot.HasAim) return new PNRefusedCameraHandle(PNCameraRefusal.InvalidParameter);
        }
        var handle = new PNCinematicHandle();
        try { cinematics.Request(owner, key, sequence.Shots[0].Priority, viewed.InstanceId, new CinematicRecord { Sequence = sequence, Handle = handle }, handle); return handle; }
        catch (InvalidOperationException) { return new PNRefusedCameraHandle(PNCameraRefusal.LimitReached); }
    }

    private void EnsureCinematicRig()
    {
        if (cinematicObject != null) return;
        cinematicObject = new GameObject("PN Camera Cinematic Rig"); cinematicObject.transform.SetParent(transform, false);
        cinematicRig = cinematicObject.AddComponent<CinemachineCamera>();
        cinematicRig.Priority.Value = 200;
        cinematicObject.SetActive(false);
        blender = ScriptableObject.CreateInstance<CinemachineBlenderSettings>(); blender.name = "PN cinematic blends";
        blender.CustomBlends = new CinemachineBlenderSettings.CustomBlend[0];
    }

    private void SetBlends(float blendIn, PNCameraEase easeIn, float blendOut, PNCameraEase easeOut)
    {
        blender.CustomBlends = new[]
        {
            new CinemachineBlenderSettings.CustomBlend { From = AnyCameraLabel, To = cinematicRig.name, Blend = new CinemachineBlendDefinition(blendIn <= 0 ? CinemachineBlendDefinition.Styles.Cut : StyleOf(easeIn), blendIn) },
            new CinemachineBlenderSettings.CustomBlend { From = cinematicRig.name, To = AnyCameraLabel, Blend = new CinemachineBlendDefinition(blendOut <= 0 ? CinemachineBlendDefinition.Styles.Cut : StyleOf(easeOut), blendOut) }
        };
        brain.CustomBlends = blender;
    }

    // Pose of a shot `age` seconds in. Aim is pitch-only unless the shot allows yaw.
    private void EvaluateShot(PNCinematicShot shot, float age, out Vector3 position, out Quaternion rotation)
    {
        if (shot.HasPath) { var p = PNCameraPath.Sample(shot.Path, shot.Duration > 0f ? age / shot.Duration : 0f, shot.SmoothPath); position = new Vector3(p.X, p.Y, p.Z); }
        else if (shot.FollowsTarget && shot.Target != null) position = shot.Target.position + shot.Offset;
        else if (shot.FollowsTarget) position = lastCinematicPosition;   // the target is gone; the shot is about to be released
        else position = shot.Position;
        if (shot.HasAim && shot.AimTarget != null)
        {
            var toTarget = shot.AimTarget.position + shot.AimOffset - position;
            if (toTarget.sqrMagnitude < 1e-8f) rotation = Quaternion.Euler(shot.Euler);
            else if (shot.AllowYaw) rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            else { float horizontal = Mathf.Sqrt(toTarget.x * toTarget.x + toTarget.z * toTarget.z); rotation = Quaternion.Euler(Mathf.Clamp(Mathf.Atan2(-toTarget.y, horizontal) * Mathf.Rad2Deg, -89f, 89f), 0f, 0f); }
        }
        else rotation = Quaternion.Euler(shot.Euler);
    }
    private Vector3 lastCinematicPosition;

    private void ShotLens(PNCinematicShot shot, out float fov, out float ortho)
    {
        fov = shot.FieldOfView > 0f && projection != PNCameraProjection.Orthographic ? shot.FieldOfView : cinematicBaseFov;
        ortho = shot.OrthographicSize > 0f && projection == PNCameraProjection.Orthographic ? shot.OrthographicSize : cinematicBaseOrtho;
    }

    private void StartCinematic(CinematicRecord record)
    {
        EnsureCinematicRig();
        cinematicBaseFov = output.fieldOfView; cinematicBaseOrtho = output.orthographicSize;
        var shots = record.Sequence.Shots; var first = shots[0]; var last = shots[shots.Count - 1];
        record.Index = 0; record.ShotAge = 0f; record.HavePrev = false;
        EvaluateShot(first, 0f, out var position, out var rotation); lastCinematicPosition = position;
        cinematicObject.transform.SetPositionAndRotation(position, rotation);
        cinematicRig.Lens = LensSettings.FromCamera(output);
        cinematicRig.Lens.ModeOverride = projection == PNCameraProjection.Orthographic ? LensSettings.OverrideModes.Orthographic : LensSettings.OverrideModes.Perspective;
        ShotLens(first, out var fov, out var ortho); cinematicRig.Lens.FieldOfView = fov; cinematicRig.Lens.OrthographicSize = ortho;
        record.LastPosition = position; record.LastRotation = rotation; record.LastFov = fov; record.LastOrtho = ortho;
        SetBlends(first.BlendIn, first.EaseIn, last.BlendOut, last.EaseOut);
        cinematicObject.SetActive(true);
        cinematicRig.OutputChannel = reserved; cinematicRig.Priority.Value = 200; EnsureListener(cinematicRig);
        admitted[cinematicRig] = new Admission { Vcam = cinematicRig, Channel = OutputChannels.Default, Owned = true };
        cinematicLive = true; activeCinematic = record;
        record.Started = true; record.Handle.NotifyShotStarted(0);
    }

    // immediate: cut back (view changed world, session ending). Otherwise the Brain blends out with the last shot's blend-out time and style.
    private void StopCinematic(bool immediate)
    {
        if (!cinematicLive) return;
        cinematicLive = false; activeCinematic = null;
        if (cinematicRig != null) Unadmit(cinematicRig);
        if (cinematicObject != null) cinematicObject.SetActive(false);
        if (immediate && brain != null) brain.ResetState();
    }

    private CinematicRecord ActiveCinematic()
    {
        cinematics.ReleaseWhere(e => !ReferenceEquals(viewed, local?.World), PNCameraHandleEnd.WorldChanged);
        cinematics.ReleaseWhere(e => { foreach (var shot in e.Payload.Sequence.Shots) if (shot.FollowsTarget && (shot.Target == null || !local.Contains(shot.Target))) return true; return false; }, PNCameraHandleEnd.TargetLost);
        return cinematics.Winner()?.Payload;
    }

    private void UpdateCinematic(float deltaTime, float unscaledDeltaTime)
    {
        var record = ActiveCinematic();
        if (record == null) { StopCinematic(false); return; }
        if (cinematicLive && !ReferenceEquals(activeCinematic, record)) StopCinematic(true);
        if (!cinematicLive) StartCinematic(record);
        var shot = record.Shot;
        record.ShotAge += Age(shot.Clock, deltaTime, unscaledDeltaTime);
        // Next shot / completion.
        if (shot.Duration > 0f && record.ShotAge >= shot.Duration)
        {
            if (record.Index + 1 < record.Sequence.Shots.Count)
            {
                record.PrevPosition = record.LastPosition; record.PrevRotation = record.LastRotation; record.PrevFov = record.LastFov; record.PrevOrtho = record.LastOrtho; record.HavePrev = true;
                record.ShotAge = Mathf.Max(0f, record.ShotAge - shot.Duration); record.Index++; shot = record.Shot;
                record.Handle.NotifyShotStarted(record.Index);
            }
            else
            {
                cinematics.ReleaseWhere(e => ReferenceEquals(e.Payload, record), PNCameraHandleEnd.Completed);
                if (cinematics.Winner() == null) { StopCinematic(false); return; }
                record = cinematics.Winner().Payload; if (!ReferenceEquals(activeCinematic, record)) { StopCinematic(true); StartCinematic(record); shot = record.Shot; }
            }
        }
        EvaluateShot(shot, record.ShotAge, out var position, out var rotation); ShotLens(shot, out var fov, out var ortho);
        // Between shots of a sequence the next shot blends in from where the previous one ended.
        if (record.HavePrev && shot.BlendIn > 0f && record.ShotAge < shot.BlendIn)
        {
            float k = PNCameraEasing.Evaluate(shot.EaseIn, record.ShotAge / shot.BlendIn);
            position = Vector3.Lerp(record.PrevPosition, position, k); rotation = Quaternion.Slerp(record.PrevRotation, rotation, k); fov = Mathf.Lerp(record.PrevFov, fov, k); ortho = Mathf.Lerp(record.PrevOrtho, ortho, k);
        }
        record.LastPosition = position; record.LastRotation = rotation; record.LastFov = fov; record.LastOrtho = ortho; lastCinematicPosition = position;
        cinematicObject.transform.SetPositionAndRotation(position, rotation);
        cinematicRig.Lens.FieldOfView = fov; cinematicRig.Lens.OrthographicSize = ortho;
    }

    private void ReleaseCinematicSession()
    {
        cinematics.Clear(); StopCinematic(true);
        if (brain != null && blender != null && brain.CustomBlends == blender) brain.CustomBlends = null;
        if (cinematicObject != null) Kill(cinematicObject);
        if (blender != null) Kill(blender);
        cinematicObject = null; cinematicRig = null; blender = null;
    }
}
#endif
