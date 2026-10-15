#if UNITY_5_3_OR_NEWER
using System;
using EveEngine;

// Internal opt-in hook; the fixture owns storage, limits and subscription lifetime.
internal static class PNDamagePopupDiagnostics
{
    internal static Action<string, PNDamagePopupPool, PNDamagePopup> Observe;
    internal static void Record(string operation, PNDamagePopupPool pool, PNDamagePopup popup = null)
    {
        if (EveHotPathMetrics.Active) Observe?.Invoke(operation, pool, popup);
    }
}
#endif
