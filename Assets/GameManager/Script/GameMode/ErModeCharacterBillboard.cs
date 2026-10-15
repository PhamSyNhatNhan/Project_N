using UnityEngine;

// EN: Pitch-only facing for paper sprites; gameplay roots and floor shadows stay independent.
// VI: Bu goc nghieng camera cho sprite; root gameplay va bong san khong bi xoay.
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class ErModeCharacterBillboard : MonoBehaviour
{
    [SerializeField] private Camera viewCamera;
    [SerializeField, Min(0f)] private float footOffset;
    [SerializeField] private Vector3 footAnchor;

    private void OnEnable() => AlignToCamera();
    private void LateUpdate() => AlignToCamera();

    public bool AlignToCamera()
    {
        var camera = viewCamera != null ? viewCamera : Camera.main;
        if (camera == null) return false;
        var forward = camera.transform.forward;
        float horizontal = Mathf.Sqrt(forward.x * forward.x + forward.z * forward.z);
        float pitch = Mathf.Atan2(-forward.y, horizontal) * Mathf.Rad2Deg;
        var facing = Quaternion.AngleAxis(pitch, Vector3.right);
        transform.localRotation = transform.parent != null
            ? Quaternion.Inverse(transform.parent.rotation) * facing : facing;

        // EN: Rotate around the authored foot anchor, not the center of the sprite.
        // VI: Xoay quanh diem chan da cau hinh, khong xoay quanh tam sprite.
        var foot = Vector3.Scale(Vector3.down * footOffset, transform.localScale);
        transform.localPosition = footAnchor - transform.localRotation * foot;
        return true;
    }
}
