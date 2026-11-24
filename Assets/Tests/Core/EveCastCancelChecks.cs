#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Docs/SkillNetworkFrameworkPlan.md G3a, Edit Mode: one cancel path and one end hook. Every Eve cast (Attack, empowered Attack, Skill, Ultimate, finishing Ultimate, Repeat, Dash, Burst) is stopped at EVERY frame of
// its life, through the interrupt and through the skill's cancel call: afterwards nothing of the cast is left (no lock, no motion, no sweep, no repeat chain, no running execution, the body moves again),
// `OnCastEnded` ran exactly once per activation, and the next cast starts. Casts that run to the end are held to the same count.
public sealed class EveCancelProbe : EveCharacterSkill
{
    private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    public int Ended, EndedCancelled; public string LastReason;
    public void Tick(float delta) => AdvanceAttack(delta);
    public void Prepare() { if (pc == null) Awake(); OnEnable(); }
    public bool Slot(EveId slot, PNSkillInputPhase phase, PNSkillInteraction interaction) => RequestSlot(slot, new PNSkillInputEvent(phase, interaction));
    protected override void OnCastEnded(PNSkillInvocation invocation, bool cancelled, string reason)
    {
        Ended++; if (cancelled) EndedCancelled++; LastReason = reason;
        if (!ReferenceEquals(invocation, CurrentInvocation)) throw new Exception("The end hook must run while the invocation is still the current one.");
        base.OnCastEnded(invocation, cancelled, reason);
    }
    private T Field<T>(string name) => (T)typeof(EveCharacterSkill).GetField(name, Private).GetValue(this);
    public bool HasLock => Field<PNSkillOperation>("sequenceLock") != null;
    public bool HasMotion => Field<PNMotionAction>("motion") != null;
    public bool RepeatChain => IsRepeatChainActive;
}

public static class EveCastCancelChecks
{
    private const float Dt = 1f / 60f;
    private static readonly EveId Attack = PNSkillSlots.Attack, SkillSlot = PNSkillSlots.Skill, Ult = PNSkillSlots.Ultimate, DashSlot = PNSkillSlots.Dash, Burst = PNSkillSlots.Burst;

    private sealed class Ctx
    {
        public EveCancelProbe Skill; public EveCharacterStat Caster; public PNAbilityManager Abilities; public PNBuffManager CasterBuffs; public DummyStat Near;
        public EveId Held; public int HeldSince, Frame;
        public void Tap(EveId slot) { Skill.Slot(slot, PNSkillInputPhase.Started, PNSkillInteraction.Tap); Skill.Slot(slot, PNSkillInputPhase.Performed, PNSkillInteraction.Tap); Skill.Slot(slot, PNSkillInputPhase.Canceled, PNSkillInteraction.Tap); }
        public bool TapAccepted(EveId slot) { bool a = Skill.Slot(slot, PNSkillInputPhase.Started, PNSkillInteraction.Tap); Skill.Slot(slot, PNSkillInputPhase.Performed, PNSkillInteraction.Tap); Skill.Slot(slot, PNSkillInputPhase.Canceled, PNSkillInteraction.Tap); return a; }
        public void Hold(EveId slot) { Skill.Slot(slot, PNSkillInputPhase.Started, PNSkillInteraction.Repeat); Held = slot; HeldSince = Frame; }
        public void Release() { if (Held.IsValid) Skill.Slot(Held, PNSkillInputPhase.Canceled, PNSkillInteraction.Repeat); Held = default; }
        public void Step()
        {
            if (Held.IsValid && (Frame - HeldSince) % 6 == 0) Skill.Slot(Held, PNSkillInputPhase.Performed, PNSkillInteraction.Repeat);
            Abilities.Advance(Dt); CasterBuffs.Advance(Dt); Near.GetComponent<PNBuffManager>()?.Advance(Dt);
            Skill.Tick(Dt); Physics.SyncTransforms(); Frame++;
        }
        public void Run(int frames) { for (int i = 0; i < frames; i++) Step(); }
        public void RunUntilIdle(int max) { for (int i = 0; i < max && Skill.IsBusy; i++) Step(); }
    }

