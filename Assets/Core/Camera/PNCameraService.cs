using System;
using System.Collections.Generic;
using EveEngine;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

public enum PNShakeShape { Sine = 0, Rumble = 1, Recoil = 2 }
public enum PNShakeFalloff { Linear = 0, Quadratic = 1, None = 2 }

// Shake request data. Plain values validated on construction; out-of-range values are refused, never silently clamped.
// Amplitude is the peak displacement in world units along Direction (normalized internally). Radius 0 = same everywhere; otherwise the
// amplitude is attenuated by the source's distance from the viewed actor (evaluated when the request is made).
public readonly struct PNShakeSpec
{
    public const float HardMaximumAmplitude = 10f, HardMaximumFrequency = 120f, HardMaximumDuration = 15f, HardMaximumRadius = 1000f;
    public readonly float Amplitude, Frequency, Duration, FadeOut, FadeIn, DirectionX, DirectionY, DirectionZ, Radius;
    public readonly PNShakeShape Shape;
    public readonly PNShakeFalloff Falloff;
    public readonly PNCameraClock Clock;
    public PNShakeSpec(float amplitude, float frequency, float duration, float fadeOut = 0.1f, float fadeIn = 0f, PNShakeShape shape = PNShakeShape.Sine,
        float directionX = 1f, float directionY = 1f, float directionZ = 0f, float radius = 0f, PNShakeFalloff falloff = PNShakeFalloff.Linear, PNCameraClock clock = PNCameraClock.Camera)
    {
        if (!(amplitude > 0) || amplitude > HardMaximumAmplitude || float.IsNaN(amplitude) || float.IsInfinity(amplitude)) throw new ArgumentOutOfRangeException(nameof(amplitude));
        if (!(frequency > 0) || frequency > HardMaximumFrequency || float.IsNaN(frequency) || float.IsInfinity(frequency)) throw new ArgumentOutOfRangeException(nameof(frequency));
        if (!(duration > 0) || duration > HardMaximumDuration || float.IsNaN(duration) || float.IsInfinity(duration)) throw new ArgumentOutOfRangeException(nameof(duration));
        if (!(fadeOut >= 0) || float.IsNaN(fadeOut) || float.IsInfinity(fadeOut)) throw new ArgumentOutOfRangeException(nameof(fadeOut));
        if (!(fadeIn >= 0) || float.IsNaN(fadeIn) || float.IsInfinity(fadeIn) || fadeIn + fadeOut > duration) throw new ArgumentOutOfRangeException(nameof(fadeIn));
        if (float.IsNaN(directionX + directionY + directionZ) || float.IsInfinity(directionX + directionY + directionZ) || directionX * directionX + directionY * directionY + directionZ * directionZ < 1e-8f) throw new ArgumentException("Shake direction must be finite and non-zero.");
        if (float.IsNaN(radius) || radius < 0f || radius > HardMaximumRadius || float.IsInfinity(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!Enum.IsDefined(typeof(PNShakeShape), shape) || !Enum.IsDefined(typeof(PNShakeFalloff), falloff) || !PNCameraEasing.IsDefined(clock)) throw new ArgumentOutOfRangeException(nameof(shape));
        Amplitude = amplitude; Frequency = frequency; Duration = duration; FadeOut = fadeOut; FadeIn = fadeIn; Shape = shape; Falloff = falloff; Clock = clock; Radius = radius;
        DirectionX = directionX; DirectionY = directionY; DirectionZ = directionZ;
    }
    // Multiplier 0..1 for a source `distance` away from the viewed actor.
    public float Attenuation(float distance)
    {
        if (Radius <= 0f || Falloff == PNShakeFalloff.None) return 1f;
        if (float.IsNaN(distance) || distance >= Radius) return 0f;
        if (distance <= 0f) return 1f;
        float k = 1f - distance / Radius;
        return Falloff == PNShakeFalloff.Quadratic ? k * k : k;
    }
    // Waveform value -1..1 at normalized time t (0..1 across the duration), before the fade-in/out envelope.
    public float Wave(float t)
    {
        t = t < 0f ? 0f : t > 1f ? 1f : t;
        float phase = 2f * (float)Math.PI * Frequency * Duration * t;
        switch (Shape)
        {
            case PNShakeShape.Rumble: return .5f * (float)Math.Sin(phase) + .3f * (float)Math.Sin(1.7f * phase + 1.3f) + .2f * (float)Math.Sin(2.9f * phase + 2.1f);
            case PNShakeShape.Recoil: { float k = 1f - t; return k * k * (float)Math.Sin(phase); }
            default: return (float)Math.Sin(phase);
        }
    }
}

public enum PNCameraRigKind { None = 0, World = 1, External = 2, Fallback = 3, LockOn = 4, Cinematic = 5 }
public enum PNCameraMode { None = 0, Normal = 1, Transition = 2, Background = 3 }

#if UNITY_5_3_OR_NEWER
// A complete cinematic shot: a fixed pose, a pose that follows a target at an offset, or a pose along a waypoint path, with optional aim
// at a target, a lens override, blend in/out styles and an opt-in yaw. Yaw stays 0 unless AllowYaw is set (billboards and shadows only change
// when it is; an aim target then only drives pitch). HoldInput is part of the contract so a shot can state it, but input gating is not
// implemented in the camera: a shot asking for it is refused with NotSupported.
public readonly struct PNCinematicShot
{
    public const int MaximumPathPoints = PNCameraPath.MaximumPoints;
    public readonly Vector3 Position;
    public readonly Vector3 Euler;
    public readonly Transform Target;
    public readonly Vector3 Offset;
    public readonly Transform AimTarget;
    public readonly Vector3 AimOffset;
    public readonly PNPoint[] Path;
    public readonly bool SmoothPath;
    public readonly float FieldOfView, OrthographicSize, Duration, BlendIn, BlendOut;
    public readonly PNCameraEase EaseIn, EaseOut;
    public readonly PNCameraClock Clock;
    public readonly bool AllowYaw, HoldInput, IsFollow, IsAim;
    public readonly int Priority;
    private PNCinematicShot(Vector3 position, Vector3 euler, Transform target, Vector3 offset, Transform aim, Vector3 aimOffset, PNPoint[] path, bool smoothPath, float fov, float ortho, float duration,
        float blendIn, float blendOut, PNCameraEase easeIn, PNCameraEase easeOut, PNCameraClock clock, bool allowYaw, bool holdInput, int priority, bool follow)
    {
        Check(position); Check(euler); Check(offset); Check(aimOffset);
        if (float.IsNaN(fov) || fov < 0f || fov > 170f || float.IsNaN(ortho) || ortho < 0f || ortho > 1000f) throw new ArgumentOutOfRangeException(nameof(fov));
        if (fov > 0f && ortho > 0f) throw new ArgumentException("A shot sets either a field of view or an orthographic size, not both.");
        if (float.IsNaN(duration) || duration < 0f || duration > 600f) throw new ArgumentOutOfRangeException(nameof(duration));
        if (float.IsNaN(blendIn) || blendIn < 0f || blendIn > PNCameraProfile.HardTimeMaximum || float.IsNaN(blendOut) || blendOut < 0f || blendOut > PNCameraProfile.HardTimeMaximum) throw new ArgumentOutOfRangeException(nameof(blendIn));
        if (!PNCameraEasing.IsDefined(easeIn) || !PNCameraEasing.IsDefined(easeOut) || !PNCameraEasing.IsDefined(clock)) throw new ArgumentOutOfRangeException(nameof(easeIn));
        if (path != null)
        {
            if (path.Length < 2 || path.Length > MaximumPathPoints) throw new ArgumentException("A path needs 2.." + MaximumPathPoints + " points.");
            foreach (var point in path) if (!point.IsFinite) throw new ArgumentException("Path points must be finite.");
            if (!(duration > 0f)) throw new ArgumentException("A path shot needs a duration.");
            path = (PNPoint[])path.Clone();
        }
        Position = position; Euler = euler; Target = target; Offset = offset; AimTarget = aim; AimOffset = aimOffset; Path = path; SmoothPath = smoothPath; FieldOfView = fov; OrthographicSize = ortho; Duration = duration;
        BlendIn = blendIn; BlendOut = blendOut; EaseIn = easeIn; EaseOut = easeOut; Clock = clock; AllowYaw = allowYaw; HoldInput = holdInput; Priority = priority; IsFollow = follow; IsAim = aim != null;
    }
    private static void Check(Vector3 v) { if (float.IsNaN(v.x + v.y + v.z) || float.IsInfinity(v.x + v.y + v.z)) throw new ArgumentException("Shot vectors must be finite."); }
    // Fixed world-space pose. Euler.y must be 0 unless allowYaw.
    public static PNCinematicShot Pose(Vector3 position, Vector3 euler, float fieldOfView = 0f, float orthographicSize = 0f, float duration = 0f, float blendIn = .5f, float blendOut = .5f, bool allowYaw = false, bool holdInput = false, int priority = 0,
        Transform aimTarget = null, Vector3 aimOffset = default, PNCameraEase easeIn = PNCameraEase.EaseInOut, PNCameraEase easeOut = PNCameraEase.EaseInOut, PNCameraClock clock = PNCameraClock.Camera)
        => new PNCinematicShot(position, euler, null, default, aimTarget, aimOffset, null, true, fieldOfView, orthographicSize, duration, blendIn, blendOut, easeIn, easeOut, clock, allowYaw, holdInput, priority, false);
    // The camera stays at target.position + offset with the given fixed orientation (or aimed at aimTarget).
    public static PNCinematicShot Follow(Transform target, Vector3 offset, Vector3 euler, float fieldOfView = 0f, float orthographicSize = 0f, float duration = 0f, float blendIn = .5f, float blendOut = .5f, bool allowYaw = false, bool holdInput = false, int priority = 0,
        Transform aimTarget = null, Vector3 aimOffset = default, PNCameraEase easeIn = PNCameraEase.EaseInOut, PNCameraEase easeOut = PNCameraEase.EaseInOut, PNCameraClock clock = PNCameraClock.Camera)
        => new PNCinematicShot(default, euler, target ?? throw new ArgumentNullException(nameof(target)), offset, aimTarget, aimOffset, null, true, fieldOfView, orthographicSize, duration, blendIn, blendOut, easeIn, easeOut, clock, allowYaw, holdInput, priority, true);
    // The camera travels the waypoints over `duration` (needs a duration), with a fixed orientation or aimed at aimTarget.
    public static PNCinematicShot AlongPath(PNPoint[] points, Vector3 euler, float duration, bool smooth = true, float fieldOfView = 0f, float orthographicSize = 0f, float blendIn = .5f, float blendOut = .5f, bool allowYaw = false, bool holdInput = false, int priority = 0,
        Transform aimTarget = null, Vector3 aimOffset = default, PNCameraEase easeIn = PNCameraEase.EaseInOut, PNCameraEase easeOut = PNCameraEase.EaseInOut, PNCameraClock clock = PNCameraClock.Camera)
        => new PNCinematicShot(default, euler, null, default, aimTarget, aimOffset, points ?? throw new ArgumentNullException(nameof(points)), smooth, fieldOfView, orthographicSize, duration, blendIn, blendOut, easeIn, easeOut, clock, allowYaw, holdInput, priority, false);
    // True for a follow shot even after its target was destroyed (Target itself then compares equal to null).
    public bool FollowsTarget => IsFollow;
    public bool HasAim => IsAim;
    public bool HasPath => Path != null;
}

// Shots played in order. Every shot except the last needs a duration; each next shot blends in over its own BlendIn with its EaseIn.
public sealed class PNCinematicSequence
{
    public const int MaximumShots = 16;
    public IReadOnlyList<PNCinematicShot> Shots { get; }
    public PNCinematicSequence(IEnumerable<PNCinematicShot> shots)
    {
        var list = new List<PNCinematicShot>(shots ?? throw new ArgumentNullException(nameof(shots)));
        if (list.Count < 1 || list.Count > MaximumShots) throw new ArgumentException("A sequence has 1.." + MaximumShots + " shots.");
        for (int i = 0; i < list.Count - 1; i++) if (!(list[i].Duration > 0f)) throw new ArgumentException("Every shot except the last needs a duration.");
        foreach (var shot in list) if (shot.HoldInput) throw new ArgumentException("HoldInput shots are not supported.");
        Shots = list.AsReadOnly();
    }
}

// Handle of a cinematic: also reports which shot of the sequence is playing.
public sealed class PNCinematicHandle : PNCameraHandle
{
    public int CurrentShot { get; private set; } = -1;
    public event Action<int> ShotStarted;
    // Called by the camera director when a shot starts; gameplay code only subscribes to ShotStarted.
    public void NotifyShotStarted(int index)
    {
        CurrentShot = index;
        var handlers = ShotStarted; if (handlers == null) return;
        foreach (Action<int> handler in handlers.GetInvocationList()) handler(index);
    }
}

// Box the camera must stay inside (world space), scoped to the viewed world.
public readonly struct PNCameraBounds
{
    public readonly Vector3 Center, Size;
    public PNCameraBounds(Vector3 center, Vector3 size)
    {
        if (float.IsNaN(center.x + center.y + center.z) || float.IsInfinity(center.x + center.y + center.z)) throw new ArgumentException("Bounds centre must be finite.");
        if (!(size.x > 0f && size.y > 0f && size.z > 0f) || size.x > 10000f || size.y > 10000f || size.z > 10000f) throw new ArgumentOutOfRangeException(nameof(size));
        Center = center; Size = size;
    }
}

// Follow-composition tuning for rigs that can follow (composer rigs). Unset (null) fields keep the rig's authored value; releasing restores it.
public sealed class PNFollowTuning
{
    public Vector3? TargetOffset;
    public Vector3? Damping;
    public Vector2? DeadZone;          // width, height as 0..1 of the screen
    public float? LookaheadTime;       // seconds
    public void Validate()
    {
        void Finite(Vector3 v, float min, float max) { if (float.IsNaN(v.x + v.y + v.z) || v.x < min || v.y < min || v.z < min || v.x > max || v.y > max || v.z > max) throw new ArgumentOutOfRangeException(); }
        if (TargetOffset.HasValue) Finite(TargetOffset.Value, -100f, 100f);
        if (Damping.HasValue) Finite(Damping.Value, 0f, 20f);
        if (DeadZone.HasValue && !(DeadZone.Value.x >= 0f && DeadZone.Value.x <= 1f && DeadZone.Value.y >= 0f && DeadZone.Value.y <= 1f)) throw new ArgumentOutOfRangeException();
        if (LookaheadTime.HasValue && !(LookaheadTime.Value >= 0f && LookaheadTime.Value <= 1f)) throw new ArgumentOutOfRangeException();
        if (!TargetOffset.HasValue && !Damping.HasValue && !DeadZone.HasValue && !LookaheadTime.HasValue) throw new ArgumentException("Tuning must set at least one field.");
    }
}

// Free orbit around the followed target: yaw and a bounded pitch offset from the authored view. Needs a rig that can follow. A non-zero
// yaw is a yaw override (billboards and shadows follow it while it lasts).
public readonly struct PNOrbitRequest
{
    public const float MaximumPitch = 30f;
    public readonly float Yaw, Pitch, BlendSeconds;
    public readonly PNCameraEase Ease;
    public readonly PNCameraClock Clock;
    public PNOrbitRequest(float yaw, float pitch = 0f, float blendSeconds = .4f, PNCameraEase ease = PNCameraEase.EaseInOut, PNCameraClock clock = PNCameraClock.Camera)
    {
        if (float.IsNaN(yaw) || float.IsInfinity(yaw) || yaw < -360f || yaw > 360f) throw new ArgumentOutOfRangeException(nameof(yaw));
        if (float.IsNaN(pitch) || pitch < -MaximumPitch || pitch > MaximumPitch) throw new ArgumentOutOfRangeException(nameof(pitch));
        if (float.IsNaN(blendSeconds) || blendSeconds < 0f || blendSeconds > PNCameraProfile.HardTimeMaximum) throw new ArgumentOutOfRangeException(nameof(blendSeconds));
        if (!PNCameraEasing.IsDefined(ease) || !PNCameraEasing.IsDefined(clock)) throw new ArgumentOutOfRangeException(nameof(ease));
        Yaw = yaw; Pitch = pitch; BlendSeconds = blendSeconds; Ease = ease; Clock = clock;
    }
}

// Snapshot of the view camera for gameplay and UI. Everything is read-only data.
public readonly struct PNCameraState
{
    public readonly bool Available;
    public readonly Vector3 Position;
    public readonly Quaternion Rotation;
    public readonly bool Orthographic;
    public readonly float FieldOfView, OrthographicSize;
    public readonly PNCameraRigKind Rig;
    public readonly PNCameraRigCapabilities Capabilities;
    public readonly PNCameraMode Mode;
    public readonly bool Blending, LockOnActive, CinematicActive, YawOverridden;
    public readonly float DistanceScale, LensScale;
    public readonly Guid ViewedWorld;
    public PNCameraState(bool available, Vector3 position, Quaternion rotation, bool orthographic, float fieldOfView, float orthographicSize, PNCameraRigKind rig, PNCameraRigCapabilities capabilities,
        PNCameraMode mode, bool blending, bool lockOn, bool cinematic, bool yawOverridden, float distanceScale, float lensScale, Guid viewedWorld)
    {
        Available = available; Position = position; Rotation = rotation; Orthographic = orthographic; FieldOfView = fieldOfView; OrthographicSize = orthographicSize; Rig = rig; Capabilities = capabilities;
        Mode = mode; Blending = blending; LockOnActive = lockOn; CinematicActive = cinematic; YawOverridden = yawOverridden; DistanceScale = distanceScale; LensScale = lensScale; ViewedWorld = viewedWorld;
    }
    public static PNCameraState None => new PNCameraState(false, default, Quaternion.identity, false, 0f, 0f, PNCameraRigKind.None, PNCameraRigCapabilities.None, PNCameraMode.None, false, false, false, false, 1f, 1f, Guid.Empty);
}
#endif

// Lock-on parameters. Negative values mean "use the profile"; everything else is validated.
public readonly struct PNLockOnRequest
{
    public readonly float Weight, Radius, FramingSize, MinimumDistance, MaximumDistance, Damping, BlendSeconds;
    public readonly PNCameraEase Ease;
    public readonly PNCameraClock Clock;
    public PNLockOnRequest(float weight = 1f, float radius = .5f, float framingSize = 0f, float minimumDistance = -1f, float maximumDistance = -1f, float damping = -1f, float blendSeconds = -1f,
        PNCameraEase ease = PNCameraEase.EaseInOut, PNCameraClock clock = PNCameraClock.Camera)
    {
        if (float.IsNaN(weight) || weight < .1f || weight > 10f) throw new ArgumentOutOfRangeException(nameof(weight));
        if (float.IsNaN(radius) || radius < 0f || radius > 50f) throw new ArgumentOutOfRangeException(nameof(radius));
        if (float.IsNaN(framingSize) || framingSize < 0f || framingSize > 1.5f || framingSize > 0f && framingSize < .05f) throw new ArgumentOutOfRangeException(nameof(framingSize));
        if (float.IsNaN(minimumDistance) || float.IsInfinity(minimumDistance) || minimumDistance > 1000f) throw new ArgumentOutOfRangeException(nameof(minimumDistance));
        if (float.IsNaN(maximumDistance) || float.IsInfinity(maximumDistance) || maximumDistance > 1000f) throw new ArgumentOutOfRangeException(nameof(maximumDistance));
        if (minimumDistance >= 0f && maximumDistance >= 0f && minimumDistance > maximumDistance) throw new ArgumentException("Lock-on minimum distance exceeds maximum.");
        if (float.IsNaN(damping) || float.IsInfinity(damping) || damping > 20f) throw new ArgumentOutOfRangeException(nameof(damping));
        if (float.IsNaN(blendSeconds) || float.IsInfinity(blendSeconds) || blendSeconds > PNCameraProfile.HardTimeMaximum) throw new ArgumentOutOfRangeException(nameof(blendSeconds));
        if (!PNCameraEasing.IsDefined(ease) || !PNCameraEasing.IsDefined(clock)) throw new ArgumentOutOfRangeException(nameof(ease));
        Weight = weight; Radius = radius; FramingSize = framingSize; MinimumDistance = minimumDistance; MaximumDistance = maximumDistance; Damping = damping; BlendSeconds = blendSeconds; Ease = ease; Clock = clock;
    }
    public static PNLockOnRequest Default => new PNLockOnRequest(1f);
}

#if UNITY_5_3_OR_NEWER
// Camera requests for gameplay code. Every request returns a handle; nothing throws for "no camera" (legacy scene, lobby, test), the handle
// is simply inactive with a refusal code. Requests are local to this player's view.
public interface IPNCameraService
{
    // False for the empty service.
    bool Available { get; }
    // Player-side multiplier applied on top of the profile gain (default 1, clamped 0..2).
    float PlayerShakeGain { get; set; }
    // Shake from a source object. Only sources in the world this player is viewing shake the camera (others are refused with OtherWorld).
    IPNCameraHandle RequestShake(UnityEngine.Object source, string key, PNShakeSpec spec);

    // Layered tuning (game -> mode -> world -> player). An invalid layer throws and changes nothing.
    PNCameraProfile Profile { get; }
    void SetProfileLayer(PNCameraProfileSource source, PNCameraProfileLayer layer);
    // What the live rig can do (distance zoom, lens zoom, follow ...); None when there is no camera.
    PNCameraRigCapabilities ViewCapabilities { get; }
    // The player's own base zoom (1 = as authored); the setter-style call returns the applied, clamped value.
    float BaseZoom { get; }
    float SetBaseZoom(float scale);
    // Gameplay zoom modifier with a handle; refused with NotSupported when no rig can zoom, NoCamera outside a session.
    IPNCameraHandle RequestZoom(object owner, string key, PNZoomRequest request);
    // Camera-only lock-on: frames the local actor and the target together with fixed pitch/yaw. Does not aim or rotate anything.
    IPNCameraHandle RequestLockOn(UnityEngine.Object owner, UnityEngine.Transform target, PNLockOnRequest request);
    // Local cinematic (this player's view only). Highest priority camera; releasing blends back to whatever was live before (world rig or lock-on).
    IPNCameraHandle RequestCinematic(UnityEngine.Object owner, string key, PNCinematicShot shot);
    // Shots in order; the returned handle (a PNCinematicHandle when accepted) reports ShotStarted and Ended.
    IPNCameraHandle RequestCinematicSequence(UnityEngine.Object owner, string key, PNCinematicSequence sequence);
    // Keep the camera inside a box for the viewed world (the latest request wins). Refused with NotSupported when the rig cannot be confined.
    IPNCameraHandle RequestBounds(UnityEngine.Object owner, string key, PNCameraBounds bounds);
    // Follow composition (offset, damping, dead zone, lookahead) for composer rigs; refused with NotSupported otherwise.
    IPNCameraHandle RequestFollowTuning(UnityEngine.Object owner, string key, PNFollowTuning tuning);
    // Free orbit/pitch around the followed target (composer rigs); a non-zero yaw is a yaw override.
    IPNCameraHandle RequestOrbit(UnityEngine.Object owner, string key, PNOrbitRequest request);
    // Current look direction for camera-facing visuals; YawOverridden is true only while a yaw override (cinematic AllowYaw or orbit) is live.
    PNCameraOrientation Orientation { get; }
    // Snapshot of the view camera (pose, lens, active rig, mode, capabilities, zoom). Not available outside a session.
    PNCameraState State { get; }
}

public sealed class PNNullCameraService : IPNCameraService
{
    public static readonly PNNullCameraService Instance = new PNNullCameraService();
    private static IPNCameraHandle Refuse() => new PNRefusedCameraHandle(PNCameraRefusal.NoCamera);
    public bool Available => false;
    public float PlayerShakeGain { get; set; } = 1f;
    public IPNCameraHandle RequestShake(UnityEngine.Object source, string key, PNShakeSpec spec) => Refuse();
    public PNCameraProfile Profile => PNCameraProfile.Default;
    public void SetProfileLayer(PNCameraProfileSource source, PNCameraProfileLayer layer) { }
    public PNCameraRigCapabilities ViewCapabilities => PNCameraRigCapabilities.None;
    public float BaseZoom => 1f;
    public float SetBaseZoom(float scale) => 1f;
    public IPNCameraHandle RequestZoom(object owner, string key, PNZoomRequest request) => Refuse();
    public IPNCameraHandle RequestLockOn(UnityEngine.Object owner, UnityEngine.Transform target, PNLockOnRequest request) => Refuse();
    public IPNCameraHandle RequestCinematic(UnityEngine.Object owner, string key, PNCinematicShot shot) => Refuse();
    public IPNCameraHandle RequestCinematicSequence(UnityEngine.Object owner, string key, PNCinematicSequence sequence) => Refuse();
    public IPNCameraHandle RequestBounds(UnityEngine.Object owner, string key, PNCameraBounds bounds) => Refuse();
    public IPNCameraHandle RequestFollowTuning(UnityEngine.Object owner, string key, PNFollowTuning tuning) => Refuse();
    public IPNCameraHandle RequestOrbit(UnityEngine.Object owner, string key, PNOrbitRequest request) => Refuse();
    public PNCameraOrientation Orientation => default;
    public PNCameraState State => PNCameraState.None;
}

public static class PNCameraServices
{
    private static IPNCameraService current;
    private static Guid session;
    // Outside a play session the empty service is returned so gameplay code needs no null checks.
    public static IPNCameraService Resolve(EveSessionContext value) => current != null && value != null && value.SessionId == session ? current : PNNullCameraService.Instance;
    public static IPNCameraService Current => current ?? PNNullCameraService.Instance;
    public static void Register(EveSessionContext value, IPNCameraService service)
    {
        if (value == null || service == null) throw new ArgumentNullException();
        if (current != null && !ReferenceEquals(current, service)) throw new InvalidOperationException("A camera service is already registered for the running session.");
        current = service; session = value.SessionId;
    }
    public static void Unregister(IPNCameraService service) { if (ReferenceEquals(current, service)) { current = null; session = Guid.Empty; } }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { current = null; session = Guid.Empty; }
}
#endif
