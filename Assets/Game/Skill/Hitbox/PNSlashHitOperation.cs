using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Per-invocation geometry tracks share the visual clock; damage delivery remains separate.
// VI: Các track hình học riêng từng lần chém dùng chung thời gian VFX; delivery damage vẫn tách biệt.
public class PNSlashHitOperation : PNSkillOperation
{
    private PNHitboxCast cast;
    private PNHitboxCast[] regionCasts;
    private EveHitboxTrackData[] tracks;
    private PNArcSlashVisual visual;
    private Vector3 previousPosition;
    private readonly HashSet<EveEntityId> delivered = new HashSet<EveEntityId>();
    public virtual void BindVisual(PNArcSlashVisual value) { if (executed) throw new InvalidOperationException("Bind before Execute."); visual = value; }
    private PNSkillActionContext invocation;
    private PNSlashHitSettings settings;
    private PNStat caster;
    private Vector3 origin, direction;
    private Guid sessionId;
    private double elapsed;
    private bool executed, disposed, scanning;
    private readonly List<PNStat> targets = new List<PNStat>();
    private readonly List<PNDamageResult> results = new List<PNDamageResult>();
    public virtual PNDamage Damage { get; protected set; }
    private double rawAmount;
    private PNCombatSourceSnapshot damageSource;
    public virtual IReadOnlyList<PNDamageResult> Results => results.AsReadOnly();

