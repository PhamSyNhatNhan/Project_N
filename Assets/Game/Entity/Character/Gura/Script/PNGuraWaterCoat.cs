using EveEngine;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// EN: The picture of the water coat of the held Dash (Docs/GuraCharacter.md 4.5): a registered look sustained on the entity while the coat state is on (skill data `stateEffects.look`), so the engine
//     turns it on and off on every machine and nothing here knows the network. Display only.
// VI: Hinh ao nuoc cua Dash giu: look dang ky, bat/tat theo state ao nuoc. Chi la hinh.
public static class PNGuraCoatLooks
{
    public static readonly EveId Id = EveId.Parse("project_n:gura_coat");

    public static PNLook Create(PNLookBuild build)
    {
#if UNITY_5_3_OR_NEWER
        return build.Skill != null ? new PNGuraCoatLook(build.Skill) : null;
#else
        return null;
#endif
    }
}

#if UNITY_5_3_OR_NEWER
internal sealed class PNGuraCoatLook : PNLook
{
    private PNGuraCoatVisual visual;
    private readonly PNStat stat;

    public PNGuraCoatLook(PNSkill skill)
    {
        var node = new GameObject("Gura Dash Coat") { layer = skill.gameObject.layer };
        node.transform.SetParent(skill.transform, false);
        stat = skill.GetComponent<PNStat>();
        visual = node.AddComponent<PNGuraCoatVisual>();
        visual.Bind(skill);
    }

    // The splash of letting go still plays after `End`; the engine ends the look again on teardown.
    public override bool HasTail => true;

    public override void End(PNActionViewEnd kind)
    {
        if (visual == null) return;
        bool dead = stat != null && stat.IsDead;
        if (kind == PNActionViewEnd.Completed && !dead && !visual.Releasing && visual.gameObject.activeInHierarchy) { visual.Release(); return; }
        visual.Remove(); visual = null;
    }
}

// Draws itself from its own age. Put on: solid water rises from her feet, then clears to a thin coat with a ring of light around her, so the face and the colours stay seen. While she runs it leaves
// solid after-images and two speed lines. In the last second of the state it flickers. Let go: it turns solid and melts away layer by layer, with rings on the ground.
public sealed class PNGuraCoatVisual : MonoBehaviour
{
    private const float Rise = .12f, ClearFrom = .18f, ClearTo = .42f, Flash = .08f, ReleaseTail = .46f;
    private const float Thin = .12f, RingWidth = .028f, Warning = 1f, GhostEvery = .75f;
    private Transform owner;
    private PNSkill skill;
    private PNAbility state;
    private EveTimeScale clock;
    private PNGuraWaterFx water;
    private Material material, ringMaterial;
    private PNGuraEchoRush.Silhouette sheet, ring;
    private PNGuraWaterGhosts ghosts;
    private PNVisualActor covered;
    private PNVisualBody body;
    private Vector3 lastPosition, heading = Vector3.right;
    private float age, moving, run, blink, releasedFor = -1f, releasedFill = Thin;
    private float fill = 1f;

    public bool Releasing => releasedFor >= 0f;
    public int GhostCount => ghosts != null ? ghosts.Count : 0;
    // The inside of the coat as drawn last: 1 = solid water.
    public float Fill => fill;

    public void Bind(PNSkill target)
    {
        skill = target; owner = target.transform; lastPosition = owner.position;
        clock = owner.GetComponent<EveTimeScale>();
        body = owner.GetComponentInChildren<PNVisualBody>();
        water = new PNGuraWaterFx(transform, "Gura Dash Coat Water");
        material = PNGuraEchoRush.CreateMaterial("Gura Dash coat");
        ringMaterial = PNGuraEchoRush.CreateMaterial("Gura Dash coat ring", true);
        ghosts = new PNGuraWaterGhosts(transform, 3, .45f);
        Tick(0f);
    }

