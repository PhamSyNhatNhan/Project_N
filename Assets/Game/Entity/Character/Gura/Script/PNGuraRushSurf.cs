#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using EveEngine;
using UnityEngine;

// EN: The water of Gura's rush on and over the floor: the breaking wave that carries her, the strip it leaves, and, at the arrival, the wave falling in on the spot: its crest drops, the body gets
//     lower and stretches back into a layer on the floor, and it breaks up and fades with the strip. While the Action runs its view drives this by the run's age; the fading outlasts the Action,
//     so after it the picture goes on by itself. Display only: it reads where she is and nothing else.
// VI: Nuoc cua cu luot: con song, dai nuoc de lai, va luc toi noi song do xuong, dan ve sau roi tan. Chi la hinh.
public sealed class PNGuraRushSurf : MonoBehaviour
{
    // Seconds after the arrival until nothing is left.
    private const float Fade = .65f;
    private const float Spacing = .2f;
    // The drops thrown off what she struck: how long they fly, and how far ahead of where she stops it stands (the step's `stopDistance` in the shipped data).
    private const float Splash = .42f, Struck = .6f;
    private Transform owner;
    private EveTimeScale clock;
    private PNGuraWave wave;
    private PNGuraWaterFx lying, air;
    private PNGuraWakeStrip strip;
    // The places the wave has passed and when: water stays there for a while.
    private readonly List<(Vector3 At, float Born)> wake = new();
    private Vector3 ground, facing = Vector3.right, arrivedAt;
    private float pause, travel = -1f, age;
    private bool landed, released, removed;
    // The shake of the hit: once, and only when the way there was seen (not on a view that starts after the arrival).
    private bool onTheWay, shook, seeking;
    private IPNCameraHandle shake;

    public bool Released => released;

    internal static PNGuraRushSurf Create(Transform owner, float pause)
    {
        var node = new GameObject("Gura Rush Water") { layer = owner.gameObject.layer };
        node.transform.SetParent(owner, false);
        var surf = node.AddComponent<PNGuraRushSurf>();
        surf.owner = owner;
        surf.pause = pause;
        surf.clock = owner.GetComponent<EveTimeScale>();
        surf.wave = new PNGuraWave(node.transform);
        // Under the coat and the wave.
        surf.lying = new PNGuraWaterFx(node.transform, "Gura rush water on the ground", 3011);
        surf.strip = new PNGuraWakeStrip(node.transform);
        // Over the wave.
        surf.air = new PNGuraWaterFx(node.transform, "Gura rush drops");
        return surf;
    }

    private float Arrival => pause + Mathf.Max(0f, travel);
    private bool Arrived => age >= Arrival && (travel >= 0f || age > pause + .04f);

    // One step of the running Action. `travel`: seconds of the way once known, below zero before.
    internal void Run(float runAge, Vector3 position, Vector3 way, float travelSeconds, bool jumped)
    {
        if (removed || released || owner == null) return;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        age = runAge;
        ground = position + Vector3.up * .045f;
        facing = way;
        travel = travelSeconds;
        seeking = jumped;
        Draw();
    }

    // The Action ended on its own: the water stays where she stopped and goes on fading.
    public void Release()
    {
        if (removed || released) return;
        released = true;
    }

    private void LateUpdate()
    {
        if (released) Tick(clock != null && clock.isActiveAndEnabled ? clock.DeltaTime : Time.deltaTime);
    }

    public void Tick(float delta)
    {
        if (removed || !released || owner == null) return;
        age += delta;
        seeking = false;
        if (age >= Arrival + Fade) { Remove(); return; }
        Draw();
    }

