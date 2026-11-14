#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Herald's private, deliberately simple effect (Docs/HeraldPlan.md H10; the user restyles it later): a few pale blue-violet sparks gather above the halo while the cast winds up, one ring spreads
// over the ground to the registered radius at the release, and a small puff of sparks rises at every target that received the Buff. Existing star shader and modes only (PNStarweaverEffects), no new
// shader. It advances with the puppet's clock (entity time, pause included) and returns every object it made when the cast ends, the Herald dies or the component goes away.
[DefaultExecutionOrder(250), DisallowMultipleComponent]
public sealed class PNHeraldPresentation : MonoBehaviour, IDisposable
{
    public static readonly Color Pale = new Color(.6f, .66f, 1.5f, 1);
    private const float RingSeconds = .45f;
    private readonly List<Material> materials = new List<Material>();
    private readonly Vector3[] ringPoints = new Vector3[49];
    private GameObject root; private LineRenderer ring; private Material ringMaterial; private ParticleSystem sparks;
    private PNVisualBody body; private PNStat stat; private PNVisualActor actor;
    private float clock, ringAge = -1, ringRadius, gatherTimer;
    public bool HasEffects => root != null;
    public int LiveSparks => sparks != null ? sparks.particleCount : 0;
    public bool RingVisible => ring != null && ring.enabled;

    private void Awake() { body = GetComponent<PNVisualBody>(); stat = GetComponent<PNStat>(); }
    private void LateUpdate()
    {
        if (body == null) body = GetComponent<PNVisualBody>();
        var next = body != null ? body.Actor : actor;
        if (next == null || (stat != null && stat.IsReady && stat.IsDead)) { Release(); return; }
        actor = next;
        if (root != null) Advance(actor.ClockDelta());
    }
    // Called every frame of the cast by the Action with its own age (0 .. total).
    public void Cast(float age, float windup, float release)
    {
        Ensure();
        if (age < windup && gatherTimer <= 0) { gatherTimer = .06f; Emit(HaloPoint() + new Vector3(0, .05f, 0), 1, .16f, .6f); }
    }
    public void Pulse(float radius) { Ensure(); ringAge = 0; ringRadius = radius; ring.enabled = true; Emit(HaloPoint(), 8, .12f, .5f); DrawRing(.3f, 1); }
    public void Recipient(Vector3 position) { Ensure(); Emit(position + Vector3.up * .05f, 14, .25f, .8f); }
    public void EndCast() { ringAge = -1; if (ring != null) ring.enabled = false; }

    private Vector3 HaloPoint() => transform.position + Vector3.up * .5f;
    public void Advance(float dt)
    {
        if (root == null) return;
        dt = Mathf.Max(0, dt); clock += dt; gatherTimer -= dt;
        if (dt > 0) sparks.Simulate(dt, false, false, false);
        foreach (var m in materials) m.SetFloat("_FxTime", clock);
        if (ringAge >= 0)
        {
            ringAge += dt; float t = Mathf.Clamp01(ringAge / RingSeconds);
            if (t >= 1) { ringAge = -1; ring.enabled = false; }
            else DrawRing(Mathf.Lerp(.3f, ringRadius, 1 - (1 - t) * (1 - t)), 1 - t);
        }
    }
    private void DrawRing(float radius, float fade)
    {
        var center = transform.position + Vector3.up * .03f;
        for (int i = 0; i < ringPoints.Length; i++) { float a = i / (float)(ringPoints.Length - 1) * Mathf.PI * 2; ringPoints[i] = center + new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius); }
        ring.SetPositions(ringPoints); ring.widthMultiplier = .05f + .04f * fade;
        ringMaterial.SetColor("_Tint", new Color(Pale.r * fade, Pale.g * fade, Pale.b * fade, 1));
    }
    private void Emit(Vector3 position, int count, float spread, float lifetime)
    {
        for (int i = 0; i < count; i++)
        {
            float a = i * 2.399963f + clock * 3, r = spread * Mathf.Sqrt((i + .5f) / count);
            sparks.Emit(new ParticleSystem.EmitParams
            {
                position = position + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r), velocity = new Vector3(0, .12f + (i % 4) * .03f, 0),
                startColor = Pale, startSize = .02f + (i % 3) * .006f, startLifetime = lifetime + (i % 3) * .1f
            }, 1);
        }
    }
    private Material Make(int mode, string mask, Color tint)
    {
        var shader = Resources.Load<Shader>("PNStarweaverEffects"); if (shader == null) throw new InvalidOperationException("Missing star VFX shader.");
        var m = new Material(shader) { name = "Herald effect " + mode, hideFlags = HideFlags.HideAndDontSave };
        materials.Add(m); m.SetFloat("_Mode", mode); m.SetFloat("_DstBlend", 1); m.SetColor("_Tint", tint);
        m.SetTexture("_NoiseTex", Resources.Load<Texture2D>("StarweaverVfx/Noise"));
        if (mask != null) m.SetTexture("_MainTex", Resources.Load<Texture2D>("StarweaverVfx/" + mask));
        return m;
    }
    private void Ensure()
    {
        if (root != null) return;
        root = new GameObject("Herald sidus effect") { hideFlags = HideFlags.DontSave }; root.layer = gameObject.layer; root.transform.SetParent(transform, false);
        try
        {
            var go = new GameObject("Spreading ring"); go.layer = gameObject.layer; go.transform.SetParent(root.transform, false);
            ring = go.AddComponent<LineRenderer>(); ringMaterial = Make(22, null, Pale); ring.sharedMaterial = ringMaterial;
            ring.positionCount = ringPoints.Length; ring.useWorldSpace = true; ring.enabled = false; ring.shadowCastingMode = ShadowCastingMode.Off; ring.receiveShadows = false;
            var ps = new GameObject("Sparks"); ps.layer = gameObject.layer; ps.transform.SetParent(root.transform, false);
            sparks = ps.AddComponent<ParticleSystem>(); sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main; main.playOnAwake = false; main.loop = false; main.maxParticles = 120; main.simulationSpace = ParticleSystemSimulationSpace.World; main.startSpeed = 0;
            var emission = sparks.emission; emission.enabled = false; var shape = sparks.shape; shape.enabled = false;
            var col = sparks.colorOverLifetime; col.enabled = true; var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.85f, .15f), new GradientAlphaKey(0, 1) }); col.color = gradient;
            var size = sparks.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1, 1, 0));
            var r = sparks.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = Make(5, "Glow", Color.white); r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        }
        catch { Release(); throw; }
    }
    private void OnDisable() { Release(); }
    private void OnDestroy() { Release(); }
    public void Dispose() { Release(); }
    private void Release()
    {
        if (!ReferenceEquals(root, null)) { root.SetActive(false); DestroyOwned(root); }
        foreach (var m in materials) DestroyOwned(m);
        materials.Clear(); root = null; ring = null; ringMaterial = null; sparks = null; ringAge = -1; clock = 0; gatherTimer = 0;
    }
    private static void DestroyOwned(UnityEngine.Object value)
    { if (ReferenceEquals(value, null)) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
}
#endif
