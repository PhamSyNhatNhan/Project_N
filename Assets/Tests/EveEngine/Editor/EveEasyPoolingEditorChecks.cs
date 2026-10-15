using System;
using EveEngine;
using UnityEngine;

// EN: Opt-in checks against Unity's real ObjectPool; temporary objects are cleaned up afterwards.
// VI: Kiểm thử tường minh bằng ObjectPool Unity thật; object tạm được dọn sau khi chạy.
public static class EveEasyPoolingEditorChecks
{
    public static string Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Run pooling Editor checks outside Play Mode.");
        var prefab = new GameObject("Temporary pooling test prefab");
        prefab.hideFlags = HideFlags.HideAndDontSave;
        prefab.AddComponent<EveEasyPoolingProbe>();
        EveEasyPoolingProbe.Created = EveEasyPoolingProbe.Taken = EveEasyPoolingProbe.Released = EveEasyPoolingProbe.Enabled = 0;
        EveEasyPoolingProbe.FailGet = EveEasyPoolingProbe.FailRelease = false;
        var pool = new EveEasyPooling(prefab);
        try
        {
            var first = pool.Get(); var second = pool.Get();
            Check(first != second && !first.activeSelf && pool.CountActive == 2, "reserve inactive objects");
            Check(EveEasyPoolingProbe.Enabled == 0, "suppress active prefab OnEnable until setup");
            Check(EveEasyPoolingProbe.Created == 2 && EveEasyPoolingProbe.Taken == 2, "component hooks");
            first.GetComponent<EveEasyPoolingProbe>().State = 42;
            first.SetActive(true);
            Check(EveEasyPoolingProbe.Enabled == 1, "caller activates after setup");
            first.SetActive(false);
            Check(pool.CountActive == 2, "disable is not release");
            Check(pool.Release(first) && !pool.Release(first), "duplicate release rejected");
            Check(EveEasyPoolingProbe.Released == 1 && first.GetComponent<EveEasyPoolingProbe>().State == 0, "reset hook");
            Check(pool.Get() == first && first.GetComponent<EveEasyPoolingProbe>().Pool == pool, "reuse and owner reference");
            UnityEngine.Object.DestroyImmediate(second);
            Check(pool.Release(second), "remove externally destroyed active object");
            pool.ReleaseAll();
            UnityEngine.Object.DestroyImmediate(first);
            var fresh = pool.Get();
            Check(fresh != null && pool.CountAll == 1, "skip externally destroyed idle object");
            pool.Clear();
            Check(fresh == null && pool.CountAll == 0, "Clear destroys outstanding objects");
            EveEasyPoolingProbe.FailGet = true;
            Reject(() => pool.Get(), "Get hook failure propagated");
            Check(pool.CountAll == 0, "failed Get evicts instance");
            EveEasyPoolingProbe.FailGet = false;
            pool.Get(); pool.Get();
            EveEasyPoolingProbe.FailRelease = true;
            Reject(() => pool.ReleaseAll(), "reset hook failure propagated");
            Check(pool.CountAll == 0, "ReleaseAll processes all failed resets");
            EveEasyPoolingProbe.FailRelease = false;
            int peakStart = EveEasyPoolingProbe.Created;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                for (int i = 0; i < 800; i++) pool.Get();
                pool.ReleaseAll();
            }
            Check(EveEasyPoolingProbe.Created - peakStart == 800 && pool.CountInactive == 800,
                "native Unity ObjectPool stabilizes over bullet hell bursts");
            using (var limited = new EveEasyPooling(prefab, maxSize: 2))
            {
                for (int i = 0; i < 10; i++) limited.Get();
                Check(limited.CountActive == 10, "idle maxSize does not limit spawns");
                limited.ReleaseAll();
                Check(limited.CountAll == 2, "native Unity idle capacity enforcement");
            }
            var outstanding = pool.Get();
            pool.Dispose(); pool.Dispose();
            Check(outstanding == null && pool.CountAll == 0 && !pool.Release(outstanding), "Dispose destroys idle and live objects");
            return "PASS: native Unity ObjectPool wrapper; Get/Release hooks; setup before enable; reuse; ownership; external destruction; failure cleanup; 3 x 800 bullet bursts stabilize; idle maxSize; Clear/Dispose.";
        }
        finally
        {
            EveEasyPoolingProbe.FailGet = EveEasyPoolingProbe.FailRelease = false;
            pool.Dispose();
            UnityEngine.Object.DestroyImmediate(prefab);
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Pooling check failed: " + message);
    }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (Exception) { return; }
        throw new Exception("Pooling check failed: " + message);
    }
}