    private void Draw()
    {
        bool arrived = Arrived;
        float since = age - Arrival;
        if (arrived && !landed) { landed = true; arrivedAt = ground; }
        // A replay from before the arrival.
        if (!arrived) { landed = false; onTheWay = true; }
        else if (onTheWay && !shook && !seeking && Application.isPlaying)
        {
            shook = true;
            shake = PNCameraServices.Current.RequestShake(gameObject, "gura.rush_hit",
                new PNShakeSpec(.11f, 25f, .24f, .18f, 0f, PNShakeShape.Recoil, .68f, 1f, .12f, 7f, PNShakeFalloff.Quadratic));
        }

        Vector3 side = Vector3.Cross(Vector3.up, facing).normalized;
        lying.Begin();
        strip.Begin();
        Wake(arrived ? Mathf.Clamp01(since / .24f) : 0f, arrived ? Spread(since) : 0f, side);
        strip.Draw(age, facing);
        if (age < pause)
        {
            float gathering = Mathf.Clamp01(age / Mathf.Max(.01f, pause));
            lying.Pool(ground, Mathf.Lerp(.14f, .36f, gathering), age, .85f * (1f - gathering * gathering));
            lying.Ripple(ground, Mathf.Lerp(.55f, .2f, gathering), .035f, Mathf.Sin(Mathf.PI * gathering));
        }
        // The wave swells behind her while she gathers.
        float swell = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(pause * .35f, pause + .04f, age));
        Vector3 crest = ground + facing * Mathf.Lerp(-.3f, .22f, Mathf.InverseLerp(pause * .7f, pause + .04f, age));
        // The floor dark and wet under it, drying after the fall.
        Vector3 shadeAt = arrived ? arrivedAt + facing * .15f : crest;
        lying.WetShade(shadeAt - facing * .3f, facing, .8f * Mathf.Lerp(.6f, 1f, swell), 1.15f * Mathf.Lerp(.5f, 1f, swell),
            .45f * swell * (arrived ? 1f - Mathf.Clamp01(since / .55f) : 1f));
        lying.Draw();

        air.Begin();
        if (arrived) Drops(arrivedAt + facing * Struck + Vector3.up * .26f, since / Splash, side);
        air.Draw();

        if (!arrived)
        {
            // It carries her once she goes, and rises and curls over as it nears.
            float going = travel > 0f ? Mathf.Clamp01((age - pause) / travel) : 0f;
            float curl = swell * Mathf.Lerp(.45f, 1f, Mathf.SmoothStep(0f, 1f, going));
            wave.Draw(crest, facing, 1.45f * Mathf.Lerp(.6f, 1f, swell), .5f * swell * Mathf.Lerp(1f, 1.25f, going), 2.1f * Mathf.Lerp(.5f, 1f, swell), age, swell, 0f, curl);
            return;
        }

