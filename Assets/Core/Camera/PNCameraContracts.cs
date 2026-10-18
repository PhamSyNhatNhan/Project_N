using System;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

public enum PNCameraProjection { Perspective, Orthographic }

// What a camera rig can do; requests that need a missing capability fall back to the director's own rig or are refused.
[Flags]
public enum PNCameraRigCapabilities
{
    None = 0,
    Pose = 1,
    Follow = 2,
    DistanceZoom = 4,
    LensZoom = 8,
    GroupFraming = 16
}

// Current look direction of the view camera, for billboards and other camera-dependent visuals.
public readonly struct PNCameraOrientation
{
    public readonly float Pitch, Yaw;
    public readonly bool YawOverridden;
    public PNCameraOrientation(float pitch, float yaw, bool yawOverridden) { Pitch = pitch; Yaw = yaw; YawOverridden = yawOverridden; }
}

#if UNITY_5_3_OR_NEWER
// A component group handed from PNWorldPresentation to another owner. Release returns it with the current view visibility applied.
public interface IPNPresentationClaim
{
    bool Active { get; }
    void Release();
}

// The single source of the rendering camera for popups, buff visuals, UI and camera-facing visuals.
public interface IPNViewCameraService
{
    Camera ViewCamera { get; }
    PNCameraOrientation Orientation { get; }
    // Raised after the view camera pose/lens has been written for the frame (after Cinemachine's Brain update).
    event Action Updated;
}

public static class PNViewCamera
{
    private static IPNViewCameraService service;
    public static IPNViewCameraService Service => service;
    public static event Action Updated;
    // Outside a play session (lobby, legacy scenes) there is no service and callers fall back to Camera.main as before.
    public static Camera Current => service?.ViewCamera != null ? service.ViewCamera : Camera.main;
    public static PNCameraOrientation Orientation => service != null ? service.Orientation : DefaultOrientation(Camera.main);
    public static void Register(IPNViewCameraService value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (service != null && !ReferenceEquals(service, value)) throw new InvalidOperationException("A view camera service is already registered.");
        service = value;
    }
    public static void Unregister(IPNViewCameraService value) { if (ReferenceEquals(service, value)) service = null; }
    public static void RaiseUpdated() { try { Updated?.Invoke(); } catch (Exception error) { Debug.LogException(error); } }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { service = null; Updated = null; }
    private static PNCameraOrientation DefaultOrientation(Camera camera)
    {
        if (camera == null) return default;
        var euler = camera.transform.rotation.eulerAngles;
        return new PNCameraOrientation(euler.x, euler.y, false);
    }
}
#endif
