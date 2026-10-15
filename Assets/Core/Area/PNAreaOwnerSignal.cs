#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

// Runtime owner notification prevents disable/enable in one frame restoring old regions.
[DisallowMultipleComponent]
public sealed class PNAreaOwnerSignal : MonoBehaviour
{
    public event Action Disabled;
    private void OnDisable()
    {
        foreach (Action callback in Disabled?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { callback(); } catch (Exception error) { Debug.LogException(error, this); }
    }
}
#endif
