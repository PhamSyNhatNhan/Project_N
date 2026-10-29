#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;

// EN: Anything that turns to face the view camera. Align runs once per camera update and returns false when there is no camera yet.
// VI: Thứ gì quay về phía camera. Align chạy một lần mỗi lần camera cập nhật.
public interface IPNCameraFacing { bool Align(); }

// EN: The one loop that aligns every registered camera-facing visual, shared by characters, Visual actors and props (trees, objects). With a camera director
//     it runs on PNViewCamera.Updated, i.e. after Brain wrote the pose, so visuals follow the pose of the same frame (a default-order LateUpdate reads the previous frame).
//     Without a director (lobby, legacy scenes) a hidden runner ticks it in LateUpdate. It also publishes the camera tilt as shader globals for objects that turn in
//     their vertex shader instead (large crowds such as trees need no per-object CPU work).
// VI: Vòng lặp duy nhất căn mọi visual hướng camera. Có camera director thì chạy sau Brain (PNViewCamera.Updated); không có thì một runner ẩn chạy LateUpdate.
public static class PNCameraFacingSystem
{
    public const string PitchGlobal = "_PNCameraPitch", YawGlobal = "_PNCameraYaw";
    private static readonly List<IPNCameraFacing> targets = new List<IPNCameraFacing>();
    private static readonly int pitchId = Shader.PropertyToID(PitchGlobal), yawId = Shader.PropertyToID(YawGlobal);
    private static bool subscribed;
    private static GameObject runner;

    public static int Count => targets.Count;
    // Number of completed ticks (checks and diagnostics).
    public static int TickCount { get; private set; }
    // Targets that threw while aligning (each is also logged).
    public static int ErrorCount { get; private set; }

    public static void Register(IPNCameraFacing target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (targets.Contains(target)) return;
        targets.Add(target);
        if (!subscribed) { PNViewCamera.Updated += Tick; subscribed = true; }
        if (runner == null && Application.isPlaying) EnsureRunner();
    }

    public static void Unregister(IPNCameraFacing target)
    {
        if (target == null) return;
        int index = targets.IndexOf(target);
        if (index >= 0) targets.RemoveAt(index);
        if (targets.Count == 0) Shutdown();
    }

    // Aligns every target once. Safe if a target registers or unregisters while it runs; a throwing target is logged and the rest still align.
    public static void Tick()
    {
        TickCount++;
        PublishGlobals();
        for (int i = targets.Count - 1; i >= 0; i--)
        {
            if (i >= targets.Count) continue;
            try { targets[i].Align(); }
            catch (Exception error) { ErrorCount++; Debug.LogException(error); }
        }
    }

    private static void PublishGlobals()
    {
        var camera = PNCameraFacing.ResolveCamera(null, Application.isPlaying);
        if (camera == null) return;
        var forward = camera.transform.forward;
        var service = Application.isPlaying ? PNViewCamera.Service : null;
        Shader.SetGlobalFloat(pitchId, PNCameraFacing.PitchDegrees(forward.x, forward.y, forward.z));
        Shader.SetGlobalFloat(yawId, service != null ? PNCameraFacing.YawDegrees(service.Orientation, PNFacingMode.PitchYaw) : 0f);
    }

    private static void EnsureRunner()
    {
        runner = new GameObject("PNCameraFacingRunner") { hideFlags = HideFlags.HideAndDontSave };
        UnityEngine.Object.DontDestroyOnLoad(runner);
        runner.AddComponent<PNCameraFacingRunner>();
    }

    private static void Shutdown()
    {
        if (subscribed) { PNViewCamera.Updated -= Tick; subscribed = false; }
        if (runner != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(runner); else UnityEngine.Object.DestroyImmediate(runner); runner = null; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { targets.Clear(); subscribed = false; runner = null; TickCount = 0; ErrorCount = 0; }

    // Ticks the system in LateUpdate while no camera director owns the view (PNViewCamera.Updated never fires then).
    private sealed class PNCameraFacingRunner : MonoBehaviour
    {
        private void LateUpdate() { if (PNViewCamera.Service == null) Tick(); }
    }
}

// EN: A prop or Visual that stands up to the camera. Add it to the object that carries the sprite or mesh (its local rotation and position are driven); the footAnchor is
//     where the feet are in the parent's space and footOffset is the distance from the pivot to the feet along the object's down. Edit Mode keeps the same look.
// VI: Vật thể đứng thẳng đối diện camera. Gắn vào object mang sprite/mesh; footAnchor là điểm chân theo không gian cha.
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class PNCameraFacingTarget : MonoBehaviour, IPNCameraFacing
{
    [SerializeField] private Camera viewCamera;
    [SerializeField] private PNFacingMode mode = PNFacingMode.PitchYaw;
    [SerializeField, Min(0f)] private float footOffset;
    [SerializeField] private Vector3 footAnchor;

    public Camera ViewCamera { get => viewCamera; set => viewCamera = value; }
    public PNFacingMode Mode { get => mode; set => mode = PNCameraFacing.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value)); }
    public float FootOffset { get => footOffset; set => footOffset = PNCameraFacing.IsValidFootOffset(value) ? value : throw new ArgumentOutOfRangeException(nameof(value)); }
    public Vector3 FootAnchor { get => footAnchor; set => footAnchor = value; }

    private void OnEnable() { if (Application.isPlaying) PNCameraFacingSystem.Register(this); Align(); }
    private void OnDisable() => PNCameraFacingSystem.Unregister(this);
#if UNITY_EDITOR
    private void LateUpdate() { if (!Application.isPlaying) Align(); }
#endif
    public bool Align() => PNCameraFacing.Align(transform, viewCamera, mode, footOffset, footAnchor, Application.isPlaying);
}
#endif
