#if UNITY_5_3_OR_NEWER
using UnityEngine;

// EN: Smooths what is DRAWN of a body whose world steps physics by hand (PNWorldPhysicsLoop): Unity does not interpolate Rigidbodies there, so the transform stands still on every
//     rendered frame without a physics step. The body, its colliders and gameplay are never moved: only what the bound callback places, the follow point of the camera and the readers of
//     `Offset` (ground shadow, puppet motion) are put between the last two steps (at most one step behind). A body with interpolation None (a skill drives it) and a body moved
//     since the last step have no offset.
// VI: Làm mượt phần HÌNH của body trong world chạy physics thủ công. Không dời body, collider hay gameplay.
[DefaultExecutionOrder(32100)]   // after PNWorldPhysicsLoop (32000) in FixedUpdate and after gameplay in Update; every LateUpdate (camera, shadow) sees the result
[DisallowMultipleComponent]
public sealed class PNPhysicsPose : MonoBehaviour
{
    public const float TeleportMetres = .5f;
    private const string FollowName = "PN Follow Point";

    private Rigidbody body;
    private System.Action<Vector3> place;
    private Transform follow;
    private Vector3 previous, current;
    private bool sampled;

    // World-space metres to add to the body's position to get where it is drawn this frame.
    public Vector3 Offset
    {
        get
        {
            if (!enabled || !sampled || body == null || body.interpolation == RigidbodyInterpolation.None) return Vector3.zero;
            if ((transform.position - current).sqrMagnitude > 1e-6f) return Vector3.zero;
            float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            return (previous - current) * (1f - alpha);
        }
    }

    public static Vector3 OffsetOf(Transform root) => root != null && root.TryGetComponent<PNPhysicsPose>(out var pose) ? pose.Offset : Vector3.zero;

    // What a camera should follow: the smoothed point of a body that has one, else the body itself.
    public static Transform FollowOf(Transform root)
    {
        if (root == null || !root.TryGetComponent<PNPhysicsPose>(out var pose)) return root;
        if (pose.follow == null) { pose.follow = new GameObject(FollowName).transform; pose.follow.SetParent(root, false); }
        return pose.follow;
    }

    // `drawn` receives this frame's offset in the body's local space and places the visual (its owner knows who else writes that transform).
    public void Bind(System.Action<Vector3> drawn)
    {
        Unbind();
        body = GetComponent<Rigidbody>(); place = drawn;
        enabled = true;
    }

    public void Unbind()
    {
        place?.Invoke(Vector3.zero);
        if (follow != null) follow.localPosition = Vector3.zero;
        place = null; sampled = false; enabled = false;
    }

    private void FixedUpdate()
    {
        var scene = gameObject.scene.GetPhysicsScene();
        if (scene == Physics.defaultPhysicsScene) { sampled = false; return; }   // there Unity interpolates by itself
        var position = transform.position;
        previous = sampled && (position - current).sqrMagnitude < TeleportMetres * TeleportMetres ? current : position;
        current = position; sampled = true;
    }

    private void Update()
    {
        var local = transform.InverseTransformVector(Offset);
        place?.Invoke(local);
        if (follow != null) follow.localPosition = local;
    }
}
#endif
