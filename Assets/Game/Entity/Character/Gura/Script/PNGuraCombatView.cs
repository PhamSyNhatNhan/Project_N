#if UNITY_5_3_OR_NEWER
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal sealed class PNGuraCombatView : IPNActionView
{
    private static readonly ConditionalWeakTable<PNVisualBody, PNGuraCombatView> PoseOwners = new();
    private readonly Transform owner;
    private readonly PNVisualBody body;
    private readonly Vector3 direction;
    private readonly string state;
    private readonly bool fixedSweep;
    private readonly bool rush, finisher;
    // The two forms of the Skill: the whirl of the near one and the stab that ends the far one.
    private readonly bool whirl, lunge;
    private readonly float[] hitRadii, hitAngles;
    // The rush: how long it gathers before it goes, how long the way takes (the Action's `travel` fact; below 0 until it is known).
    private readonly float pause;
    private float travel = -1f;
    // The water over her whole body on the way (the same sheet as attack 3's) and its ring of light; the water around the stab's trident.
    private Material coatMaterial, coatRingMaterial;
    private PNGuraEchoRush.Silhouette coat, coatRing;
    private PNVisualActor coated;
    private PNGuraAttackWeapon sheath;
    // The wave, the water on the floor and their fading after the Action (a picture of its own, like the Dash's).
    private PNGuraRushSurf surf;
    private bool surfTail;
    // The near form: her dashes and the seven water shadows; how far short of the enemy her second dash ends.
    private PNGuraShadowCuts shadows;
    private readonly float nearStand;
    // Where she looks: the cast aim, then where the Action turned its last hit (`aim` fact).
    private Vector3 facing;
    private const float SweepLeadFraction = .86f;
    private const float SweepFollowSeconds = .089f;
    // Attacks 1-2: the trident crosses the whole hit angle in this window around the hit, just ahead of the water.
    private const float SwingLead = .072f, SwingFollow = .014f;
    private readonly float innerRadius, halfThickness;
    private readonly float sweepHeight;
    private readonly System.Func<float, float, float> sweepLift;
    private readonly PNGuraSlashArc[] slashes;
    private readonly float[] hitAges;
    // When the near form lets its two flying slashes go (empty otherwise).
    private readonly float[] shotAges;
    private readonly Vector3[] hitDirections;
    private readonly Vector3[] hitOrigins;
    private readonly bool[] sweepAnchored;
    private readonly PNGuraAttackWeapon[] ghosts;
    private readonly PNGuraSharkBite shark;
    private readonly PNGuraDiveVisual dive;
    private readonly PNGuraBurstLeap leap;
    private readonly float duration, radius, angle;
    private readonly PNGuraWaterFx water;
    private readonly PNGuraWaterFx accents;
    private readonly PNGuraEchoRush echo;
    private readonly PNGuraWaterParticles particles;
    private readonly PNGuraAttackWeapon weapon;
    private const int GripHistorySize = 24;
    private readonly float[] gripTimes = new float[GripHistorySize];
    private readonly Vector3[] gripPositions = new Vector3[GripHistorySize];
    private Vector3 grip;
    private Vector3 biteOrigin;
    private Vector3? biteStepEnd;
    private bool bitePlaced;
    private int gripCount, gripNext;
    private bool ended, hidden;
    private float previousFinisherAge = -1f;
    private bool finisherShook;
    private IPNCameraHandle finisherShake;
    private readonly PNGuraDashVisual dash;
    private bool dashTail;
    // The Dash goes on playing after its 0.16 s: the picture advances itself from there.
    public bool HasTail => dashTail && dash != null || surfTail && surf != null || shark != null && shark.HasTail;
    public bool Lead { private get; set; } = true;

    public static IPNActionView CreateStrike(PNActionViewBuild build) =>
        new PNGuraCombatView(build, ((JObject)build.Data.Action.Perform)["visualState"].Value<string>());
    public static IPNActionView CreateThrow(PNActionViewBuild build) => new PNGuraCombatView(build, "throw");
    public static IPNActionView CreateDash(PNActionViewBuild build) => new PNGuraCombatView(build, "dash");

    private PNGuraCombatView(PNActionViewBuild build, string state)
    {
        owner = build.Skill.transform;
        body = build.Skill.GetComponent<PNVisualBody>();
        this.state = state;
        fixedSweep = state == "attack_1" || state == "attack_2";
        rush = state == "attack_3";
        finisher = state == "attack_4";
        whirl = state == "vortex";
        lunge = state == "rush";
        direction = Flat(build.Direction);
        facing = direction;
        var payload = (JObject)build.Data.Action.Perform;
        var hits = payload["hits"] as JArray;
        hitAges = hits != null ? hits.Select(x => x["at"].Value<float>()).ToArray() : System.Array.Empty<float>();
        hitOrigins = new Vector3[hitAges.Length];
        shotAges = payload["shots"] is JArray shots ? shots.Select(x => x["at"].Value<float>()).ToArray() : System.Array.Empty<float>();
        duration = state == "throw" ? payload["windup"].Value<float>() : payload["duration"].Value<float>();
        radius = payload["radius"] != null ? PNHitCapture.ScaleLength(payload["radius"].Value<float>(), build.Scale) : 0;
        angle = payload["angle"]?.Value<float>() ?? 0;
        bool attack = state.StartsWith("attack_", System.StringComparison.Ordinal);
        if (lunge) pause = PNComboStrikeAction.Steps(payload["step"]) is { Count: > 0 } moves ? moves[0]["at"].Value<float>() : 0f;
        hitRadii = new float[hitAges.Length]; hitAngles = new float[hitAges.Length];
        for (int i = 0; i < hitAges.Length; i++)
        {
            float own = hits[i]["radius"]?.Value<float>() ?? 0f;
            hitRadii[i] = own > 0f ? PNHitCapture.ScaleLength(own, build.Scale) : radius;
            hitAngles[i] = hits[i]["angle"]?.Value<float>() ?? angle;
        }
        attack |= whirl;
        if (whirl) nearStand = PNComboStrikeAction.Steps(payload["step"]) is { Count: > 1 } sides ? sides[1]["stopDistance"].Value<float>() : 1f;
        if (state == "dive")
        {
            particles = PNGuraWaterParticles.Create(owner, state);
            dive = PNGuraDiveVisual.Create(owner, direction);
        }
        if (state == "dash") dash = PNGuraDashVisual.Create(owner, direction, duration, payload["distance"]?.Value<float>() ?? 0f);
        else if (state != "shark" && state != "dive" && state != "burst") water = new PNGuraWaterFx(owner);
        if (state == "burst" && PNComboStrikeAction.Steps(payload["step"]) is { Count: > 0 } jump)
            leap = new PNGuraBurstLeap(owner, jump[0]["at"].Value<float>() + jump[0]["duration"].Value<float>(), jump[0]["height"]?.Value<float>() ?? 0f, radius, duration);
        if (finisher) accents = new PNGuraWaterFx(owner, "Gura finisher water accents");
        if (rush)
        {
            var steps = PNComboStrikeAction.Steps(payload["step"]);
            echo = new PNGuraEchoRush(owner, body, direction, hitAges, radius,
                steps.Count > 0 ? steps[0]["at"].Value<float>() + steps[0]["duration"].Value<float>() : 0f);
        }
        if (attack && !rush)
        {
            hitDirections = hits != null ? hits.Select(hit => PNComboStrikeAction.HitDirection(direction, hit)).ToArray() : System.Array.Empty<Vector3>();
            sweepHeight = payload["height"]?.Value<float>() ?? .3f;
            innerRadius = PNHitCapture.ScaleLength(payload["innerRadius"]?.Value<float>() ?? 0f, build.Scale);
            halfThickness = PNHitCapture.ScaleLength(payload["thickness"]?.Value<float>() ?? .45f, build.Scale) * .5f;
            float sweepAngle = angle;
            if (fixedSweep) sweepLift = (swing, reach) => PNGuraSlashArc.TiltLift(swing, reach, sweepAngle);
            else if (whirl)
            {
                // Her two slashes of the near form run close to the floor.
                sweepHeight = .08f;
                float low = hits != null && hits.Count > 0 ? hits[0]["angle"]?.Value<float>() ?? angle : angle;
                sweepLift = (swing, reach) => PNGuraSlashArc.TiltLift(swing, reach, low) * .55f;
            }
            else sweepLift = AttackLift;
            sweepAnchored = new bool[hitAges.Length];
            // The near form draws its own two crossed slashes; its later blows are the doubles' picture.
            slashes = new PNGuraSlashArc[hitAges.Length];
            for (int i = 0; i < (whirl ? System.Math.Min(2, slashes.Length) : slashes.Length); i++) slashes[i] = PNGuraSlashArc.Create(owner, finisher, 1f);
        }
        if (state == "shark") shark = PNGuraSharkBite.Create(owner);
        if (attack && !rush || state == "throw" || lunge) weapon = new PNGuraAttackWeapon(owner, state == "throw" ? "Gura windup trident" : "Gura attack trident");
        if (attack && !rush)
        {
            ghosts = new PNGuraAttackWeapon[fixedSweep ? 2 : 3];
            for (int i = 0; i < ghosts.Length; i++)
                ghosts[i] = new PNGuraAttackWeapon(owner, "Gura trident afterimage " + i, true);
        }
    }

    public void Advance(double age, double rate, bool seeking)
    {
        if (ended || owner == null) return;
        float t = (float)age;
        Vector3 origin = owner.position;
        bool resuming = hidden || seeking;
        hidden = false;
        if (Lead && state != "dash" && (state != "throw" || t < duration))
        {
            if (body != null && Pose(age, resuming))
            {
                // Every pose of Gura's actions (strikes, the throw, the bite) looks where the cast is aimed. A dive keeps the way she already faces.
                if (state != "dive") body.FaceAction(facing);
                if (!PoseOwners.TryGetValue(body, out var active) || !ReferenceEquals(active, this))
                {
                    PoseOwners.Remove(body);
                    PoseOwners.Add(body, this);
                }
            }
        }
        else ReleasePose();

        if (dash != null)
        {
            dash.Run(t, resuming);
            return;
        }

        if (state == "throw")
        {
            float phase = Mathf.Clamp01(t / Mathf.Max(.001f,duration));
            float cast = Mathf.SmoothStep(0f,1f,phase);
            Vector3 axis = (Quaternion.AngleAxis(Mathf.Lerp(-136f,-5f,cast),Vector3.up) * direction
                + Vector3.up * Mathf.Lerp(.72f,.12f,cast)).normalized;
            var pose = new PNGuraAttackWeapon.Pose(origin + Vector3.up*.16f, axis, Vector3.Cross(Vector3.up,axis).normalized,
                Mathf.Lerp(.37f,.57f,cast), .22f, .065f * Mathf.Sin(phase*Mathf.PI));
            weapon.Draw(pose, Lead && t < duration ? Mathf.Clamp01(t/.045f) : 0f);
            water.Begin();
            if (Lead && t < duration)
            {
                float charge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.19f, .82f, phase));
                water.TridentStream(pose, t, pose.Length * .61f, charge);
                // Water gathers: a pool and a ring closing on her feet.
                Eye(out var right, out var up);
                Vector3 feetAt = origin + Vector3.up * .045f;
                water.Pool(feetAt, Mathf.Lerp(.16f, .3f, phase), t, .75f * charge);
                water.Ripple(feetAt, Mathf.Lerp(.7f, .18f, phase), .05f, Mathf.Sin(Mathf.PI * phase));
                // Beads drawn to the prongs.
                Vector3 prong = pose.At(1f);
                for (int i = 0; i < 14; i++)
                {
                    float seed = Mathf.Repeat(i * .618034f + .15f, 1f);
                    float in_ = Mathf.Repeat(t * 3.2f + seed, 1f);
                    float turn = i * 2.39996f + t * 6f;
                    Vector3 from = prong + new Vector3(Mathf.Cos(turn), .55f * Mathf.Sin(turn * 1.7f), Mathf.Sin(turn)) * .42f;
                    water.Bead(Vector3.Lerp(from, prong, in_ * in_), (.018f + .026f * seed) * (1f - in_ * .5f), charge * Mathf.Sin(Mathf.PI * in_), right, up);
                }
            }
            water.Draw();
            return;
        }

        water?.Begin();
        accents?.Begin();
        if (dive != null)
        {
            particles?.Sample(origin + Vector3.up*.029f,Vector3.zero,new Vector3(.22f,.12f,.22f),.12f+t,resuming);
            dive.Draw(origin,t,duration,resuming);
        }
        else if (shark != null) DrawBite(origin,t,resuming);
        else if (leap != null) leap.Draw(t, resuming, Lead && body != null ? body.Actor : null);
        else if (echo != null)
        {
            if (Lead) echo.Draw(t, resuming);
            else echo.Hide();
        }
        else if (lunge) DrawLunge(origin,t,resuming);
        else DrawAttack(origin,t,resuming);
        water?.Draw();
        accents?.Draw();
    }

    // EN: The Skill's two strikes have no motion of their own yet (rough pictures): they borrow the attack motions, each placed so that its blow falls on the Skill's hit.
    private bool Pose(double age, bool seeking)
    {
        // The near form's own motion, once Gura's content carries it; until then borrowed attack poses, each placed so that its blow falls on a hit.
        if (whirl && body.PlayActionAt("vortex", System.Math.Min(age, duration), duration, seeking)) return true;
        if (whirl && hitAges.Length > 2)
        {
            // Without its own motion: attacks 1 and 2 for the two slashes, and again for the two thrown ones.
            bool second;
            double local;
            if (age >= 1.9 && shotAges.Length > 0)
            {
                second = shotAges.Length > 1 && age >= (shotAges[0] + shotAges[1]) * .5;
                local = .17 + age - shotAges[second ? 1 : 0];
            }
            else
            {
                second = age >= (hitAges[0] + hitAges[1]) * .5;
                local = .17 + age - hitAges[second ? 1 : 0];
            }
            return body.PlayActionAt(second ? "attack_2" : "attack_1", System.Math.Max(0d, System.Math.Min(.45, local)), .45, seeking);
        }
        if (lunge) return body.PlayActionAt("attack_1", System.Math.Max(0d, System.Math.Min(.45, .17 + age - (Arrival + hitAges[0] - pause))), .45, seeking);
        return body.PlayActionAt(state, age, state == "throw" ? duration * 2.2f : duration, seeking);
    }

    // EN: "She turns to water and rushes": water climbs over her whole body (the sheet of attack 3), a breaking wave carries her to the struck enemy, over her; at the
    //     arrival the wave falls in and spreads back over the floor (PNGuraRushSurf) while the coat melts off her from the head down. Her puppet is never hidden (the wave may cover it).
    private void DrawLunge(Vector3 origin, float age, bool seeking)
    {
        if (!Lead) { weapon.Hide(); HideLunge(); return; }
        float arrive = Arrival;
        bool arrived = age >= arrive && (travel >= 0f || age > pause + .04f);
        float out_ = age - arrive;

        var actor = body != null ? body.Actor : null;
        if (actor != null && actor.Renderer != null)
        {
            if (coatMaterial == null)
            {
                coatMaterial = PNGuraEchoRush.CreateMaterial("Gura rush coat");
                coatRingMaterial = PNGuraEchoRush.CreateMaterial("Gura rush coat ring", true);
            }
            if (coated != actor && coatMaterial != null && coatRingMaterial != null)
            {
                coat?.Dispose(); coatRing?.Dispose();
                coat = new PNGuraEchoRush.Silhouette(actor, coatMaterial, "Gura rush coat sheet");
                coatRing = new PNGuraEchoRush.Silhouette(actor, coatRingMaterial, "Gura rush coat ring", true);
                coated = actor;
            }
            // Climbs from her feet before she goes, whole on the way, melts away from the head down after the arrival.
            float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / Mathf.Max(.01f, pause * .8f)));
            float melt = arrived ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.07f, .27f, out_)) : 0f;
            bool rising = !arrived && rise < 1f;
            float dissolve = rising ? Mathf.Lerp(.995f, 0f, rise) : melt;
            coat?.Draw(actor, age, dissolve, true, .96f, rising);
            coatRing?.Draw(actor, age, dissolve, true, .9f * (1f - melt), rising, 1f, .03f);
        }

        surf ??= PNGuraRushSurf.Create(owner, pause);
        surf.Run(age, origin, facing, travel, seeking);

        if (!arrived)
        {
            weapon.Hide(); sheath?.Hide();
            return;
        }

        Vector3 side = Vector3.Cross(Vector3.up, facing).normalized;
        float end = duration + Mathf.Max(0f, travel);
        // The trident is whole or gone: a part-faded solid reads as a black shape.
        bool held = end - age > .05f;
        var pose = new PNGuraAttackWeapon.Pose(origin + Vector3.up * .18f + facing * .14f, facing, -side, .72f, .26f, 0f);
        weapon.Draw(pose, held ? 1f : 0f);
        sheath ??= new PNGuraAttackWeapon(owner, "Gura rush trident water", true);
        sheath.Draw(new PNGuraAttackWeapon.Pose(pose.Grip, pose.Axis, pose.Across, pose.Length, pose.Width * 1.6f, 0f),
            held ? .9f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.07f, .24f, out_))) : 0f);
        float since = age - (arrive + hitAges[0] - pause);
        water.TridentStream(pose, age, 1.5f, (held ? 1f : 0f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, .12f, since))));
    }

    // The near form's middle: water climbs over her as she flies to the other side (0.30-0.38 s) and she is gone into it (0.60-0.68 s); she forms again out of it above the enemy (2.06-2.16 s)
    // and it melts off her from the head down around the chop.
    // Returns how much of her is water: 0 herself, 1 gone.
    private const float WaterFrom = .30f, WaterFull = .38f, GoneFrom = .60f, Gone = .69f, BackFrom = 2.07f, Back = 2.16f, MeltFrom = 2.18f, Melted = 2.36f;

    private float TurnToWater(float age)
    {
        float away = age < Back ? Mathf.InverseLerp(GoneFrom, Gone, age) : 0f;
        if (age >= BackFrom) away = 1f - Mathf.InverseLerp(BackFrom, Back, age);
        if (away > 0f) PNGuraSubmerge.Hold(body, this, 1f - away);
        else PNGuraSubmerge.Release(body, this);

        bool going = age >= WaterFrom && age < Gone + .08f;
        bool coming = age >= BackFrom - .03f && age < Melted;
        var actor = body != null ? body.Actor : null;
        if (!(going || coming) || actor == null || actor.Renderer == null)
        {
            coat?.Hide(); coatRing?.Hide();
            return away;
        }
        if (coatMaterial == null)
        {
            coatMaterial = PNGuraEchoRush.CreateMaterial("Gura water body");
            coatRingMaterial = PNGuraEchoRush.CreateMaterial("Gura water body ring", true);
        }
        if (coated != actor && coatMaterial != null && coatRingMaterial != null)
        {
            coat?.Dispose(); coatRing?.Dispose();
            coat = new PNGuraEchoRush.Silhouette(actor, coatMaterial, "Gura water body sheet");
            coatRing = new PNGuraEchoRush.Silhouette(actor, coatRingMaterial, "Gura water body outline", true);
            coated = actor;
        }
        bool rising = going ? age < WaterFull : age < Back;
        float dissolve;
        if (going) dissolve = rising ? Mathf.Lerp(.995f, 0f, Mathf.InverseLerp(WaterFrom, WaterFull, age)) : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Gone - .03f, Gone + .08f, age));
        else dissolve = rising ? Mathf.Lerp(.9f, 0f, Mathf.InverseLerp(BackFrom - .03f, Back, age)) : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MeltFrom, Melted, age));
        coat?.Draw(actor, age, dissolve, true, .96f, rising);
        coatRing?.Draw(actor, age, dissolve, true, .9f * (1f - dissolve), rising, 1f, .03f);
        return away;
    }

    private void HideLunge()
    {
        coat?.Hide(); coatRing?.Hide(); sheath?.Hide();
        if (surf != null) surf.Hide();
    }

    private void DisposeLunge()
    {
        coat?.Dispose(); coatRing?.Dispose(); sheath?.Dispose();
        coat = coatRing = null; coated = null; sheath = null;
        // A surf that was let go fades by itself.
        if (!surfTail && surf != null) { surf.Remove(); surf = null; }
        foreach (var material in new[] { coatMaterial, coatRingMaterial })
            if (material != null) { if (Application.isPlaying) Object.Destroy(material); else Object.DestroyImmediate(material); }
        coatMaterial = coatRingMaterial = null;
    }

    private float Arrival => pause + Mathf.Max(0f, travel);

    private static void Eye(out Vector3 right, out Vector3 up)
    {
        var camera = PNViewCamera.Current;
        right = camera != null ? camera.transform.right : Vector3.right;
        up = camera != null ? camera.transform.up : Vector3.up;
    }

    private void DrawBite(Vector3 origin, float age, bool seeking)
    {
        if (hitAges.Length == 0) return;
        float since = age - hitAges[0];
        if (since < PNGuraSharkBite.Start || since > PNGuraSharkBite.End)
        {
            shark.Hide();
            bitePlaced = false;
            return;
        }
        // Commit the line before the first visible frame. A late observer can
        // recover the pose at the caster even when the step fact was skipped.
        if (!bitePlaced || seeking)
        {
            biteOrigin = biteStepEnd ?? origin;
            bitePlaced = true;
        }
        // Coming up out of a dive, she stays out of sight behind the shark until its Action is over.
        if (PNGuraSubmerge.Held(body)) PNGuraSubmerge.Hold(body, this, 0f);
        shark.Draw(biteOrigin,direction,radius,since,seeking);
    }

    private void DrawAttack(Vector3 origin, float age, bool seeking)
    {
        if (!Lead || hitAges.Length == 0)
        {
            weapon.Hide();
            foreach (var ghost in ghosts) ghost.Hide();
            if (slashes != null) foreach (var slash in slashes) slash?.Hide();
            particles?.Hide();
            shadows?.Hide();
            gripCount = gripNext = 0;
            return;
        }

        var actor = body != null ? body.Actor : null;
        Vector3 feet = actor != null ? actor.transform.position : origin;
        grip = feet + Vector3.up * .13f + direction * (fixedSweep || whirl ? .28f : .16f);
        if (seeking) gripCount = gripNext = 0;
        int previous = (gripNext + GripHistorySize - 1) % GripHistorySize;
        if (gripCount == 0 || age - gripTimes[previous] > .001f)
        {
            gripTimes[gripNext] = age;
            gripPositions[gripNext] = grip;
            gripNext = (gripNext + 1) % GripHistorySize;
            gripCount = Mathf.Min(gripCount + 1, GripHistorySize);
        }
        int current = ClosestHit(age);
        for (int i = 0; i < hitAges.Length; i++)
        {
            float since = age - hitAges[i];
            if (since < 0f || seeking) sweepAnchored[i] = false;
            if (!sweepAnchored[i])
            {
                hitOrigins[i] = origin;
                sweepAnchored[i] = since >= 0f;
            }
            if (!whirl || i < 2) DrawSweep(i, since);
            if (finisher) DrawFinisherWater(i, since);
        }

        float water_ = 0f;
        if (whirl && hitAges.Length >= PNGuraShadowCuts.FirstHit + PNGuraShadowCuts.Cuts)
        {
            shadows ??= new PNGuraShadowCuts(owner, body, hitAges, duration);
            // The enemy is taken to be in front of where her second slash was made.
            Vector3 target = hitOrigins[1] + hitDirections[1] * nearStand;
            water_ = TurnToWater(age);
            shadows.Draw(age, target, direction, hitRadii[PNGuraShadowCuts.FirstHit], seeking);
        }
        if (finisher) PlayFinisherShake(age, seeking);
        float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / .055f));
        alpha *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((duration - age) / .11f));
        // While she is water there is no trident either (it is whole or gone: a part-faded solid reads as a black shape).
        if (water_ > .5f) alpha = 0f;
        weapon.Draw(BladeAt(current, age), alpha);
        for (int i = 0; i < ghosts.Length; i++)
        {
            float past = age - (.019f + i*.018f);
            int beat = ClosestHit(past);
            float travel = past - hitAges[beat];
            float opacity = alpha * (fixedSweep ? .15f - i * .05f : .29f - i*.075f);
            bool visible = past > 0f && travel > -.068f && travel < .075f && water_ <= 0f;
            ghosts[i].Draw(BladeAt(beat,past), visible ? opacity : 0f);
        }
    }

    // The picture of a sweep is the hit sector itself: centred where the hit is queried (the caster plus the registered height), out to the scaled radius, across the hit angle. Complete on the hit.
    private void DrawSweep(int hit, float since)
    {
        float side = state == "attack_2" || hit % 2 == 1 ? -1f : 1f;
        Vector3 centre = hitOrigins[hit] + Vector3.up * sweepHeight;
        Vector3 aim = hitDirections[hit];
        float radius = hitRadii[hit], angle = hitAngles[hit];
        var lift = sweepLift;
        slashes[hit]?.Draw(centre, aim, radius, innerRadius, angle, side, since, lift);
        float swept = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-PNGuraSlashArc.Lead, 0f, since));
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.012f, .085f, since));
        water.SectorEdge(centre, aim, radius, angle, side, swept, lift, finisher ? fade * .72f : fade);
    }

    private float AttackLift(float swing, float reach) =>
        Mathf.Lerp(.54f, -.19f, swing) * Mathf.Max(0f, halfThickness - .004f);

    private void PlayFinisherShake(float age, bool seeking)
    {
        float hit = hitAges[0];
        if (!finisherShook && !seeking && previousFinisherAge >= 0f && previousFinisherAge < hit && age >= hit && Application.isPlaying)
        {
            finisherShook = true;
            finisherShake = PNCameraServices.Current.RequestShake(owner.gameObject, "gura.finisher",
                new PNShakeSpec(.092f, 26f, .22f, .17f, 0f, PNShakeShape.Recoil, .68f, 1f, .12f, 7f, PNShakeFalloff.Quadratic));
        }
        previousFinisherAge = age;
    }

    // The finisher's water, one piece after the other: two thinner strokes chase the main one, a wall of water rises along the outer arc behind the swing, breaks into columns one after another
    // in the swing's direction, and leaves drops and a band of foam. The damage is the sector's; all of this is its picture.
    private void DrawFinisherWater(int hit, float since)
    {
        Vector3 aim = hitDirections[hit];
        Vector3 centre = hitOrigins[hit] + Vector3.up * sweepHeight;
        Vector3 ground = hitOrigins[hit] + Vector3.up * .045f;
        float side = hit % 2 == 1 ? -1f : 1f;
        accents.EdgeSpray(centre, aim, radius, angle, since, 54, radius * .25f);
        float swept = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-PNGuraSlashArc.Lead, 0f, since));
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.045f, .22f, since));
        accents.FinisherCrest(centre, aim, innerRadius, radius, angle, swept, fade, halfThickness, sweepLift);
        for (int stroke = 1; stroke <= 2; stroke++)
        {
            float late = since - .028f * stroke;
            float run = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-PNGuraSlashArc.Lead, 0f, late));
            float left = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.02f, .13f, late));
            accents.SectorEdge(centre, aim, radius * (1f - .17f * stroke), angle * (1f - .06f * stroke), side, run, sweepLift, left * (1f - .28f * stroke));
        }
        accents.BreakingWave(ground, aim, radius, angle, side, since, radius * .31f);

        const int columns = 5;
        for (int i = 0; i < columns; i++)
        {
            float local = since - (.075f + .026f * i);
            if (local < 0f) continue;
            float turn = side * Mathf.Lerp(-angle * .40f, angle * .40f, i / (columns - 1f));
            Vector3 at = ground + Quaternion.AngleAxis(turn, Vector3.up) * aim * (radius * .86f);
            float size = radius * (i == columns / 2 ? .108f : i % 2 == 0 ? .074f : .088f);
            float p = Mathf.Clamp01(local / .24f);
            if (p < 1f)
            {
                float jet = Mathf.Sin(Mathf.PI * Mathf.Lerp(.08f, 1f, p));
                accents.Crown(at, size * Mathf.Lerp(.45f, 1.25f, Mathf.Sqrt(p)), size * 3.6f * jet, since + i, (1f - p * p) * .95f);
                accents.Crown(at, size * Mathf.Lerp(.15f, .45f, p), size * 5.0f * jet, since + i + .6f, (1f - p * p) * .85f);
            }
            accents.Fountain(at, radius * .17f, .05f + local * 1.9f, 11);
        }

        float foam = Mathf.InverseLerp(.10f, duration - hitAges[hit] - .02f, since);
        if (foam > 0f && foam < 1f)
        {
            accents.ArcFoam(ground, aim, radius * Mathf.Lerp(.90f, 1.05f, 1f - (1f - foam) * (1f - foam)), angle, radius * .13f, 1f - foam * foam, since);
            accents.ArcFoam(ground + Vector3.up * .004f, aim, radius * Mathf.Lerp(.66f, .82f, foam), angle * .86f, radius * .075f, (1f - foam) * .75f, since + 1.3f);
        }
    }

    private PNGuraAttackWeapon.Pose BladeAt(int hit, float age)
    {
        float now = age - hitAges[hit];
        // The near form: two crossed slashes like attacks 1-2; after she forms again, two more that let the flying ones go (one "/", one "\").
        if (whirl && age < 1.9f) { hit = System.Math.Min(hit, 1); now = age - hitAges[hit]; }
        if (whirl && age >= 1.9f && shotAges.Length > 0)
        {
            int shot = shotAges.Length > 1 && age >= (shotAges[0] + shotAges[1]) * .5f ? 1 : 0;
            float cut = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(shotAges[shot] - .07f, shotAges[shot] + .025f, age));
            float turn = (shot == 0 ? 1f : -1f) * Mathf.Lerp(-95f, 70f, cut);
            Vector3 swung = (Quaternion.AngleAxis(turn, Vector3.up) * direction + Vector3.up * Mathf.Lerp(.75f, -.2f, cut)).normalized;
            // Her motion lifts her off the floor in the hop: the trident rides at the height of her hands (the owner itself never leaves the floor).
            Vector3 held = GripAt(age) + Vector3.up * .08f;
            if (body != null && body.Actor != null && body.Actor.TryGetSocketWorld("weapon_use", out var hand)) held.y = Mathf.Max(held.y, hand.y);
            return new PNGuraAttackWeapon.Pose(held, swung, Vector3.Cross(Vector3.up, swung).normalized, .86f, .27f, 0f);
        }

        float sign = (state == "attack_2" || hit % 2 == 1) ? -1f : 1f;
        float approach = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-hitAges[hit] * SweepLeadFraction, 0f, now));
        float follow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, SweepFollowSeconds, now));
        float span = hitAngles[hit] * .5f;
        float yaw = sign * span * (now < 0f ? approach - 1f : follow);
        Vector3 aim = hitDirections[hit];
        Vector3 axis = Quaternion.AngleAxis(yaw, Vector3.up) * aim;
        float extension = Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(-.082f,0f,now))
            * (1f - Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.018f,.145f,now)));
        if (fixedSweep || whirl)
        {
            // Wound back until the swing, then across the whole hit angle with the water.
            float wind = Mathf.Lerp(.93f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-hitAges[hit], -SwingLead, now)));
            float swing = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-SwingLead, SwingFollow, now));
            yaw = sign * span * (now < -SwingLead ? -wind : 2f * swing - 1f);
            axis = Quaternion.AngleAxis(yaw, Vector3.up) * aim;
            // The trident leans with the water's plane; the low follow-through keeps its full length above the floor.
            float elevation = Mathf.Max(-.19f, -sign * Mathf.Sin(yaw * Mathf.Deg2Rad) * .66f);
            Vector3 sweepAxis = (axis + Vector3.up * elevation).normalized;
            return new PNGuraAttackWeapon.Pose(GripAt(age) + Vector3.up * .06f, sweepAxis,
                Vector3.Cross(Vector3.up, sweepAxis).normalized, .55f, .205f, 0f);
        }

        if (finisher)
        {
            float sweep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-.072f, .014f, now));
            axis = Quaternion.AngleAxis(sign * span * (2f * sweep - 1f), Vector3.up) * aim;
        }

        float swingProgress = now < 0f ? approach * .5f : .5f + follow * .5f;
        axis = (axis + Vector3.up * (.25f*(1f-extension)+.095f*follow)).normalized;
        Vector3 across = Vector3.Cross(Vector3.up, axis).normalized;
        const float length = .86f;
        float width = Mathf.Lerp(.22f,.33f,extension);
        float bend = sign * length * .135f * Mathf.Sin((now < 0f ? approach : 1f + follow)*Mathf.PI*.82f);
        Vector3 at = GripAt(age);
        return new PNGuraAttackWeapon.Pose(at, axis, across, length, width, bend);
    }

    private int ClosestHit(float age)
    {
        int current = 0;
        for (int i = 1; i < hitAges.Length; i++)
            if (Mathf.Abs(age - hitAges[i]) < Mathf.Abs(age - hitAges[current])) current = i;
        return current;
    }

    private Vector3 GripAt(float age)
    {
        if (gripCount == 0) return grip;
        int newer = (gripNext + GripHistorySize - 1) % GripHistorySize;
        if (age >= gripTimes[newer]) return grip;
        for (int i = 1; i < gripCount; i++)
        {
            int older = (newer + GripHistorySize - 1) % GripHistorySize;
            if (age >= gripTimes[older])
                return Vector3.Lerp(gripPositions[older], gripPositions[newer], Mathf.InverseLerp(gripTimes[older], gripTimes[newer], age));
            newer = older;
        }
        return gripPositions[newer];
    }

    private static Vector3 Flat(Vector3 value)
    {
        value.y = 0f;
        return value.sqrMagnitude > .0001f ? value.normalized : Vector3.right;
    }

    private void ReleasePose()
    {
        if (body == null || !PoseOwners.TryGetValue(body, out var active) || !ReferenceEquals(active, this)) return;
        PoseOwners.Remove(body);
        body.EndAction();
    }

    public void Fact(string key, double[] values, double age)
    {
        if (shark != null && !bitePlaced && key == PNComboStrikeAction.StepEndFact && values.Length == 3)
            biteStepEnd = new Vector3((float)values[0], (float)values[1], (float)values[2]);
        if (lunge && key == PNComboStrikeAction.TravelFact && values.Length == 4) travel = (float)values[0];
        if (key == PNComboStrikeAction.AimFact && values.Length == 3)
        {
            facing = Flat(new Vector3((float)values[1], 0f, (float)values[2]));
            int turned = (int)values[0];
            if (hitDirections != null && turned >= 0 && turned < hitDirections.Length) hitDirections[turned] = facing;
        }
    }
    public void Result(in PNActionViewResult result) { }
    public void Hide()
    {
        hidden = true;
        ReleasePose();
        water?.Hide();
        accents?.Hide();
        echo?.Hide();
        shadows?.Hide();
        finisherShake?.Dispose();
        finisherShake = null;
        previousFinisherAge = -1f;
        particles?.Hide();
        weapon?.Hide();
        shark?.Hide();
        if (dive != null) dive.Hide();
        leap?.Hide();
        HideLunge();
        PNGuraSubmerge.Release(body, this);
        if (dash != null) dash.Hide();
        if (ghosts != null) foreach (var ghost in ghosts) ghost.Hide();
        if (slashes != null) foreach (var slash in slashes) slash?.Hide();
    }
    public void End(PNActionViewEnd kind)
    {
        if (ended)
        {
            // Teardown ends a tail a second time.
            if (kind == PNActionViewEnd.Cancelled)
            {
                dashTail = false;
                surfTail = false;
                if (dash != null) dash.Remove();
                if (surf != null) surf.Remove();
                if (shark != null) shark.Dispose();
            }
            return;
        }
        ended = true;
        ReleasePose();
        if (dash != null)
        {
            dashTail = kind == PNActionViewEnd.Completed && !hidden && dash.gameObject.activeInHierarchy;
            if (dashTail) dash.Release();
            else dash.Remove();
        }
        surfTail = surf != null && kind == PNActionViewEnd.Completed && !hidden && surf.gameObject.activeInHierarchy;
        if (surfTail) surf.Release();
        shadows?.Dispose();
        water?.Dispose();
        accents?.Dispose();
        echo?.Dispose();
        finisherShake?.Dispose();
        particles?.Dispose();
        leap?.Dispose();
        weapon?.Dispose();
        DisposeLunge();
        if (shark != null && (kind != PNActionViewEnd.Completed || !shark.Release())) shark.Dispose();
        // Letting go of the dive cancels it, so its short way out plays however it ended.
        if (dive != null && !dive.Release()) dive.Dispose();
        PNGuraSubmerge.Release(body, this);
        if (ghosts != null) foreach (var ghost in ghosts) ghost.Dispose();
        if (slashes != null) foreach (var slash in slashes) slash?.Dispose();
    }
}
#endif
