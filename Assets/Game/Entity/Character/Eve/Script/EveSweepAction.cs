using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;
#if UNITY_5_3_OR_NEWER
using UnityEngine.SceneManagement;
#endif

// EN: Independent Eve melee action. No legacy slash prefab, tracks or VFX operations.
// VI: Action cận chiến riêng của Eve; không dùng prefab, track hay VFX slash cũ.
public class EveSweepAction : PNSkillAction
{
    public override bool HasAuxiliaryTimeline => true;
    public override bool AdvanceAuxiliary(float delta) { Advance(delta); return IsComplete; }
    public static int Invocations { get; private set; }
    private PNSkillActionContext context;
    private PNLockMovementOperation movementLock;
    private readonly List<PNDamageResult> results = new List<PNDamageResult>();
    private EveSweepSettings settings;
    private PNStat caster;
    private float elapsed;
    private bool performed, disposed;
    public bool IsComplete => performed && elapsed >= settings.Duration;
    public IReadOnlyList<PNDamageResult> Results => results.AsReadOnly();
    public PNDamage Damage { get; private set; }
#if UNITY_5_3_OR_NEWER
    private double rawAmount;
    private PNCombatSourceSnapshot damageSource;
    private PNHitboxCast hitbox;
    private EveHitboxPose root;
    private readonly List<PNStat> targets = new List<PNStat>();
    public EveSweepVisual Visual { get; private set; }
#endif
    public override void Start(PNSkillActionContext value)
    {
        if (context != null || disposed) throw new InvalidOperationException("Sweep cannot start twice.");
        if (value == null || !value.Direction.HasValue || (value.Direction.Value.x == 0 && value.Direction.Value.z == 0))
            throw new ArgumentException("Sweep needs a planar aim.");
        settings = EveSweepSettings.Parse(value.Data.Action.Perform);
        context = value; caster = value.Skill.GetComponent<PNStat>();
        if (caster == null || !caster.IsReady || !ReferenceEquals(((PNStatSet)caster.Set).Registry, value.Registry))
            throw new InvalidOperationException("Sweep needs ready session stats.");
        value.BeginAction();
        movementLock = (PNLockMovementOperation)CreateOperation(value, EveId.Parse("base:lock_movement")); movementLock.Execute(value, null);
    }
    public override void Perform(PNSkillActionContext value)
    {
        if (performed || disposed || !ReferenceEquals(value, context)) throw new InvalidOperationException("Invalid sweep perform.");
        performed = true; Invocations++;
#if UNITY_5_3_OR_NEWER
        var rig=((EveCharacterSkill)context.Skill).WeaponRig;
        root = EveHitboxPose.Planar(context.Skill.transform.position+rig.HandOffset(context.Direction.Value), context.Direction.Value);
        Visual = EveSweepVisual.Create(context.Skill.transform, root, settings);
        if (EveRegistrySession.Context.HasAuthority)
        {
            double amount = 0d;
            foreach (var component in settings.Values.Components) amount += component.Evaluate(caster);
            rawAmount = amount; damageSource = caster.CaptureCombatSource();
            Damage = caster.CreateDamage(amount, true, settings.IFrameDuration, value.Data.Action.PerformTags.ToArray());
            hitbox = new PNHitboxCast(EveHitboxShape.Sector(settings.Radius, settings.HitThickness, 4, settings.InnerRadius),
                caster.gameObject.scene.GetPhysicsScene());
        }
#endif
    }
    public virtual void Advance(float delta)
    {
        if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (!performed || disposed || delta == 0) return;
        float previous = elapsed; elapsed = Math.Min(settings.Duration, elapsed + delta);
#if UNITY_5_3_OR_NEWER
        if (!caster.IsReady || caster.IsDead || !EveRegistrySession.IsReady || caster.SessionId != EveRegistrySession.Context.SessionId)
            throw new InvalidOperationException("Sweep belongs to an unavailable caster/session.");
        float begin = Math.Max(previous, settings.SweepStart * settings.Duration);
        float end = Math.Min(elapsed, settings.SweepEnd * settings.Duration);
        if (hitbox != null && begin <= end && previous <= settings.SweepEnd * settings.Duration && elapsed >= settings.SweepStart * settings.Duration)
        {
            int count = Math.Max(1, (int)Math.Ceiling((end - begin) / settings.SampleInterval));
            count = Math.Max(count, (int)Math.Ceiling((end - begin) / settings.Duration / (settings.SweepEnd - settings.SweepStart)
                * Math.Abs(settings.EndAngle - settings.StartAngle) * 1.5f / 2f));
            for (int i = 0; i <= count && !disposed; i++)
            {
                float time = Mathf.Lerp(begin, end, i / (float)count);
                float progress = settings.SweepProgress(time / settings.Duration);
                float angle = Mathf.Lerp(settings.StartAngle, settings.EndAngle, progress);
                var rotation = root.Rotation * Quaternion.AngleAxis(settings.Tilt, Vector3.right) * Quaternion.Euler(0, -angle, 0);
                hitbox.Query(new EveHitboxPose(root.Position + Vector3.up * settings.Height, rotation), time, targets, caster, ~0,
                    QueryTriggerInteraction.Ignore, target => !ReferenceEquals(target, caster) && PNLayers.Contains(caster.TargetLayers.Mask | settings.TargetLayers.Mask, target.Layer));
                foreach (var target in targets)
                {
                    if (disposed) break;
                    if (!target.IsReady || target.IsDead) continue;
                    hitbox.CommitHit(target, time);
                    var point = target.transform.position;
                    results.Add(context.ReceiveHit(target, point, Damage, rawAmount, damageSource));
                    if (!disposed && Visual != null) Visual.Impact(point, ((EveCharacterSkill)context.Skill).Moveset.Energy.ImpactParticles);
                }
            }
        }
        if (!disposed) Visual?.Render(elapsed / settings.Duration, delta);
#endif
    }
    public override void Exit(PNSkillActionContext value) { value.CompleteAction(); Cleanup(false); }
    public override void Cancel(PNSkillActionContext value) { value?.CancelEffects(); Cleanup(true); }
    public override void Dispose() => Cleanup(true);
    private void Cleanup(bool cancelled)
    {
        if (disposed) return; disposed = true;
#if UNITY_5_3_OR_NEWER
        hitbox?.Dispose(); hitbox = null;
        if (Visual != null) { Visual.Release(cancelled); Visual = null; }
#endif
        movementLock?.Dispose(); movementLock = null;
    }
}
