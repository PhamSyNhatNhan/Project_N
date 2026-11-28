#if UNITY_5_3_OR_NEWER
using System;
using UnityEngine;

// EN: The picture of the near form of Gura's Skill (Docs/GuraCharacter.md 4.2) after her own two slashes. While she is water, seven water shadows come one after another from different sides and
//     heights; each dashes past the heart of the struck enemy (never straight through it, from its own distance), cuts once and is gone, while the cut before it is still fading. Around them:
//     thin veils of water circling the enemy faster and tighter, and on the wet floor a bright slash mark left by every cut; as she forms again the marks flare.
//     No bubbles or round drops (the user's rule). Only the picture: the hit sectors belong to the Action and stay around the caster. Everything is placed from the Action age and is over before the
//     Action is, so there is no tail.
// VI: Hinh cua dang can chien: bay bong nuoc chem lech tam tu nhieu huong va do cao, dai nuoc mo luon quanh, vet chem tren san loe len khi Gura hien lai. Chi la hinh.
internal sealed class PNGuraShadowCuts : IDisposable
{
    // The first of the seven in the hit list (her own two slashes come before); how many there are; slots, as at most two shadows are about at once; puppets per slot (itself and two afterimages).
    public const int FirstHit = 2, Cuts = 7;
    private const int Slots = 2, Trail = 3;
    // Where each comes from: degrees round the enemy (0 = the side the cast came from), the height it starts and ends at (m), how far to the side it passes the enemy, and how far out it starts and
    // ends (m, at a reach of 3 m; all lengths grow with the reach of the hits). Low, high, falling and rising; no two alike.
    private static readonly float[] Bearings = { 55f, -125f, 150f, -40f, 100f, -80f, 12f };
    private static readonly float[] Starts = { .12f, 1.20f, .20f, 1.05f, .15f, 1.30f, .60f };
    private static readonly float[] Ends = { .22f, .15f, .90f, .15f, .30f, .20f, .12f };
    private static readonly float[] Miss = { 1.15f, -1.25f, 1.35f, -1.05f, 1.25f, -1.30f, 1.10f };
    private static readonly float[] From = { 2.0f, 2.4f, 1.8f, 2.3f, 2.0f, 2.4f, 2.2f };
    private static readonly float[] To = { 1.7f, 1.5f, 2.0f, 1.6f, 1.9f, 1.5f, 1.8f };
    private const float DrawnReach = 3f;
    // The dash past the enemy, around the hit; when the shadow shows and is gone; how long its cut takes to fade (as a clock of the slash arc).
    private const float DashBefore = .085f, DashAfter = .055f, ShowBefore = .11f, GoneAfter = .17f, CutClock = .75f;
    // The last cut holds this long on its blow (picture only).
    private const float Stop = .06f;
    // The marks on the floor flare this long after the last cut, and the veils of water are drawn in with them.
    private const float FlareAfter = .08f, FlareSeconds = .30f;
    // Her own dashes and the hop back leave frozen copies of her at these ages.
    private static readonly float[] GhostAges = { .075f, .115f, .37f, .41f, .45f, 2.28f, 2.36f, 2.46f };
    private const float GhostFade = .17f;

    private readonly Transform owner;
    private readonly PNVisualBody body;
    private readonly float[] hits;
    private readonly float duration;
    private readonly PNGuraEchoRush.Double[] doubles = new PNGuraEchoRush.Double[Slots * Trail];
    private readonly PNGuraSlashArc[] crescents = new PNGuraSlashArc[Cuts], lines = new PNGuraSlashArc[Cuts];
    private readonly PNGuraEchoRush.Silhouette[] ghosts = new PNGuraEchoRush.Silhouette[8];
    private readonly PNGuraWaterFx water, floor;
    private Material material;
    private IPNCameraHandle shake;
    private Vector3 enemy, ghostFrom;
    private float previous = -1f;
    private float scale = 1f;
    private bool anchored, shaken, disposed;

