using UnityEngine;

// EN: Pitch-only facing for paper sprites (plus yaw only while a cinematic with AllowYaw is live); gameplay roots and floor shadows stay independent.
//     The math and the per-frame loop now live in PNCameraFacing / PNCameraFacingSystem (shared with every camera-facing visual); this component keeps Eve's
//     serialized fields (viewCamera, footOffset, footAnchor) and AlignToCamera, and registers with the shared loop while playing.
// VI: Bu goc nghieng camera cho sprite; root gameplay va bong san khong bi xoay. Toan va vong lap dung chung nam o PNCameraFacing / PNCameraFacingSystem.
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class ErModeCharacterBillboard : MonoBehaviour, IPNCameraFacing
{
    [SerializeField] private Camera viewCamera;
    [SerializeField, Min(0f)] private float footOffset;
    [SerializeField] private Vector3 footAnchor;

    private void OnEnable() { if (Application.isPlaying) PNCameraFacingSystem.Register(this); AlignToCamera(); }
    private void OnDisable() => PNCameraFacingSystem.Unregister(this);
    // Edit Mode only: in play the shared system aligns after Brain wrote the camera pose.
    private void LateUpdate() { if (!Application.isPlaying) AlignToCamera(); }

    bool IPNCameraFacing.Align() => AlignToCamera();

    public bool AlignToCamera()
        => PNCameraFacing.Align(transform, viewCamera, PNFacingMode.PitchYaw, footOffset, footAnchor, Application.isPlaying);
}
