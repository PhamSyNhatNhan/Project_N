using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public enum EveCast { None, Attack, Repeat, Skill, Dash, Ultimate }
public enum EveCastPhase { None, Motion, Retreat, Charge, Rush, Slash }

public partial class EveCharacterSkill
{
    public static readonly EveId SkillCooldownId = EveId.Parse("project_n:eve_skill_ready");
    public static readonly EveId UltimateCooldownId = EveId.Parse("project_n:eve_ultimate_ready");
    public static readonly EveId DashCooldownId = EveId.Parse("project_n:eve_dash_ready");
    public static readonly EveId BurstGaugeId = EveId.Parse("project_n:eve_burst_gauge");
    public static readonly EveId BurstStateId = EveId.Parse("project_n:eve_burst_state");
    public static readonly EveId SlashStacksId = EveId.Parse("project_n:eve_slash_stacks");
    public static readonly EveId EmpowermentId = EveId.Parse("project_n:eve_next_attack");
    private static readonly EveId BurstSourceId = EveId.Parse("project_n:eve_burst_bonus");
    private EveMovesetSettings moveset;
    public EveMovesetSettings Moveset => moveset;
    public EveCast CurrentCast { get; private set; }
    public EveCastPhase CurrentPhase { get; private set; }
    public PNStat CurrentTarget => castTarget;
    public bool IsBusy => CurrentCast != EveCast.None;
    public bool IsInBurst => burstEffectsActive;
    public int ComboIndex => comboIndex;
    public PNAbilityCounter BurstGauge => gauge;
    public PNAbilityCounter SlashStacks => stacks;
    public PNAbilityCooldown SkillRecovery => skillRecovery;
    public PNAbilityCooldown UltimateRecovery => ultimateRecovery;
    public PNAbilityState Empowerment => empowerment;
    public PNAbilityState BurstState => burst;
    public PNBuffSnapshot BlazingAssault => blazingAssault?.Snapshot;
    public PNBuffSnapshot RisingMomentum => risingMomentum?.Snapshot;
    private EveOwnedBuff blazingAssault, risingMomentum;
    private bool repeatChainActive;
    private PNStat loadedStats;
    private PNStat Stats => loadedStats;
    private EveRegistryHub movesetRegistry;
    private PNAbilityCollection ownedAbilities;
    private PNAbilityCooldown skillRecovery, ultimateRecovery;
    private PNAbilityCounter gauge, stacks;
    private PNAbilityState burst, empowerment;
    private PNMotionAction motion;
    private PNSkillActionContext motionContext;
    private PNSkillOperation sequenceLock;
    private int comboIndex, repeatStroke, ultimateStroke;
    private string pendingAttackProfile;
    private float comboIdle, phaseRemaining;
    private double lastCdr = double.NaN;
    private bool repeatHeld, repeatExhausted, rewardedHit, burstEffectsActive, burstFinisher, ultimateRewarded;
    private bool deathHandled;
    private Vector3 castDirection;
    private PNStat castTarget;
#if UNITY_5_3_OR_NEWER
    private EveEnergyVisual energy;
    public EveWeaponRig WeaponRig { get; private set; }
#endif
    public override IEnumerable<Type> Dependencies => new[] { typeof(PNAbilityManager), typeof(PNStat) };
    protected override void AwakeSetUp()
    {
        base.AwakeSetUp();
        AttackInput.HoldMode = PlayerSkillHoldMode.Repeat;
        DashInput.CanTap = true; DashInput.PressImmediately = true; DashInput.HoldMode = PlayerSkillHoldMode.None;

#if UNITY_5_3_OR_NEWER
        // Existing scene components may predate PNSkill's RequireComponent.
        if (GetComponent<PNAbilityManager>() == null) gameObject.AddComponent<PNAbilityManager>();
        // Gamepad North already belongs to Jump in PNPlayerMove.
        var ulti = pc.Controller.Ulti;
        for (int i = 0; i < ulti.bindings.Count; i++)
            if (ulti.bindings[i].path == "<Gamepad>/buttonNorth")
                ulti.ApplyBindingOverride(i, new UnityEngine.InputSystem.InputBinding { overridePath = "<Gamepad>/rightShoulder" });
#endif
    }
    private void LoadMoveset(EveEntityLoadContext context)
    {
        movesetRegistry = context.Registry; ownedAbilities = Abilities; loadedStats = GetComponent<PNStat>();
        moveset = EveMovesetSettings.Parse((GetData(EveId.Parse("base:eve_moveset")).Skill as JObject)?["tuning"]);
        movesetRegistry.Buffs().Get(moveset.BlazingAssaultBuffId); movesetRegistry.Buffs().Get(moveset.RisingMomentumBuffId);
        PNAbility Require(EveId id, PNAbilityKind kind)
        {
            var found = ownedAbilities.Find(id);
            if (found.Count != 1 || found[0].Definition.Kind != kind)
                throw new InvalidOperationException("Missing or incompatible Eve Ability in JSON: " + id);
            return found[0];
        }
        skillRecovery = (PNAbilityCooldown)Require(SkillCooldownId, PNAbilityKind.Cooldown);
        ultimateRecovery = (PNAbilityCooldown)Require(UltimateCooldownId, PNAbilityKind.Cooldown);
        if(DashSettings.RecoveryId!=DashCooldownId)throw new InvalidOperationException("Eve Dash needs its entity recovery Ability.");
        moveset.ResolveMotionProfiles(GetData,DashSettings.Motion);
        gauge = (PNAbilityCounter)Require(BurstGaugeId, PNAbilityKind.Counter);
        stacks = (PNAbilityCounter)Require(SlashStacksId, PNAbilityKind.Counter);
        burst = (PNAbilityState)Require(BurstStateId, PNAbilityKind.State);
        empowerment = (PNAbilityState)Require(EmpowermentId, PNAbilityKind.State);
        if (!burst.Definition.Duration.HasValue || !empowerment.Definition.Duration.HasValue || gauge.Capacity < moveset.BurstCost)
            throw new InvalidOperationException("Eve requires timed Burst/empowerment and sufficient gauge capacity.");
        if (empowerment.Definition.Reapply == PNAbilityReapply.Independent)
            throw new InvalidOperationException("Eve next-attack empowerment requires a single State instance.");
        AttackInput.RepeatDelay = moveset.RepeatDelay; AttackInput.RepeatInterval = moveset.RepeatInterval;
        // EN: Rebuild bindings after content load, including input enabled before load.
        // VI: Cập nhật binding sau nạp dữ liệu, kể cả khi input đã bật trước đó.
        if (isActiveAndEnabled) { base.OnDisable(); base.OnEnable(); }
        ownedAbilities.Changed += OnAbilityChanged;
        Stats.Died += OnEveDied;
        SyncCooldowns();
#if UNITY_5_3_OR_NEWER
        WeaponRig = EveWeaponRig.Create(transform, moveset.Weapon);
#endif
    }
    private bool CanBegin(bool allowInterrupt = false)
    {
        var loader = GetComponent<EveEntityLoader>(); var stat = Stats;
        return CanInput && isActiveAndEnabled && IsReady && loader != null && loader.IsReady &&
            stat != null && stat.IsReady && !stat.IsDead && ownedAbilities != null &&
            (allowInterrupt || !IsBusy);
    }
    private void SyncCooldowns()
    {
        if (ownedAbilities == null || !Stats.IsReady) return;
        double cdr = Stats.Evaluate(PNContentIds.CooldownReduction);
        if (cdr == lastCdr) return;
        ApplyCooldownReduction(skillRecovery); ApplyCooldownReduction(ultimateRecovery); SyncPlayerDashCooldown();
        lastCdr = cdr;
    }
    protected virtual PNStat FindTarget(bool lowestHealth)
    {
#if UNITY_5_3_OR_NEWER
        if (Stats == null || !Stats.IsReady) return null;
        PNStat best = null; double bestHealth = double.MaxValue; float bestDistance = float.MaxValue;
        foreach (var entry in EveRegistrySession.Context.Entities.Snapshot())
        {
            var candidate = entry.GetComponent<PNStat>();
            if (candidate == null || candidate == Stats || !candidate.gameObject.activeInHierarchy || !candidate.isActiveAndEnabled || !candidate.IsReady || candidate.IsDead ||
                candidate.gameObject.scene != gameObject.scene || candidate.SessionId != Stats.SessionId || !Stats.IsHostileTo(candidate)) continue;
            var loader = candidate.GetComponent<EveEntityLoader>();
            if (loader == null || !loader.IsReady) continue;
            var offset = candidate.transform.position - transform.position; offset.y = 0;
            float distance = offset.sqrMagnitude;
            if (distance > moveset.TargetRange * moveset.TargetRange) continue;
            double health = lowestHealth ? candidate.GetCurrent(PNContentIds.Health) : 0;
            if (best == null || health < bestHealth || (health == bestHealth && (distance < bestDistance ||
                (distance == bestDistance && candidate.EntityId.Value < best.EntityId.Value))))
            { best = candidate; bestHealth = health; bestDistance = distance; }
        }
        return best;
#else
        return null;
#endif
    }
    private Vector3 SelectDirection(bool lowest, out PNStat target)
    {
        target = FindTarget(lowest);
        var direction = target == null ? ResolveAttackDirection() : target.transform.position - transform.position;
        direction.y = 0;
        double length = Math.Sqrt((double)direction.x * direction.x + (double)direction.z * direction.z);
        if (length == 0) return Vector3.right;
        return new Vector3((float)(direction.x / length), 0, (float)(direction.z / length));
    }
    private void Face(Vector3 direction)
    {
        var move = GetComponent<PNPlayerMove>();
        if (move != null) move.FaceSkillDirection(new Vector2(direction.x, direction.z));
        else GetComponent<PNMove>()?.TryFaceSkill(new Vector2(direction.x, direction.z));
    }
    private PNSkillActionContext Context(Vector3 direction)
    {
        var data = GetData(EveId.Parse("base:eve_moveset"));
        return new PNSkillActionContext(this, movesetRegistry.Skills.ResolveIdentity(EveId.Parse("base:eve_moveset")), data, movesetRegistry, null, direction);
    }
    private PNSkillActionContext BuffContext() => new PNSkillActionContext(this,
        movesetRegistry.Skills.ResolveIdentity(EveId.Parse("base:eve_moveset")), GetData(EveId.Parse("base:eve_moveset")), movesetRegistry, new[] { Stats }, null, CurrentInvocation);
    private void LockSequence()
    {
        sequenceLock = movesetRegistry.Operations().Get(EveId.Parse("base:lock_movement")).Create(); sequenceLock.Execute(Context(castDirection), null);
    }
    private void BeginNormalAttack(Vector3 direction)
    {
        EndRepeatChain();
        if (comboIdle > moveset.ComboReset) comboIndex = 0;
        CurrentCast = EveCast.Attack; castDirection = direction; comboIdle = 0;
        bool empowered = empowerment.IsActive;
        pendingAttackProfile = (empowered ? "empowered_" : "attack_") + new[] { "right", "left", "horizontal" }[comboIndex];
        if (empowered)
        {
            castTarget = FindTarget(false); Face(castDirection); LockSequence();
            empowerment.TryConsume();
            StartMotion(EveCastPhase.Rush,castDirection,moveset.EmpoweredMotion,moveset.EmpoweredStopDistance);
        }
        else StartSweep(pendingAttackProfile);
        comboIndex = (comboIndex + 1) % 3;
    }
    private void StartSweep(string profile)
    {
        Face(castDirection);
        var id = EveId.Parse("base:eve_" + profile);
        var data = GetData(id);
        var settings = EveSweepSettings.Parse(data.Action.Perform);
        attackContext = new PNSkillActionContext(this, movesetRegistry.Skills.ResolveIdentity(id), data, movesetRegistry, null, castDirection);
        var created = movesetRegistry.Actions().Get(data.Action.Id).Create();
        attack = created as EveSweepAction;
        if (attack == null) { created.Dispose(); throw new InvalidOperationException("Eve requires a sweep-compatible action."); }
        CurrentPhase = EveCastPhase.Slash; phaseRemaining = settings.Duration; rewardedHit = false;
#if UNITY_5_3_OR_NEWER
        energy?.SetMotion(false); energy?.Charge(0);
#endif
        attack.Start(attackContext); attack.Perform(attackContext);
    }
    public bool TrySkill()
    {
        if (!CanBegin() || !CanActivateSkill(PNControlIds.Skill, PNGameplayContentModule.EveSkillEntry) || !Controls.IsAllowed(PNControlIds.VoluntaryMotion) || !skillRecovery.CanConsume()) return false;
        return RunCast(PNGameplayContentModule.EveSkillEntry, () => {
            SyncCooldowns(); skillRecovery.TryConsume();
            CurrentCast = EveCast.Skill; castDirection = SelectDirection(false, out castTarget); Face(castDirection); LockSequence();
            ownedAbilities.GrantState(EmpowermentId);
            if (IsInBurst) stacks.Add(moveset.SkillStacks); else gauge.Add(moveset.SkillGauge);
            ultimateRecovery.ReduceRemaining(moveset.SkillUltimateReduction);
            StartMotion(EveCastPhase.Motion,castDirection,moveset.SkillMotion,moveset.SkillStopDistance);
        });
    }
    protected override EveId PlayerDashSkillId => EveId.Parse("base:eve_dash");
    protected override bool CanStartPlayerDash() => !IsDashing && CanBegin(true) && CurrentCast!=EveCast.Ultimate;
    protected override Vector3 PlayerDashDirection() => ResolveAttackDirection();
    protected override void OnPlayerDashStarting(Vector3 direction)
    {
        CancelMoveset();CurrentCast=EveCast.Dash;CurrentPhase=EveCastPhase.Motion;
        castTarget=null;castDirection=direction;Face(direction);LockSequence();
#if UNITY_5_3_OR_NEWER
        EnsureEnergy().SetMotion(true);
#endif
    }
    protected override void OnPlayerDashEnded(bool cancelled)
    {if(CurrentCast==EveCast.Dash)FinishSequence(cancelled);}
    public bool TryBurst()
    {
        if (!CanBegin() || !CanActivateSkill(PNControlIds.Burst, PNGameplayContentModule.EveBurstEntry) || IsInBurst || !burst.CanActivate || !gauge.CanConsume(moveset.BurstCost)) return false;
        return RunCast(PNGameplayContentModule.EveBurstEntry, () => {
            gauge.TryConsume(moveset.BurstCost); burst.TryActivate(); burstEffectsActive = true;
            Stats.SetSource(BurstSourceId, new[] { new EveStatModifier(PNContentIds.AttackPower, moveset.BurstAttackBonus, 0),
                new EveStatModifier(PNContentIds.AttackSpeed, moveset.BurstAttackSpeedBonus, 0) });
            stacks.Add(moveset.BurstInitialStacks);
            blazingAssault = new EveOwnedBuff(BuffContext(), moveset.BlazingAssaultBuffId);
            repeatStroke = 0;
#if UNITY_5_3_OR_NEWER
            WeaponRig.SetBurst(true); EnsureEnergy().Flash();
#endif
        });
    }
    public bool TryRepeatAttack()
    {
        if (!CanBegin() || !CanActivateSkill(PNControlIds.Attack, PNGameplayContentModule.EveAttackEntry) || !Controls.IsAllowed(PNControlIds.VoluntaryMotion) || !IsInBurst || repeatExhausted || !stacks.CanConsume()) return false;
        return RunCast(PNGameplayContentModule.EveAttackEntry, () => {
            repeatChainActive = true;
            CurrentCast = EveCast.Repeat; castDirection = SelectDirection(false, out castTarget); Face(castDirection); LockSequence();
            stacks.TryConsume();
            StartMotion(EveCastPhase.Rush,castDirection,moveset.RepeatMotion,moveset.TargetStopDistance);
        });
    }
    public bool TryUltimate()
    {
        if (!CanBegin() || !CanActivateSkill(PNControlIds.Ultimate, PNGameplayContentModule.EveUltimateEntry) || !Controls.IsAllowed(PNControlIds.VoluntaryMotion) || !ultimateRecovery.CanConsume()) return false;
        return RunCast(PNGameplayContentModule.EveUltimateEntry, () => {
            SyncCooldowns(); ultimateRecovery.TryConsume(); CurrentCast = EveCast.Ultimate;
            burstFinisher = IsInBurst; ultimateStroke = 0; ultimateRewarded = false;
            castDirection = SelectDirection(false, out castTarget); Face(castDirection); LockSequence();
            StartMotion(EveCastPhase.Retreat,-castDirection,moveset.RetreatMotion);
        });
    }
    private bool RunCast(EveId entryId, Action begin)
    {
        try { BeginActivation(entryId); begin(); CommitActivation(castTarget == null ? null : new[] { castTarget }, castDirection.x == 0 && castDirection.z == 0 ? (Vector3?)null : castDirection); if (!IsBusy) EndActivation(); return true; }
        catch (Exception error)
        {
            try { CancelMoveset(); }
            finally { if (entryId == PNGameplayContentModule.EveBurstEntry && burstEffectsActive) EndBurst(false); }
            Debug.LogException(error, this); return false;
        }
    }
    private float RushDistance(float maximum, float? stopDistance = null)
    {
        if (castTarget == null) return maximum;
        var offset = castTarget.transform.position - transform.position; offset.y = 0;
        return Math.Min(maximum, Math.Max(0, offset.magnitude - (stopDistance ?? moveset.TargetStopDistance)));
    }
    private void StartMotion(EveCastPhase phase,Vector3 direction,EveId profile,float? stopDistance=null)
    {
        var settings=PNMotionSettings.Parse(GetData(profile).Action.Perform);
        float distance=stopDistance.HasValue?RushDistance(settings.Distance,stopDistance):settings.Distance;
        CurrentPhase=phase;phaseRemaining=settings.Duration;
        motion=CreateMotionAction(profile,direction,out motionContext,distance);
        var ownedMotion=motion;
        motion.BindMovementOwner(()=>{if(ReferenceEquals(motion,ownedMotion))CancelMoveset();});
        motion.Start(motionContext);motion.Perform(motionContext);
#if UNITY_5_3_OR_NEWER
        EnsureEnergy().SetMotion(true);
#endif
    }
    private void AdvanceMoveset(float delta)
    {
        AdvanceAuxiliaryEffects(delta);
        if (ownedAbilities == null) return;
        if (Stats == null || !Stats.IsReady || !GetComponent<EveEntityLoader>().IsReady) { CancelMoveset(); return; }
        if (Stats.IsDead) { OnEveDied(null); return; }
        deathHandled = false;
        try
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            SyncCooldowns();
            if(IsDashing){AdvancePlayerDash(delta);return;}
            if (!IsBusy) { comboIdle += delta; return; }
            double budget = delta;
            for (int transitions = 0; IsBusy && budget > 0 && transitions < 32; transitions++)
            {
                double speed = ExecutionSpeed;
                if (speed <= 0) break;
                double available = budget * speed;
                if (double.IsNaN(available) || double.IsInfinity(available)) throw new OverflowException("Eve cast delta overflow.");
                float step = (float)Math.Min(phaseRemaining, available);
                budget = Math.Max(0, budget - step / speed);
                phaseRemaining = Math.Max(0, phaseRemaining - step);
                if (motion != null) motion.Advance(step);
                if (attack != null)
                {
                    var active = attack; active.Advance(step);
                    if (ReferenceEquals(active, attack) && !rewardedHit && active.Results.Count != 0)
                    { rewardedHit = true; OnSlashHit(); }
                }
#if UNITY_5_3_OR_NEWER
                if (CurrentPhase == EveCastPhase.Charge) EnsureEnergy().Charge(1 - phaseRemaining / moveset.ChargeDuration);
                else energy?.SetMotion(motion != null);
#endif
                if (!IsBusy) break;
                if (phaseRemaining <= .000001f) CompletePhase(); else break;
            }
        }
        catch (Exception error) { CancelMoveset(); Debug.LogException(error, this); }
    }
    private void OnSlashHit()
    {
        if (CurrentCast == EveCast.Attack)
        {
            if (IsInBurst) stacks.Add(moveset.AttackStacks); else gauge.Add(moveset.AttackGauge);
            ultimateRecovery.ReduceRemaining(moveset.AttackUltimateReduction);
        }
        else if (CurrentCast == EveCast.Repeat)
        {
            ultimateRecovery.ReduceRemaining(moveset.AttackUltimateReduction);
            if (repeatChainActive && IsInBurst)
            {
                if (risingMomentum?.Snapshot != null || risingMomentum?.IsPending == true) risingMomentum.Reapply(attackContext);
                else { risingMomentum?.Dispose(); risingMomentum = new EveOwnedBuff(attackContext, moveset.RisingMomentumBuffId); }
            }
        }
        else if (CurrentCast == EveCast.Ultimate && !ultimateRewarded)
        {
            ultimateRewarded = true;
            if (!IsInBurst) gauge.Add(moveset.UltimateGauge);
        }
    }
    private void CompletePhase()
    {
        var phase = CurrentPhase;
        motion?.Exit(motionContext); motion = null; motionContext = null;
        if (attack != null) FinishAttack(false);
        if (CurrentCast == EveCast.Attack && phase == EveCastPhase.Rush)
        { StartSweep(pendingAttackProfile); return; }
        if (CurrentCast == EveCast.Repeat && phase == EveCastPhase.Rush)
        { StartSweep(repeatStroke++ % 2 == 0 ? "repeat_right" : "repeat_left"); return; }
        if (CurrentCast == EveCast.Ultimate)
        {
            if (phase == EveCastPhase.Retreat)
            {
                CurrentPhase = EveCastPhase.Charge; phaseRemaining = moveset.ChargeDuration;
#if UNITY_5_3_OR_NEWER
                EnsureEnergy().SetMotion(false); EnsureEnergy().Charge(.001f);
#endif
                return;
            }
            if (phase == EveCastPhase.Charge)
            {
#if UNITY_5_3_OR_NEWER
                energy?.Charge(0);
#endif
                castDirection = SelectDirection(true, out castTarget); Face(castDirection);
                StartMotion(EveCastPhase.Rush,castDirection,moveset.UltimateMotion,moveset.TargetStopDistance); return;
            }
            if (phase == EveCastPhase.Rush || (phase == EveCastPhase.Slash && burstFinisher && ++ultimateStroke < 3))
            {
                StartSweep(burstFinisher ? "burst_ultimate_" + new[] { "right", "left", "horizontal" }[ultimateStroke] : "ultimate"); return;
            }
            if (burstFinisher) EndBurst(true);
        }
        bool wasRepeat = CurrentCast == EveCast.Repeat;
        bool again = wasRepeat && repeatHeld && IsInBurst && stacks.Current > 0;
        if (CurrentCast == EveCast.Repeat && stacks.Current == 0) repeatExhausted = true;
        FinishSequence();
        if (wasRepeat && (!again || !TryRepeatAttack())) EndRepeatChain();
    }
    private void EndRepeatChain()
    {
        repeatChainActive = false;
        var owned = risingMomentum; risingMomentum = null; owned?.Dispose();
    }
    private void FinishSequence(bool cancelled = false)
    {
        if (cancelled) EndRepeatChain();
        EndActivation(cancelled);
        sequenceLock?.Dispose(); sequenceLock = null; CurrentCast = EveCast.None; CurrentPhase = EveCastPhase.None;
        castTarget = null; pendingAttackProfile = null; phaseRemaining = 0;
#if UNITY_5_3_OR_NEWER
        energy?.SetMotion(false); energy?.Charge(0);
#endif
    }
    private void OnAbilityChanged(PNAbilityChange change)
    {
        if (change.After?.Id == BurstStateId && !change.After.IsActive && burstEffectsActive && !burstFinisher)
            EndBurst(change.Reason == PNAbilityChangeReason.Expired || change.Reason == PNAbilityChangeReason.Ended);
        if (change.Reason == PNAbilityChangeReason.Reset) lastCdr = double.NaN;
    }
    private void EndBurst(bool refund)
    {
        if (!burstEffectsActive) return;
        burstEffectsActive = false; burstFinisher = false;
        var owned = blazingAssault; blazingAssault = null; owned?.Dispose(); EndRepeatChain();
        if (CurrentCast == EveCast.Repeat) CancelMoveset();
        if (burst.IsAttached && burst.IsActive) burst.End();
        if (stacks.IsAttached) stacks.Clear(); if (Stats != null && Stats.IsReady) Stats.RemoveSource(BurstSourceId);
        if (refund && gauge.IsAttached) gauge.Add(moveset.BurstEndRefund);
#if UNITY_5_3_OR_NEWER
        WeaponRig?.SetBurst(false); energy?.SetBurst(false);
#endif
    }
    public override bool TryInterrupt(EveId reason)
    {
        if (!reason.IsValid) throw new ArgumentException("Interruption reason required.");
        if (!IsReady || !IsBusy) return false;
        CancelMoveset(); return true;
    }
    private void CancelMoveset()
    {
        bool finishingBurst = burstFinisher; burstFinisher = false;
        try { FinishAttack(true); }
        finally
        {
            try { CancelPlayerDash(); }
            finally
            {
                var previousMotion = motion; var previousContext = motionContext; motion = null; motionContext = null;
                try { previousMotion?.Cancel(previousContext); }
                finally
                {
                    try { previousMotion?.Dispose(); }
                    finally { try { FinishSequence(true); } finally { repeatHeld = false; } }
                }
            }
        }
#if UNITY_5_3_OR_NEWER
        energy?.CancelCast();
#endif
        if (finishingBurst) EndBurst(false);
    }
    private void OnEveDied(PNDamageResult result)
    {
        if (deathHandled) return;
        deathHandled = true;
        CancelMoveset(); EndBurst(false); ownedAbilities.ResetAll(); comboIndex = repeatStroke = 0;
#if UNITY_5_3_OR_NEWER
        energy?.Clear();
#endif
    }
    private void UnloadMoveset()
    {
        CancelMoveset(); EndBurst(false);
        blazingAssault?.Dispose(); blazingAssault = null; EndRepeatChain();
        if (ownedAbilities != null) ownedAbilities.Changed -= OnAbilityChanged;
        if (Stats != null) Stats.Died -= OnEveDied;
        ownedAbilities = null; movesetRegistry = null; loadedStats = null; lastCdr = double.NaN;
        skillRecovery = ultimateRecovery = null; gauge = stacks = null; burst = empowerment = null;
        comboIndex = repeatStroke = 0; comboIdle = 0; repeatExhausted = false; deathHandled = false;
#if UNITY_5_3_OR_NEWER
        if (WeaponRig != null) { EveVfxUtility.DestroyResource(WeaponRig.gameObject); WeaponRig = null; }
        if (energy != null)
        {
            if (Application.isPlaying) Destroy(energy.gameObject); else DestroyImmediate(energy.gameObject);
            energy = null;
        }
#endif
    }
#if UNITY_5_3_OR_NEWER
    private EveEnergyVisual EnsureEnergy()
    {
        if (energy == null) energy = EveEnergyVisual.Create(transform, moveset.Energy);
        energy.SetBurst(IsInBurst); return energy;
    }
#endif
}