    public PNGuraShadowCuts(Transform owner, PNVisualBody body, float[] hits, float duration)
    {
        this.owner = owner;
        this.body = body;
        this.hits = hits;
        this.duration = duration;
        water = new PNGuraWaterFx(owner, "Gura shadow cut water");
        floor = new PNGuraWaterFx(owner, "Gura shadow cut floor", 2950);
        for (int i = 0; i < Cuts; i++)
        {
            // Thinner and paler than her own slashes: these are shadows' cuts.
            float strength = i == Cuts - 1 ? .56f : .46f;
            crescents[i] = PNGuraSlashArc.Create(owner, false, CutClock)?.AsShadow(strength);
            lines[i] = PNGuraSlashArc.Create(owner, false, CutClock)?.AsShadow(strength);
        }
        material = PNGuraEchoRush.CreateMaterial("Gura shadow cut");
    }

    // target = where the struck enemy is taken to be; aim = the way of the cast; radius = the reach of the seven cuts.
    public void Draw(float age, Vector3 target, Vector3 aim, float radius, bool seeking)
    {
        if (disposed || owner == null || hits.Length < FirstHit + Cuts) return;
        var actor = body != null ? body.Actor : null;
        float last = hits[FirstHit + Cuts - 1], flare = last + FlareAfter;
        float opening = hits[FirstHit] - ShowBefore;
        scale = radius / DrawnReach;
        if (!anchored || seeking || age < opening)
        {
            enemy = target;
            anchored = age >= opening;
        }
        Vector3 ground = new(enemy.x, owner.position.y + .03f, enemy.z);
        var camera = PNViewCamera.Current;
        Vector3 right = camera != null ? camera.transform.right : Vector3.right;
        Vector3 up = camera != null ? camera.transform.up : Vector3.up;
        Vector3 toward = Vector3.Cross(right, up);

        water.Begin();
        floor.Begin();
        DrawGhosts(actor, age, seeking, toward);

        // The floor darkens wet while the shadows work.
        float wet = Mathf.InverseLerp(opening - .08f, hits[FirstHit] + .1f, age) * (1f - Mathf.InverseLerp(flare + .1f, duration - .02f, age));
        if (wet > 0f) floor.WetShade(ground, aim, 2f * scale, 2f * scale, .42f * wet);

        for (int slot = 0; slot < Slots; slot++)
        {
            int index = -1;
            for (int i = slot; i < Cuts; i += Slots)
                if (age >= hits[FirstHit + i] - ShowBefore && age < hits[FirstHit + i] + GoneAfter + (i == Cuts - 1 ? Stop : 0f)) index = i;
            if (index < 0 || actor == null || material == null)
            {
                for (int t = 0; t < Trail; t++) doubles[slot * Trail + t]?.Hide();
                continue;
            }
            DrawShadow(actor, slot, index, age, ground, aim, toward);
        }
        for (int i = 0; i < Cuts; i++) DrawCut(i, age, ground, aim, right, up, toward);

        DrawVeils(age, ground, toward);
        DrawMarks(age, flare, ground, aim);

        PlayShake(age, last, seeking);
        floor.Draw();
        water.Draw();
        previous = age;
    }

    private Vector3 Out(int index, Vector3 aim) => Quaternion.AngleAxis(Bearings[index], Vector3.up) * -aim;

    // How far to the side of the enemy shadow `index` passes.
    private Vector3 Aside(int index, Vector3 aim) => Vector3.Cross(Vector3.up, Out(index, aim)).normalized * (Miss[index] * scale);

    // Where shadow `index` is on its way past the enemy (0..1).
    private Vector3 Way(int index, float p, Vector3 ground, Vector3 aim)
    {
        Vector3 at = ground + Aside(index, aim) + Out(index, aim) * (Mathf.LerpUnclamped(From[index], -To[index], p) * scale);
        at.y = ground.y + Mathf.Lerp(Starts[index], Ends[index], Mathf.Clamp01(p));
        return at;
    }

    // The age as the picture of cut `index` sees it: the last one stands still for a moment on its blow.
    private float Since(int index, float age)
    {
        float since = age - hits[FirstHit + index];
        if (index != Cuts - 1 || since <= .012f) return since;
        return since < .012f + Stop ? .012f : since - Stop;
    }

