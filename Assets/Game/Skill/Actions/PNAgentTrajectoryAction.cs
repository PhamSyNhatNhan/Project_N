#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum PNTrajectoryKind { CurveXZ, Hop, OrbitSegment }
public sealed class PNAgentTrajectorySettings
{
    public PNTrajectoryKind Kind { get; private set; }
    public float Duration { get; private set; }
    public float Distance { get; private set; }
    public float LateralOffset { get; private set; }
    public float Height { get; private set; }
    public float Radius { get; private set; }
    public float SweepDegrees { get; private set; }
    public float MaximumSegment { get; private set; }
    public float ChordError { get; private set; }
    public int MaximumSubsteps { get; private set; }
    public EveId Permission { get; private set; }
    public PNMotionActorCollision ActorCollision { get; private set; }
    public float HitRadius { get; private set; }
    public float ActiveStart { get; private set; }
    public float ActiveEnd { get; private set; }
    public float RepeatInterval { get; private set; }
    public bool CanCrit { get; private set; }
    public PNSkillValues Values { get; private set; }
    private PNAgentTrajectorySettings() { }
    public static PNAgentTrajectorySettings Parse(JToken data, EveRegistryHub registry, bool contact)
    {
        if (!(data is JObject obj)) throw new FormatException("Trajectory payload required.");
        var allowed = new HashSet<string>(new[] { "kind", "duration", "distance", "lateralOffset", "height", "radius", "sweepDegrees", "permissionId", "actorCollision", "maxSegmentLength", "maxChordError", "maxSubsteps", "tags" });
        if (contact) allowed.UnionWith(new[] { "hitRadius", "activeStart", "activeEnd", "repeatInterval", "canCrit", "value" });
        foreach (var p in obj.Properties()) if (!allowed.Contains(p.Name)) throw new FormatException("Unknown trajectory field: " + p.Name);
        float Number(string name, float minimum, float maximum)
        {
            var token = obj[name];
            if (token == null || token.Type != JTokenType.Float && token.Type != JTokenType.Integer) throw new FormatException("Missing trajectory number: " + name);
            float value = token.Value<float>(); if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum) throw new FormatException("Invalid trajectory number: " + name); return value;
        }
        if (obj["kind"]?.Type != JTokenType.String || !Enum.TryParse(obj["kind"].Value<string>(), false, out PNTrajectoryKind kind) || !Enum.IsDefined(typeof(PNTrajectoryKind), kind)) throw new FormatException("Unknown trajectory kind.");
        if (obj["actorCollision"]?.Type != JTokenType.String || !Enum.TryParse(obj["actorCollision"].Value<string>(), false, out PNMotionActorCollision collision) || !Enum.IsDefined(typeof(PNMotionActorCollision), collision)) throw new FormatException("Explicit actor collision policy required.");
        if (obj["kind"].Value<string>() != kind.ToString() || obj["actorCollision"].Value<string>() != collision.ToString()) throw new FormatException("Named trajectory/collision enums required.");
        foreach (var field in new[] { "distance", "lateralOffset", "height", "radius", "sweepDegrees" })
            if (obj[field] != null && !((field == "distance" && kind != PNTrajectoryKind.OrbitSegment) ||
                (field == "lateralOffset" && kind == PNTrajectoryKind.CurveXZ) || (field == "height" && kind == PNTrajectoryKind.Hop) ||
                ((field == "radius" || field == "sweepDegrees") && kind == PNTrajectoryKind.OrbitSegment))) throw new FormatException("Field does not apply to this trajectory kind: " + field);
        if (obj["permissionId"]?.Type != JTokenType.String) throw new FormatException("Motion permission required.");
        var result = new PNAgentTrajectorySettings { Kind = kind, Duration = Number("duration", .01f, 60), ActorCollision = collision,
            Permission = EveId.Parse(obj["permissionId"].Value<string>()), MaximumSegment = Number("maxSegmentLength", .005f, 1),
            ChordError = Number("maxChordError", .0001f, .1f) };
        float steps = Number("maxSubsteps", 1, 128); if (steps != (int)steps) throw new FormatException("Substeps must be integer."); result.MaximumSubsteps = (int)steps;
        registry.ControlPermissions().Get(result.Permission);
        if (kind == PNTrajectoryKind.OrbitSegment)
        { result.Radius = Number("radius", .05f, 100); result.SweepDegrees = Number("sweepDegrees", -180, 180); if (result.SweepDegrees == 0) throw new FormatException("Orbit sweep cannot be zero."); }
        else result.Distance = Number("distance", 0, 100);
        if (kind == PNTrajectoryKind.CurveXZ) result.LateralOffset = Number("lateralOffset", -50, 50);
        if (kind == PNTrajectoryKind.Hop) result.Height = Number("height", .01f, 5);
        if (contact)
        {
            result.HitRadius = Number("hitRadius", .02f, 5); result.ActiveStart = Number("activeStart", 0, 1); result.ActiveEnd = Number("activeEnd", 0, 1);
            if (result.ActiveEnd < result.ActiveStart) throw new FormatException("Hit window reversed.");
            result.RepeatInterval = Number("repeatInterval", 0, 60);
            if (obj["canCrit"]?.Type != JTokenType.Boolean) throw new FormatException("canCrit required.");
            result.CanCrit = obj["canCrit"].Value<bool>(); result.Values = PNSkillValues.Parse(obj["value"]); result.Values.ValidateStats(registry);
        }
        return result;
    }
}

