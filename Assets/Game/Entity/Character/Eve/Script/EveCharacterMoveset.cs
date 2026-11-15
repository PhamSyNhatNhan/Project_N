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
    // Combo state (Docs/EveMovesetRefactorPlan.md E1) lives in entity Abilities. Not held here: the idle time (`IdleTime`, G3b) is the framework's clock: it only runs while Eve is free
    // and Abilities cannot pause; the host sends the step, a client ticks its own idle time.
    public static readonly EveId ComboStepId = EveId.Parse("project_n:eve_combo_step");
    public static readonly EveId RepeatStrokeId = EveId.Parse("project_n:eve_repeat_stroke");
    public static readonly EveId UltimateStrokeId = EveId.Parse("project_n:eve_ultimate_stroke");
    public static readonly EveId RepeatExhaustedId = EveId.Parse("project_n:eve_repeat_exhausted");
    // G3c: active while the Repeat chain is landing hits (it spans several casts, one per stroke). Its `stateEffects` entry grants Rising Momentum; each further hit adds a tier.
    public static readonly EveId RepeatChainId = EveId.Parse("project_n:eve_repeat_chain");
    private static readonly EveId RisingMomentumBuffId = EveId.Parse("base:eve_rising_momentum");
    private static readonly EveId BlazingAssaultBuffId = EveId.Parse("base:eve_blazing_assault");
    private EveMovesetSettings moveset;
    public EveMovesetSettings Moveset => moveset;
    public EveCast CurrentCast { get; private set; }
    public EveCastPhase CurrentPhase { get; private set; }
    public PNStat CurrentTarget => castTarget;
    public bool IsBusy => CurrentCast != EveCast.None;
    // The target the running cast follows (the checks read what a decision point picked).
    public PNStat CastTarget => castTarget;
    // The Burst state is on (the replica's on a client), or a finishing Ultimate still holds its effects after the state ended.
    public bool IsInBurst => burst != null && burst.IsAttached && burst.IsActive || burstFinisher && HasGameplayAuthority;
    public int ComboIndex => comboStep?.Current ?? 0;
    public PNAbilityCounter BurstGauge => gauge;
    public PNAbilityCounter SlashStacks => stacks;
    public PNAbilityCooldown SkillRecovery => skillRecovery;
    public PNAbilityCooldown UltimateRecovery => ultimateRecovery;
    public PNAbilityState Empowerment => empowerment;
    public PNAbilityState BurstState => burst;
    public PNBuffSnapshot BlazingAssault => blazingAssault?.Snapshot;
    public PNBuffSnapshot RisingMomentum => risingMomentum?.Snapshot;
    private EveOwnedBuff blazingAssault, risingMomentum;
    private PNAbilityState repeatChain;
    public bool IsRepeatChainActive => repeatChain != null && repeatChain.IsAttached && repeatChain.IsActive;
    private PNStat loadedStats;
    private PNStat Stats => loadedStats;
    private EveRegistryHub movesetRegistry;
    private PNAbilityCollection ownedAbilities;
    private PNAbilityCooldown skillRecovery, ultimateRecovery;
    private PNAbilityCounter gauge, stacks, comboStep, repeatStrokes, ultimateStrokes;
    private PNAbilityState burst, empowerment, repeatExhausted;
    private PNMotionAction motion;
    private PNSkillActionContext motionContext;
    private PNSkillOperation sequenceLock;
    private double lastCdr = double.NaN;
    // G3b: the held Attack gesture is the binding layer's (`IsGestureHeld`); the idle clock is the framework's (`IdleTime`, `ResetIdle`).
    public bool IsRepeatHeld => IsGestureHeld(PNSkillSlots.Attack, PNSkillInteraction.Repeat);
    private bool deathHandled;
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
        comboStep = (PNAbilityCounter)Require(ComboStepId, PNAbilityKind.Counter);
        repeatStrokes = (PNAbilityCounter)Require(RepeatStrokeId, PNAbilityKind.Counter);
        ultimateStrokes = (PNAbilityCounter)Require(UltimateStrokeId, PNAbilityKind.Counter);
        repeatExhausted = (PNAbilityState)Require(RepeatExhaustedId, PNAbilityKind.State);
        repeatChain = (PNAbilityState)Require(RepeatChainId, PNAbilityKind.State);
        if (repeatChain.Definition.Duration.HasValue) throw new InvalidOperationException("The Eve Repeat chain state must be untimed.");
        if (comboStep.Capacity != 2 || ultimateStrokes.Capacity != 3 || repeatStrokes.Capacity < 2 || repeatExhausted.Definition.Duration.HasValue)
            throw new InvalidOperationException("Eve combo Abilities need combo step 0..2, ultimate stroke 0..3, a repeat stroke counter and an untimed exhausted state.");
        if (!burst.Definition.Duration.HasValue || !empowerment.Definition.Duration.HasValue || gauge.Capacity < moveset.BurstCost)
            throw new InvalidOperationException("Eve requires timed Burst/empowerment and sufficient gauge capacity.");
        if (empowerment.Definition.Reapply == PNAbilityReapply.Independent)
            throw new InvalidOperationException("Eve next-attack empowerment requires a single State instance.");
        AttackInput.RepeatDelay = moveset.RepeatDelay; AttackInput.RepeatInterval = moveset.RepeatInterval;
        // EN: Rebuild bindings after content load, including input enabled before load.
        // VI: Cập nhật binding sau nạp dữ liệu, kể cả khi input đã bật trước đó.
        if (isActiveAndEnabled) { base.OnDisable(); base.OnEnable(); }
        ownedAbilities.Changed += OnAbilityChanged;
        BindStateEffects(ownedAbilities, PNStateEffectData.ParseAll((GetData(EveId.Parse("base:eve_moveset")).Skill as JObject)?["stateEffects"]));
        Stats.Died += OnEveDied;
        SyncCooldowns();