    private static readonly (string Name, int Frames, Action<Ctx> Setup, Action<Ctx> Start)[] Casts =
    {
        ("attack", 70, null, c => c.Tap(Attack)),
        ("empowered_attack", 110, null, c => { c.Tap(SkillSlot); c.RunUntilIdle(120); c.Tap(Attack); }),
        ("skill", 70, null, c => c.Tap(SkillSlot)),
        ("ultimate", 340, c => c.Skill.UltimateRecovery.ResetToFull(), c => c.Tap(Ult)),
        ("ultimate_finisher", 430, c => { c.Skill.BurstGauge.Add(150); c.Tap(Burst); c.Run(5); c.Skill.UltimateRecovery.ResetToFull(); }, c => c.Tap(Ult)),
        ("repeat", 160, c => { c.Skill.BurstGauge.Add(150); c.Tap(Burst); c.Run(5); }, c => c.Hold(Attack)),
        ("dash", 45, null, c => c.Tap(DashSlot)),
        ("burst", 12, c => c.Skill.BurstGauge.Add(150), c => c.Tap(Burst)),
    };

    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Cast cancel check " + n + ": " + what); n++; }
        var errors = new List<string>();
        void Log(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            // The homing projectile logs this in Edit Mode on dispose (known noise, see the golden trace note).
            if (message.Contains("Destroy may not be called from edit mode") || message.Contains("Destroying an object in edit mode")) return;
            errors.Add(message);
        }
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var preview = EditorSceneManager.NewPreviewScene();
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        long id = 987400; int runs = 0;
        T Entity<T>(string name, Vector3 position, string character, out EveEntityLoader loader) where T : PNStat
        {
            var root = new GameObject(name); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview); root.transform.position = position;
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(++id), EveId.Parse(character), 1);
            var stat = root.AddComponent<T>().Declared(); stat.gameObject.layer = stat is PNEnemyStat ? PNLayers.Enemy : stat is PNPlayerStat ? PNLayers.Player : stat.gameObject.layer;
            if (character == "project_n:eve")
            {
                root.AddComponent<Rigidbody>().useGravity = false;
                var capsule = root.AddComponent<CapsuleCollider>(); capsule.radius = .12f; capsule.height = .6f; capsule.center = Vector3.up * .3f;
                root.AddComponent<PNPlayerMove>(); root.AddComponent<PNAbilityManager>();
                root.AddComponent<EveCancelProbe>().Prepare();
            }
            else { var sphere = root.AddComponent<SphereCollider>(); sphere.radius = .08f; }
            loader = root.AddComponent<EveEntityLoader>(); loader.Load(hub); root.SetActive(true); return stat;
        }
        EveRegistrySession.Begin();
        EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:eve_cancel"));
        var areaRoot = new GameObject("Eve cancel area fixture"); SceneManager.MoveGameObjectToScene(areaRoot, preview);
        var areaManager = areaRoot.AddComponent<PNAreaManager>(); areaManager.Initialize(EveRegistrySession.Context);
        var projectileManager = areaRoot.AddComponent<PNProjectileManager>(); projectileManager.Initialize(EveRegistrySession.Context, new PNProjectileProfile { MaxActive = 20 }); projectileManager.ManualAdvance = true;
        Application.logMessageReceived += Log;
        try
        {
            foreach (var cast in Casts)
            {
                var caster = Entity<EveCharacterStat>("Eve", Vector3.zero, "project_n:eve", out var casterLoader);
                var near = Entity<DummyStat>("Near", new Vector3(.45f, .4f, 0), "project_n:dummy", out var nearLoader);
                caster.SetSource(EveId.Parse("test:crit_off"), new[] { new EveStatModifier(PNContentIds.CritRate, -20d, 0) });
                var ctx = new Ctx { Skill = caster.GetComponent<EveCancelProbe>(), Caster = caster, Abilities = caster.GetComponent<PNAbilityManager>(), CasterBuffs = caster.GetComponent<PNBuffManager>(), Near = near };
                ctx.CasterBuffs.Advance(0); near.GetComponent<PNBuffManager>()?.Advance(0); Physics.SyncTransforms();
                var skill = ctx.Skill; var move = caster.GetComponent<PNPlayerMove>();
                void Settle()
                {
                    ctx.Release(); skill.TryInterrupt(EveId.Parse("test:settle"));
                    if (skill.BurstState != null && skill.BurstState.IsActive) skill.BurstState.Reset();
                    ctx.Run(4); skill.TryInterrupt(EveId.Parse("test:settle"));
                    foreach (var ability in ctx.Abilities.Abilities.Instances) if (ability.Id != EveCharacterSkill.BurstGaugeId) ability.Reset();
                    ctx.Run(2);
                }
                PNSkillInvocation watched = null;
                void Clean(string when)
                {
                    Check(!skill.IsBusy && !skill.IsDashing && !skill.IsAttacking && skill.CurrentInvocation == null && skill.CurrentCast == EveCast.None && skill.CurrentPhase == EveCastPhase.None, cast.Name + " " + when + ": the state machine is idle");
                    // (Independent auxiliary Actions of OnHit effects outlive their cast by design, so the execution count is not asserted; the invocation itself must be over.)
                    Check(watched == null || watched.IsEnded, cast.Name + " " + when + ": the invocation is over");
                    Check(!skill.HasLock && !skill.HasMotion && !skill.RepeatChain && skill.RisingMomentum == null && skill.CastTarget == null, cast.Name + " " + when + ": no lock, motion, repeat chain or target is left");
                    Check(move.CanMove, cast.Name + " " + when + ": the body moves again");
                    Check(skill.Ended == skill.ActivationSerial, cast.Name + " " + when + ": the end hook ran once per activation (" + skill.Ended + " of " + skill.ActivationSerial + ")");
                    Check(errors.Count == 0, cast.Name + " " + when + ": errors logged: " + string.Join(" | ", errors));
                }
                try
                {
                    // 1. Run to the end by itself: the same hook count.
                    cast.Setup?.Invoke(ctx); cast.Start(ctx); ctx.Run(cast.Frames); ctx.Release(); ctx.RunUntilIdle(200);
                    watched = null; Settle(); Clean("after finishing");
                    // 2. Stop it at every frame, both ways.
                    for (int mode = 0; mode < 3; mode++)
                        for (int frame = 0; frame <= cast.Frames; frame++)
                        {
                            cast.Setup?.Invoke(ctx); cast.Start(ctx); ctx.Run(frame);
                            bool running = skill.IsBusy || skill.IsDashing; watched = skill.CurrentInvocation; int endedBefore = skill.Ended, cancelledBefore = skill.EndedCancelled;
                            if (mode == 0) skill.TryInterrupt(EveId.Parse("test:stun")); else if (mode == 1) skill.CancelCast(PNCastEnd.Rejected); else skill.CancelForGrace();
                            ctx.Release();
                            Clean("cancelled at frame " + frame + (mode == 0 ? " by interrupt" : mode == 1 ? " by CancelCast" : " by grace"));
                            if (running) Check(skill.Ended == endedBefore + 1 && skill.EndedCancelled == cancelledBefore + 1, cast.Name + " frame " + frame + ": a cancelled cast ends once, as cancelled");
                            if (running && mode == 2) Check(skill.LastReason != null && skill.LastReason.Contains("disconnected"), cast.Name + " frame " + frame + ": a grace cancel names its reason (" + skill.LastReason + ")");
                            Settle(); runs++;
                            Check(ctx.TapAccepted(Attack) && skill.IsBusy, cast.Name + " frame " + frame + ": the next cast starts");
                            watched = skill.CurrentInvocation; skill.TryInterrupt(EveId.Parse("test:settle")); Clean("next cast after frame " + frame);
                            Settle();
                        }
                    // 3. G9b: the owner dies mid-cast: the framework ends the cast once, with the reason `died`, whichever death handler runs first.
                    Settle(); cast.Setup?.Invoke(ctx); cast.Start(ctx); ctx.Run(Math.Min(10, cast.Frames));
                    bool diedRunning = skill.IsBusy || skill.IsDashing; watched = skill.CurrentInvocation; int endedBeforeDeath = skill.Ended;
                    caster.SetCurrent(PNContentModule.Health, 1f); var killed = near.SendDamage(caster, 1000000f, PNContentModule.True); ctx.Release();
                    Check(caster.IsDead && killed.Killed, cast.Name + ": the caster died (killed " + killed.Killed + ")");
                    Check(!skill.IsBusy && !skill.IsDashing && skill.CurrentInvocation == null && (watched == null || watched.IsEnded), cast.Name + ": nothing runs after the owner died");
                    if (diedRunning) Check(skill.Ended == endedBeforeDeath + 1 && skill.LastReason == PNCastEnd.Died, cast.Name + ": the cast ended once with the reason died (ended " + (skill.Ended - endedBeforeDeath) + ", reason " + skill.LastReason + ")");
                    Check(errors.Count == 0, cast.Name + " after death: errors logged: " + string.Join(" | ", errors));
                }
                finally
                {
                    try { skill.TryInterrupt(EveId.Parse("test:end")); } catch (Exception) { }
                    foreach (var loader in new[] { casterLoader, nearLoader }) try { loader.Unload(); } catch (Exception) { }
                    foreach (var o in new[] { caster.gameObject, near.gameObject }) o.SetActive(false);
                }
            }
        }
        finally
        {
            Application.logMessageReceived -= Log;
            try { projectileManager.Shutdown(); areaManager.Shutdown(); EveRegistrySession.End(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        Check(original.isDirty == dirty, "the active scene stayed as it was");
        return "PASS " + n + " cast cancel checks over " + runs + " cancelled casts (" + Casts.Length + " Eve casts stopped at every frame, by interrupt, by CancelCast and by grace, and by the owner's death; one end hook per activation; nothing left running)";
    }
}
#endif
