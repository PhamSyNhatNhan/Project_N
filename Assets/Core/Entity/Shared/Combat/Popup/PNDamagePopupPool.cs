#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owned by PNStat, hosted independently so the killing hit can finish after its owner disappears.
public sealed class PNDamagePopupPool : MonoBehaviour
{
    private readonly List<PNDamagePopup> active = new List<PNDamagePopup>();
    private readonly Stack<PNDamagePopup> idle = new Stack<PNDamagePopup>();
    private PNDamagePopup prefab;
    private Guid sessionId;
    private bool retiring;
    private int sequence;
    public int CountActive => active.Count;
    public int CountInactive => idle.Count;
    public IReadOnlyList<PNDamagePopup> Active => active.AsReadOnly();
    // True while the prefab this pool instantiates from still exists (Unity null check, no lookup).
    internal bool PrefabAlive => prefab != null;
    public static PNDamagePopupPool Create(PNStat owner, PNDamagePopup prefab, Guid session)
    {
        var root = new GameObject("PN Damage Popup Pool: " + owner.name);
        SceneManager.MoveGameObjectToScene(root, owner.gameObject.scene);
        var pool = root.AddComponent<PNDamagePopupPool>(); pool.prefab = prefab; pool.sessionId = session;
        PNDamagePopupDiagnostics.Record("create", pool);
        return pool;
    }
    public PNDamagePopup Show(double damage, Vector3 position, Color color, EveTimeScale clock)
        => Show(damage, position, color, clock, false, Array.Empty<EveId>());
    public PNDamagePopup Show(double damage, Vector3 position, Color color, EveTimeScale clock, bool critical, EveId[] elements)
    {
        using var diagnostic = EveHotPathMetrics.Measure("popup.pool-show");
        PNDamagePopupDiagnostics.Record("before-show", this);
        if (retiring) throw new InvalidOperationException("Damage popup pool has retired.");
        PNDamagePopup popup = null;
        while (idle.Count != 0 && popup == null) popup = idle.Pop();
        if (popup == null)
        {
            EveHotPathMetrics.Increment("popup.pool-miss");
            using (EveHotPathMetrics.Measure("popup.instantiate")) popup = Instantiate(prefab, transform);
        }
        else EveHotPathMetrics.Increment("popup.pool-hit");
        // Alternating lanes avoid consuming gameplay random state or overlapping consecutive hits.
        position += Vector3.right * (((sequence++ % 3) - 1) * .12f);
        try { popup.Begin(damage, position, color, clock, critical, elements); active.Add(popup); }
        catch { using (EveHotPathMetrics.Measure("popup.destroy")) DestroyObject(popup.gameObject); throw; }
        if (Application.isPlaying && Camera.main != null) PNDamagePopupBloom.Ensure(Camera.main);
        PNDamagePopupDiagnostics.Record("after-show", this, popup);
        return popup;
    }
    private void Update() => Advance(Time.deltaTime);
    public void Advance(float deltaTime, Camera camera = null)
    {
        using var diagnostic = EveHotPathMetrics.Measure("popup.advance-all");
        PNDamagePopupDiagnostics.Record("before-advance", this);
        if (!EveRegistrySession.IsReady || EveRegistrySession.Context.SessionId != sessionId) { DestroyObject(gameObject); return; }
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var popup = active[i];
            if (popup != null && !popup.Advance(deltaTime, camera)) continue;
            active.RemoveAt(i);
            if (popup == null) continue;
            using (EveHotPathMetrics.Measure("popup.return"))
            {
                using (EveHotPathMetrics.Measure("popup.disable")) popup.gameObject.SetActive(false);
                if (retiring || idle.Count >= 32)
                {
                    using (EveHotPathMetrics.Measure("popup.destroy")) DestroyObject(popup.gameObject);
                    EveHotPathMetrics.Increment("popup.expired-destroyed");
                }
                else { idle.Push(popup); EveHotPathMetrics.Increment("popup.returned-idle"); }
            }
        }
        if (retiring && active.Count == 0) DestroyObject(gameObject);
        PNDamagePopupDiagnostics.Record("after-advance", this);
    }
    public void Retire()
    {
        using var diagnostic = EveHotPathMetrics.Measure("popup.retire");
        PNDamagePopupDiagnostics.Record("retire", this);
        retiring = true;
        foreach (var popup in active) if (popup != null) popup.DetachClock();
        while (idle.Count != 0) { var popup = idle.Pop(); if (popup != null) using (EveHotPathMetrics.Measure("popup.destroy")) DestroyObject(popup.gameObject); }
        if (active.Count == 0) DestroyObject(gameObject);
    }
    private static void DestroyObject(GameObject value)
    {
        if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
    }
    private void OnDestroy() => PNDamagePopupDiagnostics.Record("destroy-pool", this);
}
#endif
