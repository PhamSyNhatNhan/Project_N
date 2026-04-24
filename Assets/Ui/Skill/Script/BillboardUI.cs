using UnityEngine;

/// <summary>
/// Gắn lên World Space Canvas — giữ rotation luôn hướng về camera
/// Dùng cho SkillPanel, BuffPanel, EnemyBar gắn lên entity
/// </summary>
public class BillboardUI : MonoBehaviour
{
    private Camera mainCam;

    private void Awake() => mainCam = Camera.main;

    private void LateUpdate()
    {
        if (mainCam == null) return;
        transform.rotation = Quaternion.LookRotation(
            transform.position - mainCam.transform.position
        );
    }
}