    public void Release()
    {
        if (releasedFor >= 0f) return;
        releasedFor = 0f; releasedFill = fill;
    }

    public void Remove()
    {
        Clear();
        if (this == null) return;
        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
    }

    private void LateUpdate() => Tick(clock != null && clock.isActiveAndEnabled ? clock.DeltaTime : Time.deltaTime);

    // Seconds the coat state still lasts, when this machine knows it (a machine that only watches may not).
    private double? Remaining()
    {
        if (state == null || !state.IsActive)
        {
            state = null;
            var abilities = skill != null ? skill.Abilities : null;
            if (abilities == null) return null;
            try
            {
                var all = abilities.Find(PNGuraContent.WaterCoat);
                for (int i = 0; i < all.Count && state == null; i++) if (all[i].IsActive) state = all[i];
            }
            catch (System.Collections.Generic.KeyNotFoundException) { return null; }
        }
        return state?.DurationRemaining;
    }

    public void Tick(float delta)
    {
        if (water == null || owner == null) return;
        age += delta;
        if (releasedFor >= 0f) releasedFor += delta;
        if (releasedFor >= ReleaseTail) { Remove(); return; }

        Vector3 position = owner.position, step = position - lastPosition;
        lastPosition = position; step.y = 0f;
        // A jump (teleport) is not a run.
        bool running = delta > 0f && step.magnitude < 1f && step.magnitude / delta > .5f;
        if (delta > 0f)
        {
            if (running) heading = step.normalized;
            moving = Mathf.MoveTowards(moving, running ? 1f : 0f, delta * 7f);
        }

        // While her puppet is away (a strike that hides it) the coat is away too.
        if (body == null) body = owner.GetComponentInChildren<PNVisualBody>();
        var actor = body != null ? body.Actor : null;
        if (actor != null && actor.Renderer != null && !actor.Renderer.enabled)
        {
            water.Hide(); sheet?.Hide(); ring?.Hide(); ghosts.Clear(); run = 0f;
            return;
        }

        bool releasing = releasedFor >= 0f;
        float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / Rise));
        float clear = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ClearFrom, ClearTo, age));

        // The last second: it comes and goes, faster toward the end.
        float steady = 1f;
        double? left = releasing ? null : Remaining();
        if (left.HasValue && left.Value > 0d && left.Value < Warning)
        {
            blink += delta * Mathf.Lerp(34f, 18f, (float)left.Value / Warning);
            steady = .5f + .5f * Mathf.Cos(blink);
        }
        else blink = 0f;

        float dissolve, opacity, ringOpacity, ringWide;
        bool waterline = false, fromTop = true;
        if (!releasing)
        {
            // Rising: a waterline climbs from the feet (`dissolve` = the share of her height still dry).
            waterline = rise < 1f;
            dissolve = waterline ? Mathf.Lerp(.995f, 0f, rise) : 0f;
            // (the warning also flashes the inside half solid, so it is seen at a glance)
            fill = Mathf.Lerp(.55f, Mathf.Lerp(1f, Thin, clear), steady);
            opacity = Mathf.Lerp(.96f, .90f, clear);
            ringOpacity = rise * .95f * Mathf.Lerp(.12f, 1f, steady);
            ringWide = RingWidth * (1f + .16f * Mathf.Sin(age * 7f));
        }
        else
        {
            float solid = Mathf.Clamp01(releasedFor / Flash);
            float melt = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Flash, ReleaseTail - .03f, releasedFor));
            fromTop = false;
            dissolve = melt;
            fill = Mathf.Lerp(releasedFill, 1f, solid);
            opacity = .95f;
            ringOpacity = .95f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Flash, .32f, releasedFor)));
            ringWide = RingWidth * (1f + 1.1f * solid);
        }

        if (actor != null && actor.Renderer != null && material != null && ringMaterial != null)
        {
            if (covered != actor)
            {
                sheet?.Dispose(); ring?.Dispose();
                sheet = new PNGuraEchoRush.Silhouette(actor, material, "Gura Dash coat sheet");
                ring = new PNGuraEchoRush.Silhouette(actor, ringMaterial, "Gura Dash coat ring", true);
                covered = actor;
            }
            sheet.Draw(actor, age, dissolve, fromTop, opacity, waterline, fill);
            ring.Draw(actor, age, dissolve, fromTop, ringOpacity, waterline, 1f, ringWide);
        }

        // Solid after-images while she runs, once the Dash that put the coat on is over.
        if (!releasing && running && age > ClearTo)
        {
            run += step.magnitude;
            if (run >= GhostEvery && actor != null) { run = 0f; ghosts.Spawn(actor); }
        }
        ghosts.Advance(delta);

        water.Begin();
        Vector3 ground = position + Vector3.up * .035f;
        float shown = releasing ? 1f - Mathf.Clamp01(releasedFor / (ReleaseTail * .5f)) : rise * Mathf.Lerp(.3f, 1f, steady);

        // Drops lifting off the coat, left behind while she runs.
        for (int i = 0; i < 9; i++)
        {
            float seed = Mathf.Repeat(i * .618034f + .15f, 1f), variation = Mathf.Repeat(i * .414214f + .28f, 1f);
            float phase = Mathf.Repeat(age / Mathf.Lerp(.46f, .78f, variation) + seed, 1f);
            float turn = i * 2.39996f + age * 1.6f;
            Vector3 outward = new(Mathf.Cos(turn), 0f, Mathf.Sin(turn));
            Vector3 at = ground + outward * Mathf.Lerp(.21f, .40f, seed) + Vector3.up * (.05f + Mathf.Lerp(.30f, .62f, variation) * phase)
                - heading * (moving * .42f * phase);
            water.Drop(at, Vector3.up * (1f - moving * .7f) - heading * moving, Mathf.Lerp(.013f, .028f, seed * seed) * (1f - .5f * phase), variation,
                shown * Mathf.Sin(Mathf.PI * phase));
        }

        // Two speed lines behind her.
        if (moving > .01f)
        {
            var camera = PNViewCamera.Current;
            Vector3 view = camera != null ? camera.transform.forward : new Vector3(0f, -.5f, .866f);
            Vector3 side = Vector3.Cross(Vector3.up, heading);
            for (int i = 0; i < 2; i++)
            {
                Vector3 line = position + Vector3.up * (i == 0 ? .30f : .13f) + side * (i == 0 ? .10f : -.10f);
                float length = moving * (i == 0 ? .52f : .70f) * (.88f + .12f * Mathf.Sin(age * 17f + i * 2f));
                water.Streak(line - heading * (.18f + length), line - heading * .16f, .024f, shown * moving * .8f, view);
            }
        }

        // Let go: the water rings the ground and throws a few drops.
        if (releasing)
        {
            float p = Mathf.Clamp01(releasedFor / ReleaseTail);
            water.Ripple(ground, Mathf.Lerp(.24f, .85f, 1f - (1f - p) * (1f - p)), .055f, 1f - p);
            float second = Mathf.Clamp01((releasedFor - .09f) / (ReleaseTail - .09f));
            if (second > 0f) water.Ripple(ground, Mathf.Lerp(.18f, .55f, second), .026f, 1f - second);
            water.Fountain(ground, .40f, .06f + releasedFor * 1.5f, 12);
        }
        water.Draw();
    }

    private void Clear()
    {
        water?.Dispose(); water = null;
        sheet?.Dispose(); sheet = null; ring?.Dispose(); ring = null; covered = null;
        ghosts?.Dispose(); ghosts = null;
        if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
        if (ringMaterial != null) { if (Application.isPlaying) Destroy(ringMaterial); else DestroyImmediate(ringMaterial); }
        material = ringMaterial = null;
    }

    private void OnDestroy() => Clear();
}
#endif
