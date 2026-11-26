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
    private const float SweepLeadFraction = .86f;
    private const float SweepFollowSeconds = .089f;
    // Attacks 1-2: the trident crosses the whole hit angle in this window around the hit, just ahead of the water.
    private const float SwingLead = .072f, SwingFollow = .014f;
    private readonly float sweepHeight, innerRadius, halfThickness;
    private readonly System.Func<float, float, float> sweepLift;
    private readonly PNGuraSlashArc[] slashes;
    private readonly float[] hitAges;
    private readonly Vector3[] hitDirections;
    private readonly Vector3[] hitOrigins;
    private readonly bool[] sweepAnchored;
    private readonly PNGuraAttackWeapon[] ghosts;
    private readonly PNGuraSharkBite shark;
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
    public bool HasTail => dashTail && dash != null || shark != null && shark.HasTail;
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
        direction = Flat(build.Direction);
        var payload = (JObject)build.Data.Action.Perform;
        var hits = payload["hits"] as JArray;
        hitAges = hits != null ? hits.Select(x => x["at"].Value<float>()).ToArray() : System.Array.Empty<float>();
        hitOrigins = new Vector3[hitAges.Length];
        duration = state == "throw" ? payload["windup"].Value<float>() : payload["duration"].Value<float>();
        radius = payload["radius"] != null ? PNHitCapture.ScaleLength(payload["radius"].Value<float>(), build.Scale) : 0;
        angle = payload["angle"]?.Value<float>() ?? 0;
        bool attack = state.StartsWith("attack_", System.StringComparison.Ordinal);
        if (state == "dive") particles = PNGuraWaterParticles.Create(owner, state);
        if (state == "dash") dash = PNGuraDashVisual.Create(owner, direction, duration, payload["distance"]?.Value<float>() ?? 0f);
        else if (state != "shark") water = new PNGuraWaterFx(owner);
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
            else sweepLift = AttackLift;
            sweepAnchored = new bool[hitAges.Length];
            slashes = new PNGuraSlashArc[hitAges.Length];
            for (int i = 0; i < slashes.Length; i++) slashes[i] = PNGuraSlashArc.Create(owner, finisher, 1f);
        }
        if (state == "shark") shark = PNGuraSharkBite.Create(owner);
        if (attack && !rush || state == "throw") weapon = new PNGuraAttackWeapon(owner, state == "throw" ? "Gura windup trident" : "Gura attack trident");
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
            if (body != null && body.PlayActionAt(state, age, state == "throw" ? duration * 2.2f : duration, resuming))
            {
                // Every pose of Gura's actions (strikes, the throw, the bite) looks where the cast is aimed. A dive keeps the way she already faces.
                if (state != "dive") body.FaceAction(direction);
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
            }
            water.Draw();
            return;
        }

        water?.Begin();
        accents?.Begin();
        if (state == "dive")
        {
            Vector3 surface = origin + Vector3.up*.029f;
            particles?.Sample(surface,Vector3.zero,new Vector3(.22f,.12f,.22f),.12f+t,resuming);
            float pulse = Mathf.Repeat(t*2.5f,1f);
            water.Ripple(surface, .26f + .30f*pulse, .018f, (1f-pulse)*.8f);
        }
        else if (shark != null) DrawBite(origin,t,resuming);
        else if (echo != null)
        {
            if (Lead) echo.Draw(t, resuming);
            else echo.Hide();
        }
        else DrawAttack(origin,t,resuming);
        water?.Draw();
        accents?.Draw();
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
            gripCount = gripNext = 0;
            return;
        }

        var actor = body != null ? body.Actor : null;
        Vector3 feet = actor != null ? actor.transform.position : origin;
        grip = feet + Vector3.up * .13f + direction * (fixedSweep ? .28f : .16f);
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
            DrawSweep(i, since);
            if (finisher) DrawFinisherWater(i, since);
        }

        if (finisher) PlayFinisherShake(age, seeking);
        float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / .055f));
        alpha *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((duration - age) / .11f));
        weapon.Draw(BladeAt(current, age), alpha);
        for (int i = 0; i < ghosts.Length; i++)
        {
            float past = age - (.019f + i*.018f);
            int beat = ClosestHit(past);
            float travel = past - hitAges[beat];
            float opacity = alpha * (fixedSweep ? .15f - i * .05f : .29f - i*.075f);
            bool visible = past > 0f && travel > -.068f && travel < .075f;
            ghosts[i].Draw(BladeAt(beat,past), visible ? opacity : 0f);
        }
    }

    // The picture of a sweep is the hit sector itself: centred where the hit is queried (the caster plus the registered height), out to the scaled radius, across the hit angle. Complete on the hit.
    private void DrawSweep(int hit, float since)
    {
        float side = state == "attack_2" || hit % 2 == 1 ? -1f : 1f;
        Vector3 centre = hitOrigins[hit] + Vector3.up * sweepHeight;
        Vector3 aim = hitDirections[hit];
        slashes[hit]?.Draw(centre, aim, radius, innerRadius, angle, side, since, sweepLift);
        float swept = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-PNGuraSlashArc.Lead, 0f, since));
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.012f, .085f, since));
        water.SectorEdge(centre, aim, radius, angle, side, swept, sweepLift, finisher ? fade * .72f : fade);
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
        float sign = (state == "attack_2" || hit % 2 == 1) ? -1f : 1f;
        float approach = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-hitAges[hit] * SweepLeadFraction, 0f, now));
        float follow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, SweepFollowSeconds, now));
        float span = angle * .5f;
        float yaw = sign * span * (now < 0f ? approach - 1f : follow);
        Vector3 aim = hitDirections[hit];
        Vector3 axis = Quaternion.AngleAxis(yaw, Vector3.up) * aim;
        float extension = Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(-.082f,0f,now))
            * (1f - Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.018f,.145f,now)));
        if (fixedSweep)
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
    }
    public void Result(in PNActionViewResult result) { }
    public void Hide()
    {
        hidden = true;
        ReleasePose();
        water?.Hide();
        accents?.Hide();
        echo?.Hide();
        finisherShake?.Dispose();
        finisherShake = null;
        previousFinisherAge = -1f;
        particles?.Hide();
        weapon?.Hide();
        shark?.Hide();
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
                if (dash != null) dash.Remove();
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
        water?.Dispose();
        accents?.Dispose();
        echo?.Dispose();
        finisherShake?.Dispose();
        particles?.Dispose();
        weapon?.Dispose();
        if (shark != null && (kind != PNActionViewEnd.Completed || !shark.Release())) shark.Dispose();
        if (ghosts != null) foreach (var ghost in ghosts) ghost.Dispose();
        if (slashes != null) foreach (var slash in slashes) slash?.Dispose();
    }
}
#endif
