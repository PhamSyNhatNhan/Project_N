#if UNITY_5_3_OR_NEWER
using UnityEngine;

// EN: The size of a summon on a machine that did not make it (SpawnedObjectSyncPlan 4.8): the host's PNSummon multiplies the object's local scale by the owner's summon_scale when it is made, and the
//     metadata of the entity carries that factor, so a client gives the same factor to its copy with this component. Like PNSummon it gives the prefab size back when the object is disabled or
//     returned to its pool. It holds nothing else: no owner, no behavior.
// VI: Cỡ của summon trên máy không tạo ra nó: metadata của thực thể mang hệ số summon_scale của chủ, client áp lại bằng component này và trả cỡ prefab khi object bị tắt / về pool.
[DisallowMultipleComponent]
public sealed class PNScaleView : MonoBehaviour
{
    private Vector3 baseScale = Vector3.one;
    private bool applied;
    public float Factor { get; private set; } = 1f;
    // Sets the factor (1 = unchanged). A bad factor (not finite, zero or negative) is taken as 1.
    public void Apply(float factor)
    {
        Clear();
        if (float.IsNaN(factor) || float.IsInfinity(factor) || factor <= 0f || Mathf.Abs(factor - 1f) < 1e-4f) return;
        baseScale = transform.localScale; transform.localScale = baseScale * factor; Factor = factor; applied = true;
    }
    // Gives the prefab size back (a pooled object being returned; OnDisable does it too).
    public void Clear()
    {
        if (!applied) { Factor = 1f; return; }
        transform.localScale = baseScale; applied = false; Factor = 1f;
    }
    private void OnDisable() => Clear();
    private void OnDestroy() => Clear();
}
#endif
