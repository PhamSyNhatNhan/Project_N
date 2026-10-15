#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

// Pool-retained ParticleSystem, manually driven once by region Game delta.
public sealed class PNTimeScaleDust : IDisposable
{
    private readonly GameObject root;
    private readonly ParticleSystem system;
    private readonly Material material;
    private ParticleSystem.Particle[] particles;
    private PNTimeScaleArea owner;
    private bool started;
    private float opacity;
    public int Count => system != null ? system.particleCount : 0;
    public float Time { get; private set; }
    public PNTimeScaleDust(Transform parent)
    {
        root = new GameObject("Owned timescale dust"); root.SetActive(false); root.transform.SetParent(parent, false);
        system = root.AddComponent<ParticleSystem>();
        var main = system.main; main.playOnAwake = false; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.useUnscaledTime = false;
        var renderer = system.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        material = new Material(Resources.Load<Shader>("PNVfx/TimeScaleDust")) { name = "Owned timescale dust material", hideFlags = HideFlags.HideAndDontSave };
        renderer.sharedMaterial = material;
    }
    public void Initialize(PNTimeScaleArea region, PNTimeScaleAreaVisualSettings settings)
    {
        Reset(); owner = region; root.layer = region.gameObject.layer;
        var main = system.main; main.startLifetime = settings.Lifetime; main.startSpeed = settings.Speed;
        main.startSize3D = true; main.startSizeX = new ParticleSystem.MinMaxCurve(settings.Size*.75f,settings.Size*1.25f);
        main.startSizeY = settings.Size*settings.Aspect; main.startSizeZ = settings.Size;
        main.startRotation = new ParticleSystem.MinMaxCurve(0,Mathf.PI*2);
        opacity = settings.ParticleOpacity;
        var references=Resources.Load<PNTimeScaleTextureReferences>("PNVfx/TimeScaleTextures");
        if(references==null || references.Glow==null) throw new InvalidOperationException("Registered TimeScale texture references missing.");
        material.SetTexture("_GlowTex",references.Glow);
        var gradient=new Gradient(); gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,.2f),new GradientAlphaKey(1,.8f),new GradientAlphaKey(0,1)});
        var color=system.colorOverLifetime; color.enabled=true; color.color=gradient;
         main.startColor = settings.DustColor; main.maxParticles = settings.Maximum;
        var emission = system.emission; emission.enabled = true; emission.rateOverTime = settings.Rate;
        var shape = system.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = region.Radius; shape.radiusThickness = 1;
        if (particles == null || particles.Length < settings.Maximum) particles = new ParticleSystem.Particle[settings.Maximum];
        root.SetActive(true); started = false;
        material.SetFloat("_Opacity", 0);
    }
    public void Advance(float delta, float strength, bool emitting)
    {
        if (owner == null) return;
        if (!started && root.activeInHierarchy) { system.Play(false); system.Pause(false); started = true; }
        root.layer = owner.gameObject.layer;
        root.transform.localScale = Vector3.one * (owner.Radius > 0 ? owner.VisualRadius / owner.Radius : 0);
        var emission = system.emission; emission.enabled = emitting;
        if (delta > 0) { system.Simulate(delta, false, false, false); system.Pause(false); Time += delta; }
        int count = system.GetParticles(particles);
        for (int i = 0; i < count; i++)
        {
            var position = particles[i].position;
            if (position.sqrMagnitude > owner.Radius * owner.Radius || PNTimeScaleAreaRendering.HasEarlierDustOwner(owner, root.transform.TransformPoint(position)))
                particles[i].remainingLifetime = 0;
        }
        system.SetParticles(particles, count); material.SetFloat("_Opacity", strength*opacity);
    }
    public void Reset()
    { if (system != null) system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); if (root != null) root.SetActive(false); owner = null; started = false; Time = 0; if (particles != null) Array.Clear(particles, 0, particles.Length); }
    public void Dispose()
    {
        Reset();
        if (Application.isPlaying) { if (root != null) UnityEngine.Object.Destroy(root); if (material != null) UnityEngine.Object.Destroy(material); }
        else { if (root != null) UnityEngine.Object.DestroyImmediate(root); if (material != null) UnityEngine.Object.DestroyImmediate(material); }
    }
}
#endif