public class PNAgentTrajectoryAction : PNSkillAction, IPNSkillMotionAction, IPNSkillActionOutcome, IPNSkillDeferredCleanup
{
    private PNSkillActionContext context;
    private PNAgentTrajectorySettings settings;
    private PNMotionMotor motor;
    private PNSkillOperation movementLock;
    private Action cancelOwner;
    private Vector3 origin, direction, side, anchor;
    private float elapsed;
    private bool performed, disposed, scanning;
    private PNHitboxCast cast;
    private PNStat caster;
    private PNDamage damage;
    private PNCombatSourceSnapshot source;
    private double rawAmount;
    private readonly List<PNStat> targets = new List<PNStat>();
    private RaycastHit[] sweptHits = new RaycastHit[32];
    private static readonly IComparer<RaycastHit> SweepOrder = Comparer<RaycastHit>.Create((x, y) => x.distance.CompareTo(y.distance));
    protected virtual bool Contact => false;
    public PNMotionOutcome Outcome { get; private set; }
    public bool CleanupPending => scanning;
    public override bool HasAuxiliaryTimeline => true;
    public void BindMovementOwner(Action cancel)
    { if (context != null || disposed) throw new InvalidOperationException("Bind before Start."); cancelOwner = cancel ?? throw new ArgumentNullException(nameof(cancel)); }
    public static void Validate(PNSkillActionData data, EveRegistryHub registry, bool contact)
    {
        var settings = PNAgentTrajectorySettings.Parse(data.Perform, registry, contact);
        if (contact) new PNStatSet(registry).ResolveDamageType(data.PerformTags);
    }
    public override void Start(PNSkillActionContext value)
    {
        if (context != null || disposed || value == null || !value.Direction.HasValue || cancelOwner == null) throw new InvalidOperationException("Invalid trajectory Start or missing motion owner.");
        settings = PNAgentTrajectorySettings.Parse(value.Data.Action.Perform, value.Registry, Contact);
        if (!value.Skill.Controls.IsAllowed(settings.Permission)) throw new InvalidOperationException("Motion permission denied.");
        direction = value.Direction.Value; direction.y = 0;
        if (direction.sqrMagnitude < .000001f) throw new ArgumentException("Planar direction required."); direction.Normalize(); side = new Vector3(-direction.z, 0, direction.x);
        var move = value.Skill.GetComponent<PNMove>(); if (move == null) throw new InvalidOperationException("Motion body required.");
        origin = move.Body.position; anchor = value.Position ?? origin;
        if (settings.Kind == PNTrajectoryKind.OrbitSegment && (value.Position == null || Mathf.Abs(PNAgentGeometry.CenterDistance(origin, anchor) - settings.Radius) > .1f)) throw new ArgumentException("Orbit requires a snapshot anchor at the configured radius.");
        context = value; context.BeginAction(); caster = value.Skill.GetComponent<PNStat>();
        movementLock = CreateOperation(context, EveId.Parse("base:lock_movement")); movementLock.Execute(context, null);
        motor = new PNMotionMotor(move, settings.Permission, settings.ActorCollision, settings.Kind == PNTrajectoryKind.Hop, cancelOwner);
        for (int i = 0; i <= 16; i++)
        {
            var point = Sample(i / 16f); point.y = origin.y;
            if (!value.Skill.gameObject.scene.GetPhysicsScene().Raycast(point + Vector3.up * .3f, Vector3.down, out RaycastHit ground, .5f, 1 << 13, QueryTriggerInteraction.Ignore))
            { Outcome = PNMotionOutcome.InvalidLanding; break; }
        }
        if (settings.Kind == PNTrajectoryKind.Hop && Outcome == PNMotionOutcome.Running)
        {
            var lease = PNWorldNavigation.Find(value.World);
            for (int i = 0; i <= 16; i++)
            {
                var point = Vector3.Lerp(origin, origin + direction * settings.Distance, i / 16f);
                if (lease == null || !lease.TryProject(point, .25f, out var projected) || Mathf.Abs(projected.y - origin.y) > .15f)
                { Outcome = PNMotionOutcome.InvalidLanding; break; }
            }
        }
    }
    public override void Perform(PNSkillActionContext value)
    {
        if (!ReferenceEquals(context, value) || performed || disposed) throw new InvalidOperationException("Invalid trajectory Perform.");
        performed = true;
        if (Contact)
        {
            foreach (var component in settings.Values.Components) rawAmount += component.Evaluate(caster);
            source = caster.CaptureCombatSource(); damage = caster.CreateDamage(rawAmount, settings.CanCrit, null, value.Data.Action.PerformTags.ToArray());
            cast = new PNHitboxCast(EveHitboxShape.Sphere(settings.HitRadius), value.Skill.gameObject.scene.GetPhysicsScene(), settings.RepeatInterval > 0 ? settings.RepeatInterval : (double?)null);
        }
    }
    private Vector3 Sample(float t)
    {
        if (settings.Kind == PNTrajectoryKind.OrbitSegment)
        {
            var offset = origin - anchor; float angle = settings.SweepDegrees * Mathf.Deg2Rad * t;
            return anchor + new Vector3(offset.x * Mathf.Cos(angle) - offset.z * Mathf.Sin(angle), offset.y, offset.x * Mathf.Sin(angle) + offset.z * Mathf.Cos(angle));
        }
        var point = origin + direction * (settings.Distance * t);
        return point + (settings.Kind == PNTrajectoryKind.Hop ? Vector3.up * (4 * settings.Height * t * (1 - t)) : side * (4 * settings.LateralOffset * t * (1 - t)));
    }
    public override bool AdvanceAuxiliary(float delta)
    {
        PNAgentGeometry.Nonnegative(delta, nameof(delta));
        if (scanning) throw new InvalidOperationException("Trajectory timeline cannot reenter.");
        if (Outcome != PNMotionOutcome.Running || disposed) return true;
        if (!performed || delta == 0 || !PNWorldGameplay.CanExecute(context.Skill) || !motor.CanWrite) return false;
        float next = Mathf.Min(settings.Duration, elapsed + delta); float from = elapsed / settings.Duration, to = next / settings.Duration;
        float maximumStep = Contact ? Mathf.Min(settings.MaximumSegment, settings.HitRadius * .5f) : settings.MaximumSegment;
        int steps = 1;
        while (true)
        {
            bool enough = true;
            for (int i = 0; i < steps; i++)
            { float x = Mathf.Lerp(from, to, (float)i / steps), y = Mathf.Lerp(from, to, (i + 1f) / steps); if (Vector3.Distance(Sample(x), Sample(y)) > maximumStep || Vector3.Distance(Sample((x + y) * .5f), Vector3.Lerp(Sample(x), Sample(y), .5f)) > settings.ChordError) { enough = false; break; } }
            if (enough) break; if (steps * 2 > settings.MaximumSubsteps) { Outcome = PNMotionOutcome.BudgetExceeded; return true; } steps *= 2;
        }
        scanning = true;
        try
        {
            for (int i = 0; i < steps && !disposed; i++)
            {
                float x = Mathf.Lerp(from, to, (float)i / steps), y = Mathf.Lerp(from, to, (i + 1f) / steps);
                var displacement = Sample(y) - Sample(x); if (settings.Kind != PNTrajectoryKind.Hop) displacement.y = 0;
                var start = motor.Position; bool complete = motor.Step(displacement); var end = motor.Position;
                if (Contact) ScanContact(start, end, x * settings.Duration, y * settings.Duration);
                if (!complete) { Outcome = motor.Outcome; break; }
                if (Outcome != PNMotionOutcome.Running) break;
            }
            elapsed = next;
            if (!disposed && Outcome == PNMotionOutcome.Running && elapsed >= settings.Duration) Outcome = PNMotionOutcome.Completed;
        }
        finally { scanning = false; if (disposed) Cleanup(); }
        return Outcome != PNMotionOutcome.Running || disposed;
    }
    private void ScanContact(Vector3 start, Vector3 end, double timeStart, double timeEnd)
    {
        double windowStart = settings.ActiveStart * settings.Duration, windowEnd = settings.ActiveEnd * settings.Duration;
        if (timeEnd < windowStart || timeStart > windowEnd || disposed) return;
        float a = timeEnd > timeStart ? (float)Math.Max(0, (windowStart - timeStart) / (timeEnd - timeStart)) : 0;
        float b = timeEnd > timeStart ? (float)Math.Min(1, (windowEnd - timeStart) / (timeEnd - timeStart)) : 1;
        var first = Vector3.Lerp(start, end, a) + Vector3.up * .7f; var last = Vector3.Lerp(start, end, b) + Vector3.up * .7f;
        double t0 = timeStart + (timeEnd - timeStart) * a, t1 = timeStart + (timeEnd - timeStart) * b;
        Deliver(first, t0); if (disposed) return;
        var vector = last - first; float length = vector.magnitude;
        if (length > .00001f)
        {
            int count = PNAgentPhysicsQuery.SphereCast(context.Skill.gameObject.scene.GetPhysicsScene(), first, settings.HitRadius, vector / length, ref sweptHits, length, ~0);
            if (count == sweptHits.Length) { Outcome = PNMotionOutcome.BudgetExceeded; return; }
            Array.Sort(sweptHits, 0, count, SweepOrder);
            for (int i = 0; i < count && !disposed; i++)
            {
                if (sweptHits[i].collider?.GetComponentInParent<PNStat>() == null) continue;
                float f = Mathf.Clamp01((sweptHits[i].distance + .001f) / length); Deliver(Vector3.Lerp(first, last, f), t0 + (t1 - t0) * f);
            }
        }
        if (!disposed) Deliver(last, t1);
    }
    private void Deliver(Vector3 position, double time)
    {
        cast.Query(EveHitboxPose.Planar(position, direction), time, targets, caster, ~0, QueryTriggerInteraction.Ignore,
            target => caster.Targets(target) && !target.IsDead);
        foreach (var target in targets)
        {
            if (disposed) break;
            context.ReceiveHit(target, position, damage, rawAmount, source);
            if (!disposed && target.IsReady) cast.CommitHit(target, time);
        }
    }
    public override void Exit(PNSkillActionContext value) { value.CompleteAction(); Dispose(); }
    public override void Cancel(PNSkillActionContext value) { try { value.CancelEffects(); if (Outcome == PNMotionOutcome.Running) Outcome = PNMotionOutcome.Cancelled; } finally { Dispose(); } }
    public override void Dispose() { disposed = true; if (!scanning) Cleanup(); }
    private void Cleanup() { cast?.Dispose(); cast = null; motor?.Dispose(); motor = null; movementLock?.Dispose(); movementLock = null; }
}
public class PNAgentContactMotionAction : PNAgentTrajectoryAction { protected override bool Contact => true; }
#endif
