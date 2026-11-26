#if UNITY_5_3_OR_NEWER
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class PNGuraTridentSplash : PNCylinderBlast
{
    private PNGuraWaterFx ripples;
    private PNGuraWaterParticles explosion;
    private float tail;
    private IPNCameraHandle shake;

    public override void OnPoolCreated()
    {
        base.OnPoolCreated();
        ripples = new PNGuraWaterFx(transform);
        explosion = PNGuraWaterParticles.Create(transform, "skill_explosion");
    }

    protected override void OnBegin(JObject input) => tail = input["tail"].Value<float>();
    protected override void OnDetonated()
    {
        Draw(0f);
        if (!Application.isPlaying) return;
        shake?.Dispose();
        shake = PNCameraServices.Current.RequestShake(gameObject, "gura.skill_splash",
            new PNShakeSpec(.045f, 22f, .17f, .13f, 0f, PNShakeShape.Recoil, .5f, 1f, .1f, 7f, PNShakeFalloff.Quadratic));
    }

    protected override void OnPhase(float time, float delay)
    {
        if (Detonated && tail > 0f) Draw(Mathf.Max(0f, time - delay));
    }

    // One hit at the strike, then the picture of the pack's Water_Explosion plays over `tail`: flash, a swelling ball of water with spikes, ribbons of foam turning round it, breaking into drops.
    // The source moments below are those of the prefab; a tail of .75 s plays it close to its own speed, another tail stretches it. Only the thin edge of the hit radius is added.
    private void Draw(float age)
    {
        Vector3 surface = Center + Vector3.up * .035f;
        float progress = Mathf.Clamp01(age / Mathf.Max(.001f, tail));
        // The source ball is about 2.35 m in radius at its own size; it sits a little above the ground so more than its upper half shows.
        float scale = Radius / 2.35f;
        explosion?.Sample(surface + Vector3.up * (Radius * .22f), Vector3.zero, Vector3.one * scale, Mathf.Lerp(.125f, .815f, progress));
        ripples.Begin();
        float edge = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.08f, .42f, progress));
        if (edge > 0f) ripples.BlastEdge(surface, Radius, edge);
        ripples.Draw();
    }

    protected override void OnEnded()
    {
        ripples?.Hide();
        explosion?.Hide();
    }
    public override void DisposePooledResources()
    {
        shake?.Dispose();
        shake = null;
        ripples?.Dispose();
        explosion?.Dispose();
        ripples = null;
        explosion = null;
        base.DisposePooledResources();
    }
}
#else
public sealed class PNGuraTridentSplash { }
#endif