    private void DrawShadow(PNVisualActor actor, int slot, int index, float age, Vector3 ground, Vector3 aim, Vector3 view)
    {
        float since = Since(index, age);
        bool final = index == Cuts - 1;
        Vector3 heading = -Out(index, aim);
        heading.Normalize();
        float side = index % 2 == 0 ? 1f : -1f;
        float forming = 1f - Mathf.InverseLerp(-ShowBefore, -ShowBefore + .045f, since);
        float breaking = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.045f, GoneAfter, since));
        float run = Mathf.InverseLerp(-DashBefore, DashAfter, since);
        for (int t = Trail - 1; t >= 0; t--)
        {
            var current = doubles[slot * Trail + t];
            float past = run - .17f * t;
            // The afterimages exist only while it is under way.
            if (t > 0 && (past <= 0f || since > DashAfter + .05f)) { current?.Hide(); continue; }
            current ??= doubles[slot * Trail + t] = new PNGuraEchoRush.Double(owner, actor, material, slot * Trail + t);
            float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(past));
            float swing = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.06f, .035f, since - .02f * t));
            string motion = final ? "attack_4" : index % 2 == 0 ? "attack_1" : "attack_2";
            float phase = final ? Mathf.Lerp(.22f, .34f, swing) / .85f : Mathf.Lerp(.12f, .27f, swing) / .45f;
            Vector3 axis = final
                ? heading * Mathf.Cos(Mathf.Lerp(112f, -24f, swing) * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(Mathf.Lerp(112f, -24f, swing) * Mathf.Deg2Rad)
                : Quaternion.AngleAxis(side * Mathf.Lerp(-100f, 70f, swing), Vector3.up) * heading + Vector3.up * Mathf.Clamp((Ends[index] - Starts[index]) * .5f, -.4f, .4f);
            float opacity = t == 0 ? .98f : t == 1 ? .46f : .24f;
            float dissolve = t == 0 ? Mathf.Max(forming, breaking) : Mathf.Max(.2f * t, breaking);
            current.Hold(actor, Way(index, p, ground, aim), heading, motion, phase, age, dissolve, opacity, Vector3.up * .22f + heading * .07f, axis);
        }

        // Thin lines of speed behind it.
        float speeding = Mathf.InverseLerp(-DashBefore, -DashBefore + .03f, since) * (1f - Mathf.InverseLerp(DashAfter - .02f, DashAfter + .07f, since));
        if (speeding > 0f)
        {
            Vector3 head = Way(index, Mathf.SmoothStep(0f, 1f, run), ground, aim);
            Vector3 tail = Way(index, Mathf.Max(-.15f, run - .55f), ground, aim);
            Vector3 across = Vector3.Cross(Vector3.up, heading);
            for (int i = 0; i < 4; i++)
            {
                Vector3 shift = across * ((i - 1.5f) * .13f) + Vector3.up * (.12f + .17f * Mathf.Repeat(i * .618034f, 1f));
                water.Streak(tail + shift, Vector3.Lerp(tail, head, .8f + .05f * i) + shift, .016f, speeding * .8f, view);
            }
        }

        // It rises out of a flat ring on the floor under where it appears.
        float born = (since + ShowBefore) / .17f;
        if (born >= 0f && born < 1f)
        {
            Vector3 under = Way(index, 0f, ground, aim);
            under.y = ground.y + .012f;
            floor.Ripple(under, Mathf.Lerp(.06f, .46f, born), .045f, (1f - born) * .95f);
            floor.Ripple(under, Mathf.Lerp(.03f, .27f, born), .025f, (1f - born) * .65f);
        }
    }

    // The cut itself and what it knocks out of the enemy.
    private void DrawCut(int index, float age, Vector3 ground, Vector3 aim, Vector3 right, Vector3 up, Vector3 view)
    {
        var crescent = crescents[index];
        var line = lines[index];
        float since = Since(index, age);
        bool final = index == Cuts - 1;
        Vector3 heading = -Out(index, aim);
        heading.Normalize();
        float side = index % 2 == 0 ? 1f : -1f;
        float rise = Ends[index] - Starts[index];
        float middle = (Starts[index] + Ends[index]) * .5f;

        // The same crescent as her normal attacks, leaning the way this shadow falls or rises, centred a little short of the enemy.
        float size = (final ? 1.8f : 1.35f) * scale;
        Vector3 aside = Aside(index, aim);
        Vector3 centre = ground + aside * .55f - heading * (size * .55f) + Vector3.up * (.22f + middle * .8f);
        crescent?.Draw(centre, heading, size, size * .3f, final ? 180f : 160f, side, final ? since * 1.8f : since,
            (s, r) => PNGuraSlashArc.TiltLift(s, r, 160f) * .6f + rise * (s - .5f) * .9f);

        // The straight stroke along the dash.
        Vector3 a = Way(index, .12f, ground, aim) + Vector3.up * .3f, b = Way(index, .93f, ground, aim) + Vector3.up * .3f;
        line?.DrawLine(a, b, (final ? .42f : .23f) * scale, since, Mathf.InverseLerp(-.055f, .012f, since), final ? .95f : .8f, since);
        float core = Mathf.InverseLerp(-.04f, 0f, since) * (1f - Mathf.InverseLerp(.02f, .09f, since));
        if (core > 0f) water.Cut(a, b, (final ? .06f : .03f) * scale, core * .7f);

        if (since < 0f || since > .36f) return;
        Vector3 body_ = ground + aside * .12f + Vector3.up * (.28f + middle * .45f);
        float strength = final ? 1.5f : 1f;
        // A short white flash,
        float flash = 1f - Mathf.InverseLerp(.0f, .065f, since);
        if (flash > 0f) water.Puff(body_, (.14f + .2f * (1f - flash)) * strength, flash * .95f, right, up, index * .17f);
        // sharp shards of foam on along the cut,
        float shards = since / .17f;
        if (shards < 1f)
            for (int i = 0; i < (final ? 15 : 9); i++)
            {
                float seed = Mathf.Repeat(i * .618034f + index * .21f, 1f), other = Mathf.Repeat(i * .414214f + .3f, 1f);
                Vector3 way = (Quaternion.AngleAxis((seed - .5f) * 70f, Vector3.up) * heading + Vector3.up * ((other - .35f) * .8f + rise * .35f)).normalized;
                float flown = (1f - (1f - shards) * (1f - shards)) * (.5f + .8f * other) * strength;
                water.Streak(body_ + way * (.12f + flown * .55f), body_ + way * (.30f + flown), .035f * strength * (1f - shards), 1f - shards * shards, view);
            }
        // and a ripple running over the wet floor.
        float spread = since / .3f;
        if (spread < 1f) floor.Ripple(ground + Vector3.up * .008f, Mathf.Lerp(.2f, 1.7f * strength * scale, Mathf.Sqrt(spread)), .04f, (1f - spread) * .38f);
    }

    // Veils of water circling the enemy low: faster and tighter with every cut, drawn in as the marks flare.
    private void DrawVeils(float age, Vector3 ground, Vector3 view)
    {
        float begin = hits[FirstHit] - .14f, flare = hits[FirstHit + Cuts - 1] + FlareAfter;
        if (age < begin || age > flare + .06f) return;
        float done = 0f;
        for (int i = 0; i < Cuts; i++) done += Mathf.InverseLerp(0f, .1f, age - hits[FirstHit + i]) / Cuts;
        float run = Mathf.Min(age, flare) - begin;
        float turn = 170f * run + 170f * run * run;     // degrees: faster and faster
        float pull = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(flare - .07f, flare + .06f, age));
        float shown = Mathf.InverseLerp(begin, begin + .15f, age) * (1f - pull);
        Vector3 viewer = new Vector3(-view.x, 0f, -view.z).normalized;
        for (int i = 0; i < 3; i++)
        {
            float far = Mathf.Lerp(1.65f - .2f * i, 1.1f - .15f * i, done) * (1f - .7f * pull) * scale;
            Vector3 foot = ground + Vector3.up * (.05f + .2f * i);
            water.Swirl(foot, far * 1.05f, far * .92f, .16f + .08f * i, turn * (1f - .13f * i) + 127f * i, 150f + 40f * done, (.07f + .05f * done) * scale,
                shown * Mathf.Lerp(.30f, .52f, done) * (1f - .15f * i), viewer);
        }
    }

    // Every cut leaves a thin bright line on the wet floor under its path; they stay, dim, and flare together as she forms again.
    private void DrawMarks(float age, float flare, Vector3 ground, Vector3 aim)
    {
        for (int i = 0; i < Cuts; i++)
        {
            float since = age - hits[FirstHit + i];
            if (since < 0f) continue;
            Vector3 a = Way(i, .08f, ground, aim), b = Way(i, .92f, ground, aim);
            a.y = b.y = ground.y + .004f * (i + 1) / Cuts;
            float lit = Mathf.Lerp(.95f, .34f, Mathf.Sqrt(Mathf.Clamp01(since / .55f)));
            float late = age - flare;
            if (late >= 0f) lit = Mathf.Lerp(lit, 1f, Mathf.Clamp01(late / .05f)) * (1f - Mathf.InverseLerp(.08f, FlareSeconds, late));
            if (lit <= 0f) continue;
            floor.Cut(a, b, .045f * scale * (late >= 0f ? 1.6f : 1f), lit);
        }
    }

    // Her dashes: two or three frozen copies of her left behind, with a few lines of speed.
    private void DrawGhosts(PNVisualActor actor, float age, bool seeking, Vector3 view)
    {
        for (int i = 0; i < GhostAges.Length; i++)
        {
            float since = age - GhostAges[i];
            if (since < 0f || seeking)
            {
                ghosts[i]?.Dispose();
                ghosts[i] = null;
                continue;
            }
            if (ghosts[i] == null)
            {
                // Taken as she passes; a frame that comes much too late leaves it out.
                if (previous >= GhostAges[i] || since > .07f || actor == null || actor.Mesh == null || material == null) continue;
                ghosts[i] = PNGuraEchoRush.Silhouette.Freeze(actor, material, "Gura dash copy", owner);
            }
            float k = since / GhostFade;
            if (k >= 1f) { ghosts[i].Hide(); continue; }
            ghosts[i].DrawFrozen(age, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.2f, 1f, k)), true, .6f * (1f - .5f * k));
        }
        bool dashing = age > .05f && age < .20f || age > .34f && age < .51f || age > 2.24f && age < 2.62f;
        if (!dashing) { ghostFrom = owner.position; return; }
        Vector3 moved = owner.position - ghostFrom;
        moved.y = 0f;
        if (moved.sqrMagnitude < .04f) return;
        Vector3 along = moved.normalized, across = Vector3.Cross(Vector3.up, along);
        Vector3 head = owner.position - along * .12f, tail = head - along * Mathf.Min(1.3f, moved.magnitude);
        for (int i = 0; i < 4; i++)
        {
            Vector3 shift = across * ((i - 1.5f) * .11f) + Vector3.up * (.1f + .15f * Mathf.Repeat(i * .618034f, 1f));
            water.Streak(tail + shift, head + shift, .015f, .75f, view);
        }
    }

    private void PlayShake(float age, float last, bool seeking)
    {
        if (!shaken && !seeking && previous >= 0f && previous < last && age >= last && Application.isPlaying)
        {
            shaken = true;
            shake = PNCameraServices.Current.RequestShake(owner.gameObject, "gura.shadow_cut",
                new PNShakeSpec(.075f, 28f, .18f, .13f, 0f, PNShakeShape.Recoil, .68f, 1f, .12f, 7f, PNShakeFalloff.Quadratic));
        }
    }

    public void Hide()
    {
        water.Hide();
        floor.Hide();
        foreach (var item in doubles) item?.Hide();
        foreach (var arc in crescents) arc?.Hide();
        foreach (var arc in lines) arc?.Hide();
        for (int i = 0; i < ghosts.Length; i++) { ghosts[i]?.Dispose(); ghosts[i] = null; }
        shake?.Dispose();
        shake = null;
        previous = -1f;
    }

    public void Dispose()
    {
        if (disposed) return;
        Hide();
        disposed = true;
        water.Dispose();
        floor.Dispose();
        foreach (var item in doubles) item?.Dispose();
        foreach (var arc in crescents) arc?.Dispose();
        foreach (var arc in lines) arc?.Dispose();
        if (material != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(material); else UnityEngine.Object.DestroyImmediate(material); }
        material = null;
    }
}
#endif
