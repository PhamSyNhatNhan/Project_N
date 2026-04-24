using UnityEngine;

/// <summary>
/// Gắn trên GameManager — giữ flag ShouldContinue cho UIContextManager đọc.
/// </summary>
public class HallController : MonoBehaviour
{
    public static bool ShouldContinue { get; set; } = false;
}