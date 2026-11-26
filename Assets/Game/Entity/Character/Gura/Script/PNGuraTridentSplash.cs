#if UNITY_5_3_OR_NEWER
using Newtonsoft.Json.Linq;
using UnityEngine;

public sealed class PNGuraTridentSplash : PNCylinderBlast
{
    private PNGuraWaterParticles water;
    private PNGuraWaterFx ripples;
    private float tail;

    public override void OnPoolCreated()
    {
        base.OnPoolCreated();
        water = PNGuraWaterParticles.Create(transform, "impact");
        ripples = new PNGuraWaterFx(transform);
    }

    protected override void OnBegin(JObject input) => tail = input["tail"].Value<float>();
    protected override void OnDetonated() => Draw(0f);

    protected override void OnPhase(float time, float delay)
    {
        if (Detonated && tail > 0f) Draw(Mathf.Clamp01((time - delay) / tail));
    }

    private void Draw(float progress)
    {
        Vector3 surface = Center + Vector3.up * .035f;
        water?.Sample(surface, Vector3.zero,
            new Vector3(Radius * .46f, Radius * .16f, Radius * .46f), .065f + progress * .62f);
        ripples.Begin();
        float edge = Mathf.Lerp(.12f, 1f, Mathf.Sqrt(progress)) * Radius;
        ripples.Ripple(surface, edge, .055f * Radius, (1f - progress) * .85f);
        ripples.Draw();
    }

    protected override void OnEnded()
    {
        water?.Hide();
        ripples?.Hide();
    }
    public override void DisposePooledResources()
    {
        water?.Dispose();
        ripples?.Dispose();
        water = null;
        ripples = null;
        base.DisposePooledResources();
    }
}
#else
public sealed class PNGuraTridentSplash { }
#endif