        // The wave stops at the enemy's face and falls in on the spot: first the curled crest drops, then the body gets lower and runs out wide, mostly back and to the sides, a thin layer of
        // water on the floor that joins the strip, still spreading while it tears apart along its own pattern. Only a picture: it is wider than the hit.
        float sink = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .32f, since));
        float spread = Spread(since);
        float height = Mathf.Lerp(.625f, .075f, sink);
        float width = Mathf.Lerp(1.45f, 3.1f, spread), length = Mathf.Lerp(2.1f, 3.8f, spread);
        // The mesh's origin lies .28 of its length behind its nose.
        Vector3 nose = arrivedAt + facing * (.81f + .35f * spread);
        float falling = since < .06f ? Mathf.Lerp(1f, 1.15f, since / .06f) : Mathf.Lerp(1.15f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((since - .06f) / .16f)));
        float calm = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.03f, .3f, since));
        float dissolve = .97f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.18f, Fade, since));
        float opacity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.3f, Fade, since));
        // The water keeps running back, slower as it spends itself.
        wave.Draw(nose - facing * (.28f * length), facing, width, height, length, age - .45f * since * spread, opacity, dissolve, falling, null, calm);
    }

    // A few round drops knocked off what she struck: out ahead and to both sides, up and then down.
    private void Drops(Vector3 from, float p, Vector3 side)
    {
        if (p < 0f || p >= 1f) return;
        var camera = PNViewCamera.Current;
        Vector3 right = camera != null ? camera.transform.right : Vector3.right;
        Vector3 up = camera != null ? camera.transform.up : Vector3.up;
        const int count = 12;
        float go = 1f - (1f - p) * (1f - p);
        for (int i = 0; i < count; i++)
        {
            float seed = Mathf.Repeat(i * .618034f + .15f, 1f), variation = Mathf.Repeat(i * .414214f + .28f, 1f);
            float turn = Mathf.Lerp(-1.9f, 1.9f, (i + .5f) / count) + (variation - .5f) * .25f;
            Vector3 way = facing * Mathf.Cos(turn) + side * Mathf.Sin(turn);
            Vector3 at = from + way * (.16f + Mathf.Lerp(.35f, .85f, seed) * go) + Vector3.up * (Mathf.Lerp(.2f, .62f, variation) * 4f * p * (1f - p) - .22f * p * p);
            air.Bead(at, Mathf.Lerp(.048f, .1f, seed * variation + .25f * seed) * (1f - .45f * p), 1f - p * p * p, right, up);
        }
    }

    // How far the fallen water has run out (0..1): fast at first, still creeping when it is gone.
    private static float Spread(float since) => 1f - Mathf.Pow(1f - Mathf.Clamp01(since / Fade), 2f);

    // What stays where the wave has passed: one flat strip of water on the floor that reaches up to the wave, gone together with it.
    private void Wake(float fall, float spread, Vector3 side)
    {
        // Not gone yet (or a replay from the start): the way begins here.
        if (age < pause) { wake.Clear(); wake.Add((ground, pause)); return; }
        if (wake.Count == 0) wake.Add((ground, age));
        while (wake.Count < 64)
        {
            Vector3 step = ground - wake[wake.Count - 1].At;
            if (step.sqrMagnitude < Spacing * Spacing) break;
            wake.Add((wake[wake.Count - 1].At + step.normalized * Spacing, age));
        }
        float end = Arrival + Fade;
        float travelled = (wake.Count - 1) * Spacing + Vector3.Distance(wake[wake.Count - 1].At, ground);
        // The strip stops short of her, under the tail of the wave; once the wave falls it fills up to where she stands.
        float solid = travelled - Mathf.Lerp(.85f, .55f, fall), gone = travelled - Mathf.Lerp(.15f, 0f, fall);
        for (int i = 0; i <= wake.Count; i++)
        {
            bool last = i == wake.Count;
            Vector3 at = last ? ground : wake[i].At;
            // The loose end keeps the age of the place before it: a fresh end beside old water would show as a hard edge.
            float born = last ? wake[wake.Count - 1].Born : wake[i].Born;
            float along = last ? travelled : i * Spacing;
            float old = Mathf.Clamp01((age - born) / Mathf.Clamp(end - born, .2f, .9f));
            float start = Mathf.Clamp01(along / 2.2f);
            float shown = Mathf.SmoothStep(0f, 1f, start) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(solid, gone, along)));
            float half = (.84f + .14f * old) * Mathf.Lerp(.2f, 1f, Mathf.SmoothStep(0f, 1f, start));
            // Near where it fell the strip runs out wide with it.
            half *= 1f + .75f * spread * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(travelled - 3f, travelled - .8f, along));
            strip.Add(at, side, half, along, old, shown);
        }
    }

    public void Hide()
    {
        if (removed) return;
        shake?.Dispose(); shake = null;
        onTheWay = false;
        wave?.Hide();
        lying?.Hide();
        strip?.Hide();
        air?.Hide();
        gameObject.SetActive(false);
    }

    public void Remove()
    {
        Clear();
        if (this == null) return;
        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
    }

    private void Clear()
    {
        if (removed) return;
        removed = true;
        shake?.Dispose(); shake = null;
        wave?.Dispose(); wave = null;
        lying?.Dispose(); lying = null;
        strip?.Dispose(); strip = null;
        air?.Dispose(); air = null;
        wake.Clear();
    }

    private void OnDestroy() => Clear();
}
#endif