#if UNITY_5_3_OR_NEWER
        WeaponRig = EveWeaponRig.Create(transform, moveset.Weapon);
#endif
    }
    // The cast being run is a guess when its invocation says Predicted (G2): the invocation owns the Ability scope, no activation is committed. The guess is the same code as the authority's cast (G6a).
    private bool predictingCast => CurrentInvocation != null && CurrentInvocation.Mode == PNSkillMode.Predicted;
    public bool IsPredictingCast => predictingCast;
    // What only a running cast, or the authority, may write: the Abilities of the state machine (a replica refuses writes outside a guess's scope).
    private bool OwnsAbilityWrites => HasGameplayAuthority || predictingCast;
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
        // Ordinary casts follow the player's auto target setting and the character-wide held target; the ultimate keeps its own rule
        // (lowest absolute health) without touching the held target.
        var auto = PNAutoTarget.For(Stats);
        return lowestHealth ? auto.Resolve(moveset.TargetRange, Stats.TargetLayers.Mask, PNTargetRule.Autotarget(PNTargetMode.LowestHealth))
            : auto.Resolve(moveset.TargetRange, Stats.TargetLayers.Mask);
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
    // Current or last movement direction on the ground plane (the Dash rule): no target acquisition.
    private Vector3 MovementDirection()
    {
        var direction = ResolveAttackDirection(); direction.y = 0;
        double length = Math.Sqrt((double)direction.x * direction.x + (double)direction.z * direction.z);
        return length == 0 ? Vector3.right : new Vector3((float)(direction.x / length), 0, (float)(direction.z / length));
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
        sequenceLock = movesetRegistry.Operations().Get(EveId.Parse("base:lock_movement")).Create();
        // The lock belongs to the cast: the single cancel path drops it (a cast that ends well drops it in `ResetCast`).
        lockTrack = CurrentInvocation?.Track(_ => DisposeLock());
        sequenceLock.Execute(Context(castDirection), null);
    }
    private PNCastTrack lockTrack, motionTrack;
    private void DisposeLock() { var value = sequenceLock; sequenceLock = null; lockTrack?.Release(); lockTrack = null; value?.Dispose(); }
    private static readonly string[] StrokeNames = { "right", "left", "horizontal" };
    // ---- Decisions (E2): read-only; the matching Begin* executes them. ----
    private EveCastDecision DecideAttack(Vector3 direction)
    {
        bool reset = IdleTime > moveset.ComboReset, empowered = empowerment.IsActive;
        int step = reset ? 0 : comboStep.Current;
        return new EveCastDecision(EveCast.Attack, direction, empowered ? FindTarget(false) : null, step, reset, empowered, false, (empowered ? "empowered_" : "attack_") + StrokeNames[step]);
    }
    private EveCastDecision DecideSkill() => new EveCastDecision(EveCast.Skill, MovementDirection(), null);
    private EveCastDecision DecideRepeat() { var direction = SelectDirection(false, out var target); return new EveCastDecision(EveCast.Repeat, direction, target); }
    private EveCastDecision DecideUltimate() { var direction = SelectDirection(false, out var target); return new EveCastDecision(EveCast.Ultimate, direction, target, finisher: IsInBurst); }
    // ---- Execution ----
    private void BeginNormalAttack(EveCastDecision decision)
    {
        EndRepeatChain();
        if (decision.ResetCombo) comboStep.SetAmount(0);
        CurrentCast = EveCast.Attack; castDirection = decision.Direction; ResetIdle();
        pendingAttackProfile = decision.Profile;
        if (decision.Empowered)
        {
            castTarget = decision.Target; Face(castDirection); LockSequence();
            empowerment.TryConsume();
            StartMotion(EveCastPhase.Rush,castDirection,moveset.EmpoweredMotion,moveset.EmpoweredStopDistance);
        }
        else StartSweep(pendingAttackProfile);
        comboStep.SetAmount((decision.Step + 1) % 3);
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
        attackTrack = CurrentInvocation?.Track(_ => FinishAttack(true));
        CurrentPhase = EveCastPhase.Slash; phaseRemaining = settings.Duration; rewardedHit = false;
#if UNITY_5_3_OR_NEWER
        energy?.SetMotion(false); energy?.Charge(0);
#endif
        if (!predictingCast) EmitCastEvent(SweepEvent, profile, castDirection.x, castDirection.z, (float)settings.Duration);
        attack.Start(attackContext); attack.Perform(attackContext);
    }
    public bool TrySkill()
    {
        if (!CanBegin() || !CanActivateSkill(PNControlIds.Skill, PNGameplayContentModule.EveSkillEntry) || !Controls.IsAllowed(PNControlIds.VoluntaryMotion) || !skillRecovery.CanConsume()) return false;
        return RunCast(PNGameplayContentModule.EveSkillEntry, StartSkillCast);
    }
    // One body for every machine: the services decide what it does there (G2). A guess writes the Abilities inside its scope, the authority writes them for real.
    private void StartSkillCast()
    {
        var decision = DecideSkill();
        SyncCooldowns(); skillRecovery.TryConsume();
        CurrentCast = EveCast.Skill; castTarget = null; castDirection = decision.Direction; Face(castDirection); LockSequence();
        ownedAbilities.GrantState(EmpowermentId);
        if (IsInBurst) stacks.Add(moveset.SkillStacks); else gauge.Add(moveset.SkillGauge);
        ultimateRecovery.ReduceRemaining(moveset.SkillUltimateReduction);
        StartMotion(EveCastPhase.Motion,castDirection,moveset.SkillMotion);
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
        return RunCast(PNGameplayContentModule.EveBurstEntry, StartBurstCast);
    }
    // The Burst start, the same on the authority and in a guess: the state's stat source and Buffs are the engine's (`stateEffects`), not written here.
    private void StartBurstCast()
    {
        gauge.TryConsume(moveset.BurstCost); burst.TryActivate();
        stacks.Add(moveset.BurstInitialStacks); EmitCastEvent(BurstEvent, "1", 0f, 0f, 0f);
        repeatStrokes.SetAmount(0);
#if UNITY_5_3_OR_NEWER
        WeaponRig.SetBurst(true); EnsureEnergy().Flash();
#endif
    }
    public bool TryRepeatAttack()
    {
        if (!CanBegin() || !CanActivateSkill(PNControlIds.Attack, PNGameplayContentModule.EveAttackEntry) || !Controls.IsAllowed(PNControlIds.VoluntaryMotion) || !IsInBurst || repeatExhausted.IsActive || !stacks.CanConsume()) return false;
        return RunCast(PNGameplayContentModule.EveAttackEntry, StartRepeatCast);
    }
    private void StartRepeatCast() => DecideAndStartRepeatStroke();
    // A stroke of the Repeat chain: the first one, and the next ones while the gesture is held.
    private void DecideAndStartRepeatStroke()
    {
        var decision = DecideRepeat();
        CurrentCast = EveCast.Repeat; castDirection = decision.Direction; castTarget = decision.Target; Face(castDirection); LockSequence();
        stacks.TryConsume();
        StartMotion(EveCastPhase.Rush, castDirection, moveset.RepeatMotion, moveset.TargetStopDistance);
    }
    public bool TryUltimate()
    {
        if (!CanBegin() || !CanActivateSkill(PNControlIds.Ultimate, PNGameplayContentModule.EveUltimateEntry) || !Controls.IsAllowed(PNControlIds.VoluntaryMotion) || !ultimateRecovery.CanConsume()) return false;
        return RunCast(PNGameplayContentModule.EveUltimateEntry, StartUltimateCast);
    }
    private void StartUltimateCast()
    {
        var decision = DecideUltimate();
        SyncCooldowns(); ultimateRecovery.TryConsume(); CurrentCast = EveCast.Ultimate;
        burstFinisher = decision.Finisher; ultimateStrokes.SetAmount(0); ultimateRewarded = false;
        castDirection = decision.Direction; castTarget = decision.Target; Face(castDirection); LockSequence();
        StartMotion(EveCastPhase.Retreat,-castDirection,moveset.RetreatMotion);
    }
    private bool RunCast(EveId entryId, Action begin)
    {
        try { BeginActivation(entryId); begin(); CommitActivation(castTarget == null ? null : new[] { castTarget }, castDirection.x == 0 && castDirection.z == 0 ? (Vector3?)null : castDirection); if (!IsBusy) EndActivation(); return true; }
        catch (Exception error)
        {
            try { CancelMoveset(); }
            finally { if (entryId == PNGameplayContentModule.EveBurstEntry) EndBurst(PNAbilityChangeReason.Reset); }
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
        if(!predictingCast)EmitCastEvent(MotionEvent,phase.ToString(),direction.x,direction.z,(float)settings.Duration);
        motion=CreateMotionAction(profile,direction,out motionContext,distance);
        motionTrack=CurrentInvocation?.Track(_=>CancelMotion());
        var ownedMotion=motion;
        motion.BindMovementOwner(()=>{if(ReferenceEquals(motion,ownedMotion))CancelMoveset();});
        motion.Start(motionContext);motion.Perform(motionContext);
#if UNITY_5_3_OR_NEWER
        EnsureEnergy().SetMotion(true);
#endif
    }
    private void AdvanceMoveset(float delta)
    {
        bool authority = HasGameplayAuthority;
        if (!authority) AdvanceObserved(delta);
        if (!authority)
        {
            // The owner's client: it only runs what it guessed (a predicted Dash, a predicted cast); everything else is the authority's.
            if (IsPredictingDash) { AdvancePlayerDash(delta); return; }
            if (!predictingCast) return;
        }
        else { AdvanceAuxiliaryEffects(delta); AdvanceBindings(delta); }
        if (ownedAbilities == null) return;
        if (Stats == null || !Stats.IsReady || !GetComponent<EveEntityLoader>().IsReady) { CancelMoveset(); return; }
        if (Stats.IsDead) { OnEveDied(null); return; }
        deathHandled = false;
        try
        {
            if (float.IsNaN(delta) || float.IsInfinity(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            if (authority) SyncCooldowns();
            if(IsDashing){if (authority) AdvancePlayerDash(delta);return;}
            AdvanceIdle(delta);
            if (!IsBusy) return;
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
                if (attack != null) attack.Advance(step);
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
    // ---- Host-only effects (E3): rewards earned by a hit, the Burst stat source and the Buffs. A predicting client never runs these; the host runs them at the same points. ----
    // The Burst's stat source and Blazing Assault Buff are declared in the skill data (`stateEffects`); the engine grants them while the Burst state is on.
    protected override IDisposable StartStateBuff(EveId buffId)
    {
        var owned = new EveOwnedBuff(BuffContext(), buffId);
        if (buffId == BlazingAssaultBuffId) blazingAssault = owned;
        else if (buffId == RisingMomentumBuffId) risingMomentum = owned;
        return owned;
    }
    // A finishing Ultimate keeps the Burst's effects past the state's end until it is over.
    protected override bool KeepsStateEffects(EveId state) => state == BurstStateId && burstFinisher;
    // The Burst is over (authority; its stat source and Buffs are already off).
    protected override void OnAbilityEnded(EveId state, PNAbilityChangeReason reason)
    {
        if (state == RepeatChainId) { risingMomentum = null; return; }
        if (state != BurstStateId) return;
        burstFinisher = false; blazingAssault = null; EmitCastEvent(BurstEvent, "0", 0f, 0f, 0f);
        EndRepeatChain();
        if (CurrentCast == EveCast.Repeat) CancelMoveset();
        if (stacks.IsAttached) stacks.Clear();
        if ((reason == PNAbilityChangeReason.Expired || reason == PNAbilityChangeReason.Ended) && gauge.IsAttached) gauge.Add(moveset.BurstEndRefund);
#if UNITY_5_3_OR_NEWER
        WeaponRig?.SetBurst(false); energy?.SetBurst(false);
#endif
    }
    // The first hit of the chain starts the chain state (its `stateEffects` grant Rising Momentum at one tier); each later hit adds a tier. A Momentum that was taken off by something else restarts the chain.
    private void HostRisingMomentum()
    {
        if (repeatChain.IsActive && (risingMomentum?.Snapshot != null || risingMomentum?.IsPending == true)) { risingMomentum.Reapply(attackContext); return; }
        if (repeatChain.IsActive) repeatChain.End();
        repeatChain.TryActivate();
    }
    // G2: the first hit of a slash earns its reward once (authority only: the context calls this when the damage was dealt).
    public override void OnHitConfirmed(PNSkillActionContext context, PNStat target, PNDamageResult result)
    {
        if (rewardedHit || attackContext == null || !ReferenceEquals(context, attackContext)) return;
        rewardedHit = true; HostOnSlashHit();
    }
    private void HostOnSlashHit()
    {
        if (CurrentCast == EveCast.Attack)
        {
            if (IsInBurst) stacks.Add(moveset.AttackStacks); else gauge.Add(moveset.AttackGauge);
            ultimateRecovery.ReduceRemaining(moveset.AttackUltimateReduction);
        }
        else if (CurrentCast == EveCast.Repeat)
        {
            ultimateRecovery.ReduceRemaining(moveset.AttackUltimateReduction);
            if (IsInBurst) HostRisingMomentum();
        }
        else if (CurrentCast == EveCast.Ultimate && !ultimateRewarded)
        {
            ultimateRewarded = true;
            if (!IsInBurst) gauge.Add(moveset.UltimateGauge);
        }
    }
    // ---- Decision points inside a cast (E4, G4): the only places where a running cast chooses again. Each is a `Choose`: the skill gives its own choice (`compute`) and its rule for a proposal; for a remote owner the
    //      framework carries the owner's choice and the authority takes it only if the framework finds it well formed and the rule accepts it (otherwise `compute` stands). CompletePhase applies the answer. ----
    // UltimateRetarget: after the Charge the ultimate picks the lowest-health target again and turns toward it.
    private EveCastDecision DecideUltimateRetarget()
    {
        Vector3 chosenDirection = default; PNStat chosenTarget = null; bool computed = false;
        // The target is what is chosen; the framework checks it is an entity in reach, the rule here is only that it is alive and on a layer this character targets.
        var picked = Choose("ult_retarget", () => { chosenDirection = SelectDirection(true, out chosenTarget); computed = true; return chosenTarget; },
            candidate => Stats != null && !candidate.IsDead && PNLayers.Contains(Stats.TargetLayers.Mask, candidate.Layer), moveset.TargetRange);
        if (!computed && picked != null)
        {
            var toward = picked.transform.position - transform.position; toward.y = 0; double length = Math.Sqrt((double)toward.x * toward.x + (double)toward.z * toward.z);
            if (length > 0) return new EveCastDecision(EveCast.Ultimate, new Vector3((float)(toward.x / length), 0, (float)(toward.z / length)), picked, finisher: burstFinisher);
            chosenDirection = SelectDirection(true, out chosenTarget); // a target on top of us gives no direction: choose by our own rule
        }
        return new EveCastDecision(EveCast.Ultimate, chosenDirection, chosenTarget, finisher: burstFinisher);
    }
    // EmpoweredStrike: at the end of the empowered rush the slash chosen when the cast started follows. A proposal may only name an empowered slash (it cannot turn the cast into another kind).
    private string DecideEmpoweredStrike() => Choose("empowered_strike", () => pendingAttackProfile,
        profile => profile.StartsWith("empowered_", StringComparison.Ordinal) && Array.IndexOf(StrokeNames, profile.Substring("empowered_".Length)) >= 0);
    // RepeatStroke: the stroke a Repeat rush ends with alternates right and left by the stroke counter.
    private string DecideRepeatStroke() => repeatStrokes.Current % 2 == 0 ? "repeat_right" : "repeat_left";
    // RepeatContinue: at the end of a Repeat stroke, whether the held Attack gesture chains another one (a binding change cancels the gesture, which ends the hold).
    private bool DecideRepeatContinue()
    {
        // For a remote owner the gesture at this point is what ITS machine saw (a release still in flight must not chain one more stroke): a proposal can only stop a chain, never extend one.
        bool held = Choose("repeat_continue", () => IsRepeatHeld, proposed => !proposed || IsRepeatHeld);
        return held && IsInBurst && stacks.Current > 0;
    }
    private void CompletePhase()
    {
        var phase = CurrentPhase;
        motionTrack?.Release(); motionTrack = null;
        motion?.Exit(motionContext); motion = null; motionContext = null;
        if (attack != null) FinishAttack(false);
        if (CurrentCast == EveCast.Attack && phase == EveCastPhase.Rush)
        { StartSweep(DecideEmpoweredStrike()); return; }
        if (CurrentCast == EveCast.Repeat && phase == EveCastPhase.Rush)
        { var stroke = DecideRepeatStroke(); repeatStrokes.Add(1); StartSweep(stroke); return; }
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
                var retarget = DecideUltimateRetarget(); castDirection = retarget.Direction; castTarget = retarget.Target; Face(castDirection);
                StartMotion(EveCastPhase.Rush,castDirection,moveset.UltimateMotion,moveset.TargetStopDistance); return;
            }
            bool nextStroke = phase == EveCastPhase.Rush;
            if (!nextStroke && phase == EveCastPhase.Slash && burstFinisher) { ultimateStrokes.Add(1); nextStroke = ultimateStrokes.Current < 3; }
            if (nextStroke)
            {
                StartSweep(burstFinisher ? "burst_ultimate_" + StrokeNames[ultimateStrokes.Current] : "ultimate"); return;
            }
            if (burstFinisher) EndBurst(PNAbilityChangeReason.Ended);
        }
        bool wasRepeat = CurrentCast == EveCast.Repeat;
        bool again = wasRepeat && DecideRepeatContinue();
        if (CurrentCast == EveCast.Repeat && stacks.Current == 0) repeatExhausted.TryActivate();
        FinishSequence();
        if (wasRepeat && (!again || !TryRepeatAttack())) EndRepeatChain();
    }
    // The player let go of Attack: the exhausted mark of the Repeat chain ends with the gesture (safe after unload).
    private void ClearRepeatExhausted() { if (OwnsAbilityWrites && repeatExhausted != null && repeatExhausted.IsAttached && repeatExhausted.IsActive) repeatExhausted.End(); }
    private void EndRepeatChain()
    {
        if (OwnsAbilityWrites && repeatChain != null && repeatChain.IsAttached && repeatChain.IsActive) repeatChain.End();
    }
    // A cast is over (it finished, or `cancelled`): ends its invocation, whose `OnCastEnded` resets the state machine. With no invocation to end, reset directly.
    private void FinishSequence(bool cancelled = false)
    {
        var invocation = CurrentInvocation;
        if (invocation != null && !invocation.IsEnded) EndActivation(cancelled, cancelled ? PNCastEnd.Interrupted : null);
        else ResetCast(cancelled);
    }
    // G3a: the one end hook (L6). Runs once per invocation, finished or cancelled, with the cast memory still readable; a cast that ran no sequence (Burst has no phases) has nothing to reset.
    protected override void OnCastEnded(PNSkillInvocation invocation, bool cancelled, string reason)
    {
        try { if (IsBusy || cancelled) ResetCast(cancelled); }
        finally { detachedCast = invocation.Memory.Use<EveCastMemory>(); }
    }
    // The state machine back to idle: what the cast still held (lock, repeat chain) is released, the marks of the finished cast stay readable until the next one starts.
    private void ResetCast(bool cancelled)
    {
        if (cancelled) EndRepeatChain();
        DisposeLock(); CurrentCast = EveCast.None; CurrentPhase = EveCastPhase.None;
        castTarget = null; pendingAttackProfile = null; phaseRemaining = 0;
#if UNITY_5_3_OR_NEWER
        energy?.SetMotion(false); energy?.Charge(0);
#endif
    }
    // The motion of the running phase stopped for good (cancel path and fallback).
    private void CancelMotion()
    {
        var previousMotion = motion; var previousContext = motionContext; motion = null; motionContext = null;
        motionTrack?.Release(); motionTrack = null;
        try { previousMotion?.Cancel(previousContext); }
        finally { previousMotion?.Dispose(); }
    }
    private void OnAbilityChanged(PNAbilityChange change)
    {
#if UNITY_5_3_OR_NEWER
        // A replica shows the Burst's end from the state the authority sent; the authority's own visuals go off in `OnAbilityEnded`.
        if (!HasGameplayAuthority && change.After?.Id == BurstStateId && !change.After.IsActive) { WeaponRig?.SetBurst(false); energy?.SetBurst(false); }
#endif
        if (change.Reason == PNAbilityChangeReason.Reset) lastCdr = double.NaN;
    }
    // Ends the Burst on purpose (the authority only): `Ended` pays the refund (a finisher done), `Reset` does not (death, cancel, unload). The engine takes the effects off and calls `OnAbilityEnded`.
    private void EndBurst(PNAbilityChangeReason reason)
    {
        if (!HasGameplayAuthority) { burstFinisher = false; return; }
        if (burst == null || !burst.IsAttached) return;
        if (burst.IsActive) { burstFinisher = false; if (reason == PNAbilityChangeReason.Ended) burst.End(); else burst.Reset(); }
        else ReleaseStateEffects(BurstStateId, reason);
    }
    public override bool TryInterrupt(EveId reason)
    {
        if (!reason.IsValid) throw new ArgumentException("Interruption reason required.");
        if (!IsReady || !IsBusy && !IsDashing) return false; // a guessed Dash is not a moveset cast (IsBusy), but it is running
        CancelMoveset(reason.ToString()); return true;
    }
    // Every way a cast stops (interrupt, death, disable, unload, a refused guess, a motion that lost its body) comes here, and from here to the one cancel path (`CancelCast`): what the cast registered
    // is cancelled, the hook resets the state machine once. The direct branch is for a state machine with no invocation to end (a Dash guess, a half-built cast).
    private void CancelMoveset(string reason = PNCastEnd.Interrupted)
    {
        EndObserved();
        bool finishingBurst = burstFinisher; burstFinisher = false;
        try { CancelPlayerDash(); }
        finally
        {
            try
            {
                if (!CancelCast(reason))
                {
                    try { FinishAttack(true); }
                    finally { try { CancelMotion(); } finally { ResetCast(true); } }
                }
            }
            finally { EndGestureHold(PNSkillSlots.Attack, PNSkillInteraction.Repeat); }
        }
#if UNITY_5_3_OR_NEWER
        energy?.CancelCast();
#endif
        if (finishingBurst) EndBurst(PNAbilityChangeReason.Reset);
    }
    private void OnEveDied(PNDamageResult result)
    {
        if (deathHandled) return;
        deathHandled = true;
        CancelMoveset(PNCastEnd.Died); EndBurst(PNAbilityChangeReason.Reset); foreach(var id in new[]{SkillCooldownId,UltimateCooldownId,DashCooldownId,BurstGaugeId,BurstStateId,SlashStacksId,EmpowermentId}) foreach(var ability in ownedAbilities.Find(id)) ability.Reset(); comboStep.SetAmount(0); repeatStrokes.SetAmount(0);
#if UNITY_5_3_OR_NEWER
        energy?.Clear();
#endif
    }
    private void UnloadMoveset()
    {
        CancelMoveset(PNCastEnd.Unloaded); EndBurst(PNAbilityChangeReason.Reset);
        UnbindStateEffects(); blazingAssault = risingMomentum = null; EndRepeatChain(); detachedCast = new EveCastMemory();
        if (ownedAbilities != null) ownedAbilities.Changed -= OnAbilityChanged;
        if (Stats != null) Stats.Died -= OnEveDied;
        ownedAbilities = null; movesetRegistry = null; loadedStats = null; lastCdr = double.NaN;
        skillRecovery = ultimateRecovery = null; gauge = stacks = null; burst = empowerment = repeatExhausted = repeatChain = null; comboStep = repeatStrokes = ultimateStrokes = null;
        ResetIdle(); deathHandled = false;
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
