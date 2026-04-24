using UnityEngine;

public class ColliderWatcher : MonoBehaviour
{
    private CapsuleCollider2D col;
    private bool lastState;

    void Awake() => col = GetComponent<CapsuleCollider2D>();

    void Update()
    {
        if (col == null) return;
        if (col.enabled != lastState)
        {
            lastState = col.enabled;
            Debug.Log($"[ColliderWatcher] enabled changed to {col.enabled}", this);
            if (!col.enabled)
                Debug.Log(new System.Diagnostics.StackTrace().ToString());
        }
    }
}