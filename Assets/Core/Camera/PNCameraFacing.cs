using System;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// EN: How a camera-facing visual follows the view camera. Pitch: stand up to the camera's tilt only (the paper-sprite look; yaw stays 0).
//     PitchYaw: the same, and also turn with the camera while a yaw override is live (an opted-in cinematic or an orbit).
// VI: Cách một visual quay về phía camera. Pitch: chỉ nghiêng theo camera. PitchYaw: nghiêng và quay theo yaw khi camera đang bị ghi đè yaw.
public enum PNFacingMode { Pitch = 0, PitchYaw = 1 }

// EN: Pure math of camera-facing (no Unity types, so the standalone smoke suite checks it). The Unity half only turns these numbers into a rotation and a foot-anchored
//     position, shared by every camera-facing visual: Eve's billboard, the Visual actors and props such as trees. Facing never owns gameplay roots or floor shadows.
// VI: Toán thuần của việc quay về camera. Phần Unity chỉ đổi các số này thành xoay và vị trí neo chân, dùng chung cho mọi visual hướng camera.
public static class PNCameraFacing
{
    public const float MaxFootOffset = 100f;

    // Camera tilt in degrees from its forward vector: 0 looking at the horizon, positive looking down. A non-finite or zero vector is 0.
    public static float PitchDegrees(float forwardX, float forwardY, float forwardZ)
    {
        if (float.IsNaN(forwardX) || float.IsNaN(forwardY) || float.IsNaN(forwardZ) ||
            float.IsInfinity(forwardX) || float.IsInfinity(forwardY) || float.IsInfinity(forwardZ)) return 0f;
        float horizontal = (float)Math.Sqrt(forwardX * forwardX + forwardZ * forwardZ);
        return (float)(Math.Atan2(-forwardY, horizontal) * (180.0 / Math.PI));
    }

    // Yaw to apply: only PitchYaw follows it, and only while the camera service reports a live yaw override.
    public static float YawDegrees(PNCameraOrientation orientation, PNFacingMode mode)
        => mode == PNFacingMode.PitchYaw && orientation.YawOverridden && !float.IsNaN(orientation.Yaw) && !float.IsInfinity(orientation.Yaw) ? orientation.Yaw : 0f;

    public static bool IsDefined(PNFacingMode mode) => Enum.IsDefined(typeof(PNFacingMode), mode);
    public static bool IsValidFootOffset(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= MaxFootOffset;

#if UNITY_5_3_OR_NEWER
    public static Quaternion Rotation(float pitchDegrees, float yawDegrees)
    {
        var pitch = Quaternion.AngleAxis(pitchDegrees, Vector3.right);
        return yawDegrees == 0f ? pitch : Quaternion.AngleAxis(yawDegrees, Vector3.up) * pitch;
    }

    // The visual turns around its authored foot anchor, not the middle of the sprite: the point `footOffset` below the pivot stays at `footAnchor` (parent space).
    public static Vector3 LocalPosition(Vector3 footAnchor, float footOffset, Vector3 localScale, Quaternion localRotation)
        => footAnchor - localRotation * Vector3.Scale(Vector3.down * footOffset, localScale);

    // The camera a facing visual follows: the play session's view camera, else the serialized one, else the scene's main camera.
    public static Camera ResolveCamera(Camera serialized, bool playing)
    {
        var service = playing ? PNViewCamera.Service : null;
        return service != null && service.ViewCamera != null ? service.ViewCamera : serialized != null ? serialized : PNViewCamera.Current;
    }

    // One alignment step. Only this transform's local rotation and position change; the root, physics and sprite flip are never touched.
    public static bool Align(Transform target, Camera serialized, PNFacingMode mode, float footOffset, Vector3 footAnchor, bool playing)
    {
        if (target == null) return false;
        var camera = ResolveCamera(serialized, playing);
        if (camera == null) return false;
        var forward = camera.transform.forward;
        var service = playing ? PNViewCamera.Service : null;
        float yaw = service != null ? YawDegrees(service.Orientation, mode) : 0f;
        var facing = Rotation(PitchDegrees(forward.x, forward.y, forward.z), yaw);
        target.localRotation = target.parent != null ? Quaternion.Inverse(target.parent.rotation) * facing : facing;
        target.localPosition = LocalPosition(footAnchor, footOffset, target.localScale, target.localRotation);
        return true;
    }
#endif
}