    public override void Execute(PNSkillActionContext context, JToken data)
    {
        if (executed || disposed) throw new InvalidOperationException("Slash hit operation cannot execute twice.");
        executed = true;
        settings = PNSlashHitSettings.Parse(data);
        if (settings == null) return;
        if (context == null || !context.Direction.HasValue) throw new ArgumentException("Slash hits require a skill direction.");
        invocation = context; caster = context.Skill.GetComponent<PNStat>();
        if (!context.Skill.IsReady || caster == null || !caster.IsReady || !(caster.Set is PNStatSet stats) ||
            !ReferenceEquals(stats.Registry, context.Registry)) throw new InvalidOperationException("Slash requires the caster's Ready game stats.");
        sessionId = caster.SessionId;
        var pose = EveHitboxPose.Planar(context.Skill.transform.position, context.Direction.Value);
        direction = pose.Rotation * Vector3.right; origin = pose.Position;
        previousPosition = origin;
        if (visual != null) { visual.HitStart = settings.WindowStart / settings.Duration; visual.HitEnd = settings.WindowEnd / settings.Duration; }
        if (settings.PrefabDriven)
        {
            tracks = visual?.Tracks;
            if (tracks == null || tracks.Length == 0 || tracks.Length > 64) throw new InvalidOperationException("Combat prefab needs 1..64 shared hitbox tracks.");
            foreach (var track in tracks) track.Validate();
        }
        stats.ResolveDamageType(context.Data.Action.PerformTags);
        // Clients render the action but cannot query/apply authoritative hits.
        if (!EveRegistrySession.Context.HasAuthority) return;
        // Slash sums components once before creating its shared damage snapshot.
        double amount = 0d;
        foreach (var component in settings.Values.Components) amount += component.Evaluate(caster);
        rawAmount = amount; damageSource = caster.CaptureCombatSource();
        Damage = caster.CreateDamage(amount, settings.CanCrit, settings.IFrameDuration, context.Data.Action.PerformTags.ToArray());
        if (tracks == null)
            cast = new PNHitboxCast(EveHitboxShape.Sector(settings.Radius, settings.Thickness, settings.Angle, settings.InnerRadius), context.Skill.gameObject.scene.GetPhysicsScene());
        else
        {
            regionCasts = new PNHitboxCast[tracks.Length];
            try { for (int i = 0; i < tracks.Length; i++) regionCasts[i] = new PNHitboxCast(tracks[i].Geometry.Shape(), context.Skill.gameObject.scene.GetPhysicsScene()); }
            catch { Dispose(); throw; }
        }
        Scan(0d, 0d);
    }
    public virtual void Advance(float deltaTime)
    {
        if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
        if (disposed || (cast == null && regionCasts == null) || deltaTime == 0f) return;
        double previous = elapsed;
        elapsed += deltaTime;
        try { Scan(previous, elapsed); }
        catch { Dispose(); throw; }
    }
    // A target is hit when its layer is in the caster's TargetLayers or in the slash's own targetLayers (never the caster itself).
    protected virtual bool CanHit(PNStat target) => target != null && !ReferenceEquals(target, caster) &&
        PNLayers.Contains(caster.TargetLayers.Mask | settings.TargetLayers.Mask, target.Layer);
    private void Scan(double previous, double current)
    {
        if (disposed) return;
        var currentPosition = caster.transform.position;
        if (settings.FollowCaster && visual != null && visual.AttackRoot != null) visual.AttackRoot.position = currentPosition;
        if ((cast == null && regionCasts == null) || previous >= settings.WindowEnd || current < settings.WindowStart)
        { previousPosition = currentPosition; return; }
        if (scanning) throw new InvalidOperationException("Slash hit operation cannot reenter.");
        var loader = caster.GetComponent<EveEntityLoader>();
        if (!EveRegistrySession.IsReady || EveRegistrySession.Context.SessionId != sessionId || !invocation.Skill.IsReady ||
            loader == null || !loader.IsReady || !caster.IsReady || caster.IsDead || caster.SessionId != sessionId)
            throw new InvalidOperationException("Slash caster is no longer alive/Ready in its session.");
        double end = Math.Min(current, settings.WindowEnd), begin = Math.Max(previous, settings.WindowStart);
        var times = new SortedSet<double> { begin, end };
        int first = (int)Math.Floor((begin - settings.WindowStart) / settings.SampleInterval) + 1;
        for (int step = first; ; step++)
        {
            double sample = settings.WindowStart + step * (double)settings.SampleInterval;
            if (sample >= end) break;
            times.Add(sample);
        }
        if (tracks != null)
            foreach (var track in tracks)
                foreach (float boundary in new[] { track.EnableFrom, track.EnableTo })
                { double time = boundary * settings.Duration; if (time >= begin && time <= end) times.Add(time); }
        scanning = true;
        try
        {
            foreach (double time in times)
            {
                if (disposed) break;
                var position = settings.FollowCaster
                    ? Vector3.Lerp(previousPosition, currentPosition, current > previous ? (float)((time - previous) / (current - previous)) : 1f) : origin;
                var root = EveHitboxPose.Planar(position, direction);
                if (tracks == null)
                    Deliver(cast, new EveHitboxPose(position + direction * settings.ForwardOffset + Vector3.up * settings.VerticalOffset, root.Rotation), time, ~0);
                else
                    for (int i = 0; i < tracks.Length && !disposed; i++)
                    {
                        var frame = tracks[i].Sample((float)(time / settings.Duration), root);
                        if (!frame.Enabled || frame.Shape == null) continue;
                        regionCasts[i].SetShape(frame.Shape);
                        Deliver(regionCasts[i], frame.Pose, time, tracks[i].LayerMask);
                    }
            }
        }
        finally { previousPosition = currentPosition; scanning = false; if (disposed) Cleanup(); }
    }
    private void Deliver(PNHitboxCast region, EveHitboxPose pose, double time, int layerMask)
    {
        region.Query(pose, time, targets, caster, layerMask,
            settings.HitTriggers ? QueryTriggerInteraction.Collide : QueryTriggerInteraction.Ignore, CanHit);
        for (int i = 0; i < targets.Count && !disposed; i++)
        {
            var target = targets[i];
            if (delivered.Contains(target.EntityId) || !target.IsReady || !target.gameObject.activeInHierarchy || target.IsDead || !CanHit(target)) continue;
            region.CommitHit(target, time); delivered.Add(target.EntityId);
            results.Add(invocation.ReceiveHit(target, target.transform.position, Damage, rawAmount, damageSource));
        }
    }
    public override void Cancel() => Dispose();
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (!scanning) Cleanup(); // Callbacks may cancel while ReceiveDamage is still running.
    }
    private void Cleanup()
    {
        cast?.Dispose(); cast = null; targets.Clear(); invocation = null; caster = null;
        if (regionCasts != null) foreach (var region in regionCasts) region?.Dispose();
        regionCasts = null; tracks = null; visual = null; delivered.Clear();
    }
}
