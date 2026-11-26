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
    private const float SweepLeadFraction = .86f;
    private const float SweepFollowSeconds = .089f;
    // Attacks 1-2: the trident crosses the whole hit angle in this window around the hit, just ahead of the water.
    private const float SwingLead = .072f, SwingFollow = .014f;
    private readonly float sweepHeight;
    private readonly PNGuraSlashArc[] slashes;
    private readonly float[] hitAges;
    private readonly Vector3[] hitOrigins;
    private readonly bool[] placed;
    private readonly bool[] sweepAnchored;
    private readonly PNGuraWaterParticles[] cuts;
    private readonly PNGuraAttackWeapon[] ghosts;
    private readonly PNGuraSharkBite shark;
    private readonly float surgeStart;
    private readonly float duration, radius, angle;
    private readonly PNGuraWaterFx water;
    private readonly PNGuraWaterParticles particles;
    private readonly PNGuraAttackWeapon weapon;
    private readonly System.Func<float, PNGuraAttackWeapon.Pose> sampleBlade;
    private const int GripHistorySize = 24;
    private readonly float[] gripTimes = new float[GripHistorySize];
    private readonly Vector3[] gripPositions = new Vector3[GripHistorySize];
    private Vector3 grip;
    private Vector3 biteOrigin;
    private bool bitePlaced;
    private int trailHit, gripCount, gripNext;
    private bool ended, hidden;
    private Vector3 dashOrigin;
    public bool HasTail => false;
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
        direction = Flat(build.Direction);
        var payload = (JObject)build.Data.Action.Perform;
        hitAges = payload["hits"] is JArray hits ? hits.Select(x => x["at"].Value<float>()).ToArray() : System.Array.Empty<float>();
        hitOrigins = new Vector3[hitAges.Length];
        placed = new bool[hitAges.Length];
        if (fixedSweep) sweepAnchored = new bool[hitAges.Length];
        duration = state == "throw" ? payload["windup"].Value<float>() : payload["duration"].Value<float>();
        radius = payload["radius"] != null ? PNHitCapture.ScaleLength(payload["radius"].Value<float>(), build.Scale) : 0;
        angle = payload["angle"]?.Value<float>() ?? 0;
        surgeStart = payload["step"]?["at"]?.Value<float>() ?? 0f;
        bool attack = state.StartsWith("attack_", System.StringComparison.Ordinal);
        if (state == "dash" || state == "dive" || state == "shark") particles = PNGuraWaterParticles.Create(owner, state);
        if (state == "attack_3") particles = PNGuraWaterParticles.Create(owner, "surge");
        if (state != "throw") water = new PNGuraWaterFx(owner);
        if (fixedSweep)
        {
            sweepHeight = payload["height"]?.Value<float>() ?? .3f;
            slashes = new PNGuraSlashArc[hitAges.Length];
            for (int i = 0; i < slashes.Length; i++) slashes[i] = PNGuraSlashArc.Create(owner);
        }
        if (state == "shark") shark = new PNGuraSharkBite(owner);
        if (attack || state == "throw") weapon = new PNGuraAttackWeapon(owner, state == "throw" ? "Gura windup trident" : "Gura attack trident");
        if (attack)
        {
            sampleBlade = age => BladeAt(trailHit, age);
            ghosts = new PNGuraAttackWeapon[state == "attack_3" ? 1 : fixedSweep ? 2 : 3];
            for (int i = 0; i < ghosts.Length; i++)
                ghosts[i] = new PNGuraAttackWeapon(owner, "Gura trident afterimage " + i, true);
            if (!fixedSweep)
            {
                cuts = new PNGuraWaterParticles[hitAges.Length];
                for (int i = 0; i < cuts.Length; i++) cuts[i] = PNGuraWaterParticles.Create(owner, "cut");
            }
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
                if (fixedSweep)
                {
                    var camera = PNViewCamera.Current;
                    float horizontal = Vector3.Dot(direction, camera != null ? camera.transform.right : Vector3.right);
                    if (Mathf.Abs(horizontal) > .01f) body.Actor.SetFacingLeft(horizontal < 0f);
                }
                if (!PoseOwners.TryGetValue(body, out var active) || !ReferenceEquals(active, this))
                {
                    PoseOwners.Remove(body);
                    PoseOwners.Add(body, this);
                }
            }
        }
        else ReleasePose();

        if (state == "throw")
        {
            float phase = Mathf.Clamp01(t / Mathf.Max(.001f,duration));
            float cast = Mathf.SmoothStep(0f,1f,phase);
            Vector3 axis = (Quaternion.AngleAxis(Mathf.Lerp(-136f,-5f,cast),Vector3.up) * direction
                + Vector3.up * Mathf.Lerp(.72f,.12f,cast)).normalized;
            var pose = new PNGuraAttackWeapon.Pose(origin + Vector3.up*.16f, axis, Vector3.Cross(Vector3.up,axis).normalized,
                Mathf.Lerp(.37f,.57f,cast), .22f, .065f * Mathf.Sin(phase*Mathf.PI));
            weapon.Draw(pose, Lead && t < duration ? Mathf.Clamp01(t/.045f) : 0f);
            return;
        }

        water.Begin();
        if (state == "dash")
        {
            if (resuming) dashOrigin = origin;
            float phase = Mathf.Clamp01(t / duration);
            float distance = Vector3.Distance(dashOrigin, origin);
            Vector3 waterline = origin + Vector3.up * .045f;
            water.GroundWake(waterline,direction,Mathf.Max(.58f,distance),.25f,t, 1f - phase*.52f);
            water.BowSplash(waterline,direction,.28f,.13f,t,1f - phase*.66f);
            particles?.Sample(origin - direction*Mathf.Min(distance*.32f,.32f) + Vector3.up * .027f,
                -direction, new Vector3(.29f,.13f,.44f), .06f+2.9f*t,resuming);
        }
        else if (state == "dive")
        {
            Vector3 surface = origin + Vector3.up*.029f;
            particles?.Sample(surface,Vector3.zero,new Vector3(.22f,.12f,.22f),.12f+t,resuming);
            float pulse = Mathf.Repeat(t*2.5f,1f);
            water.Ripple(surface, .26f + .30f*pulse, .018f, (1f-pulse)*.8f);
        }
        else if (shark != null) DrawBite(origin,t,resuming);
        else DrawAttack(origin,t,resuming);
        water.Draw();
    }

    private void DrawBite(Vector3 origin, float age, bool seeking)
    {
        if (hitAges.Length == 0) return;
        float since = age - hitAges[0];
        if (since < -.16f || since > .24f)
        {
            shark.Hide();
            particles?.Hide();
            bitePlaced = false;
            return;
        }
        if (!bitePlaced || since < 0f || seeking)
        {
            biteOrigin = origin;
            bitePlaced = since >= 0f;
        }
        shark.Draw(biteOrigin,direction,radius,since);
        float phase = Mathf.InverseLerp(-.13f,.23f,since);
        Vector3 surface = biteOrigin + Vector3.up*.045f;
        water.Ripple(surface, radius*Mathf.SmoothStep(.32f,1f,Mathf.InverseLerp(-.13f,.011f,since)), .045f*radius, Mathf.Sin(phase*Mathf.PI)*.92f);
        particles?.Sample(surface,direction,new Vector3(.41f,.24f,.41f)*radius,.07f+.82f*phase,seeking);
    }

    private void DrawAttack(Vector3 origin, float age, bool seeking)
    {
        if (!Lead || hitAges.Length == 0)
        {
            weapon.Hide();
            foreach (var ghost in ghosts) ghost.Hide();
            if (cuts != null) foreach (var cut in cuts) cut?.Hide();
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
        bool rush = state == "attack_3";
        float rushEnd = hitAges[hitAges.Length - 1] + .12f;
        if (rush && age >= surgeStart && age < rushEnd)
        {
            Vector3 surface = origin + Vector3.up*.071f;
            float fade = Mathf.SmoothStep(1f,0f,Mathf.InverseLerp(rushEnd-.13f,rushEnd,age));
            water.GroundWake(surface+direction*.20f,direction,.86f,.43f,age,fade);
            water.BowSplash(surface,direction,.44f,.31f,age,fade);
            particles?.Sample(surface+direction*.11f,-direction,new Vector3(.42f,.33f,.63f),.04f+2.05f*(age-surgeStart),seeking);
        }
        else particles?.Hide();
        for (int i = 0; i < hitAges.Length; i++)
        {
            float hit = hitAges[i];
            float since = age - hit;
            trailHit = i;
            if (fixedSweep)
            {
                if (since < 0f || seeking) sweepAnchored[i] = false;
                if (!sweepAnchored[i])
                {
                    hitOrigins[i] = origin;
                    sweepAnchored[i] = since >= 0f;
                }
                DrawSweep(i, since);
                continue;
            }
            water.Trail(Mathf.Max(0f, hit - .046f), hit + .067f, age, state == "attack_4" ? .16f : .105f, sampleBlade, rush);
            if (since >= -.018f && since <= (rush ? .105f : .145f))
            {
                if (!placed[i] || seeking) hitOrigins[i] = origin;
                placed[i] = true;
                float size = radius * (rush ? .35f : .42f);
                const float foamOffset = .36f;
                cuts[i]?.Sample(hitOrigins[i] + direction * (radius * foamOffset) + Vector3.up*.095f,
                    direction, new Vector3(size,size*.60f,size), (rush ? .16f : .11f) + (since + .018f)*2.85f, seeking,
                    i % 2 == 1 || state == "attack_2" ? 26f : -26f);
            }
            else { cuts[i]?.Hide(); placed[i] = false; }
        }

        float alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / .055f));
        alpha *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((duration - age) / .11f));
        weapon.Draw(BladeAt(current, age), alpha);
        for (int i = 0; i < ghosts.Length; i++)
        {
            float past = age - (.019f + i*.018f);
            int beat = ClosestHit(past);
            float travel = past - hitAges[beat];
            float opacity = alpha * (rush ? .13f : fixedSweep ? .15f - i * .05f : .29f - i*.075f);
            bool visible = past > 0f && travel > -.068f && travel < .075f;
            ghosts[i].Draw(BladeAt(beat,past), visible ? opacity : 0f);
        }
    }

    // The picture of a sweep is the hit sector itself: centred where the hit is queried (the caster plus the registered height), out to the scaled radius, across the hit angle. Complete on the hit.
    private void DrawSweep(int hit, float since)
    {
        float side = state == "attack_2" ? -1f : 1f;
        Vector3 centre = hitOrigins[hit] + Vector3.up * sweepHeight;
        slashes[hit]?.Draw(centre, direction, radius, angle, side, since);
        float swept = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-PNGuraSlashArc.Lead, 0f, since));
        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.012f, .085f, since));
        water.SectorEdge(centre, direction, radius, angle, side, swept, PNGuraSlashArc.Lift, fade);
    }

    private PNGuraAttackWeapon.Pose BladeAt(int hit, float age)
    {
        float now = age - hitAges[hit];
        bool rush = state == "attack_3";
        float sign = (state == "attack_2" || hit % 2 == 1) ? -1f : 1f;
        float beforeWindow = rush && hit > 0 ? (hitAges[hit]-hitAges[hit-1])*.5f : rush ? .06f : hitAges[hit] * SweepLeadFraction;
        float afterWindow = rush && hit+1 < hitAges.Length ? (hitAges[hit+1]-hitAges[hit])*.5f : SweepFollowSeconds;
        float approach = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-beforeWindow, 0f, now));
        float follow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, afterWindow, now));
        float span = fixedSweep ? angle * .5f : rush ? 75f : Mathf.Clamp(82f+angle*.42f,110f,142f);
        float yaw = sign * span * (now < 0f ? approach - 1f : follow);
        Vector3 axis = Quaternion.AngleAxis(yaw, Vector3.up) * direction;
        float extension = Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(-.082f,0f,now))
            * (1f - Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.018f,.145f,now)));
        if (rush)
        {
            float hold = Mathf.Clamp01((age - surgeStart)/.08f) * (1f - Mathf.InverseLerp(hitAges[hitAges.Length - 1]+.03f, duration, age));
            extension = Mathf.Max(extension, hold*.65f);
        }
        if (fixedSweep)
        {
            // Wound back until the swing, then across the whole hit angle with the water.
            float wind = Mathf.Lerp(.93f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-hitAges[hit], -SwingLead, now)));
            float swing = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-SwingLead, SwingFollow, now));
            yaw = sign * span * (now < -SwingLead ? -wind : 2f * swing - 1f);
            axis = Quaternion.AngleAxis(yaw, Vector3.up) * direction;
            // The low follow-through keeps the full-length trident above the floor.
            float elevation = Mathf.Max(-.19f, -sign * Mathf.Sin(yaw * Mathf.Deg2Rad) * 1.12f);
            Vector3 sweepAxis = (axis + Vector3.up * elevation).normalized;
            return new PNGuraAttackWeapon.Pose(GripAt(age), sweepAxis,
                Vector3.Cross(Vector3.up, sweepAxis).normalized, .55f, .205f, 0f);
        }

        float lift = rush ? .12f + .12f*Mathf.Cos(yaw*Mathf.Deg2Rad) : .25f*(1f-extension)+.095f*follow;
        axis = (axis + Vector3.up * lift).normalized;
        Vector3 across = Vector3.Cross(Vector3.up, axis).normalized;
        float length = Mathf.Lerp(.42f, Mathf.Max(.42f, radius-.14f), extension);
        float width = Mathf.Lerp(.22f,.33f,extension);
        float bend = sign * length * .135f * Mathf.Sin((now < 0f ? approach : 1f + follow)*Mathf.PI*.82f);
        return new PNGuraAttackWeapon.Pose(GripAt(age), axis, across, length, width, bend);
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

    public void Fact(string key, double[] values, double age) { }
    public void Result(in PNActionViewResult result) { }
    public void Hide()
    {
        hidden = true;
        ReleasePose();
        water?.Hide();
        particles?.Hide();
        weapon?.Hide();
        shark?.Hide();
        if (ghosts != null) foreach (var ghost in ghosts) ghost.Hide();
        if (cuts != null) foreach (var cut in cuts) cut?.Hide();
        if (slashes != null) foreach (var slash in slashes) slash?.Hide();
    }
    public void End(PNActionViewEnd kind)
    {
        if (ended) return;
        ended = true;
        ReleasePose();
        water?.Dispose();
        particles?.Dispose();
        weapon?.Dispose();
        shark?.Dispose();
        if (ghosts != null) foreach (var ghost in ghosts) ghost.Dispose();
        if (cuts != null) foreach (var cut in cuts) cut?.Dispose();
        if (slashes != null) foreach (var slash in slashes) slash?.Dispose();
    }
}
#endif
