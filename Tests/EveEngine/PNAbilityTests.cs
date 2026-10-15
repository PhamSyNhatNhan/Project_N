using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;
using static EveSessionSmokeTests;

public static class PNAbilityTests
{
    private static readonly EveId ChargeId = EveId.Parse("test:charge");
    private static readonly EveId CounterId = EveId.Parse("test:counter");
    private static readonly EveId StateId = EveId.Parse("test:state");
    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.00000001;
    private static PNAbilityDefinition Charge(PNAbilityRecovery mode = PNAbilityRecovery.PerStack, double seconds = 5,
        int maximum = 3, int initial = 3, bool automatic = true, PNAbilityClock clock = PNAbilityClock.Game)
        => new PNAbilitySettings { Id = ChargeId.ToString(), Kind = PNAbilityKind.Cooldown, Maximum = maximum,
            InitialAmount = initial, Cooldown = seconds, Recovery = mode, AutoRecover = automatic, Clock = clock }.Create();
    private static PNAbilityDefinition Counter(PNAbilityLifetime mode = PNAbilityLifetime.Shared, double? duration = 10,
        int maximum = 5, int initial = 0, PNAbilityClock clock = PNAbilityClock.Game)
        => new PNAbilitySettings { Id = CounterId.ToString(), Kind = PNAbilityKind.Counter, Maximum = maximum,
            InitialAmount = initial, HasDuration = duration.HasValue, Duration = duration ?? 10, Lifetime = mode, Clock = clock }.Create();
    private static PNAbilityDefinition State(PNAbilityReapply reapply = PNAbilityReapply.Keep,
        PNAbilityCooldownStart start = PNAbilityCooldownStart.OnActivate, double cooldown = 0, double? duration = 5,
        bool active = false, PNAbilityClock clock = PNAbilityClock.Game)
        => new PNAbilitySettings { Id = StateId.ToString(), Kind = PNAbilityKind.State, HasDuration = duration.HasValue,
            Duration = duration ?? 10, Cooldown = cooldown, CooldownStart = start, InitiallyActive = active,
            Reapply = reapply, Clock = clock }.Create();

    public static void Run()
    {
        RunCooldown(); RunCounter(); RunState(); RunCollection(); RunConfiguration(); RunManager(); PNUIDataTests.Run();
    }
    private static void RunCooldown()
    {
        using (var set = new PNAbilityCollection(new[] { Charge() }))
        {
            var ability = set.GetCooldown(ChargeId);
            Check(ability.Current == 3 && !ability.IsRecovering, "ability cooldown starts from configured full amount");
            ability.TryConsume(); set.Advance(2); ability.TryConsume();
            Check(ability.Current == 1 && ability.CooldownRemaining == 3, "using another charge preserves active recovery progress");
            ability.TryConsume(); set.Advance(10.5);
            Check(ability.Current == 2 && Near(ability.CooldownRemaining, 2.5), "large tick recovers several charges and retains surplus");
            set.Advance(2.5);
            Check(ability.Current == 3 && !ability.IsRecovering, "last recovery stops timer exactly at full");
            ability.TryConsume(); set.Advance(2); ability.AddCharges(1);
            Check(ability.Current == 3 && ability.CooldownRemaining == 0, "external charge restoration stops a full recovery");
            ability.TryConsume(); Check(ability.CooldownRemaining == 5, "new depletion starts a fresh timer after external full restore");
            ability.SetCooldown(2); set.Advance(4);
            Check(ability.Current == 2 && ability.CooldownRemaining == 1, "changing cooldown does not rewrite running cycle");
            ability.TryConsume(); set.Advance(1.5);
            Check(ability.Current == 2 && Near(ability.CooldownRemaining, 1.5), "new period applies next cycle with overshoot");
            ability.SetMaximum(2);
            Check(ability.Current == 2 && !ability.IsRecovering, "reducing capacity clamps charges and stops full recovery");
            int resets = 0; set.Changed += c => { if (c.Reason == PNAbilityChangeReason.Reset) resets++; };
            ability.ResetToFull(); Check(resets == 1, "reset to full publishes a state notification");
            Check(!ability.TryConsume(2), "cooldown consumption always costs one charge");
            Reject(() => ability.SetMaximum(0), "cooldown rejects zero capacity");
        }
        using (var set = new PNAbilityCollection(new[] { Charge(PNAbilityRecovery.AllAtOnce) }))
        {
            var ability = set.GetCooldown(ChargeId); ability.TryConsume(); set.Advance(100);
            Check(ability.Current == 2 && !ability.IsRecovering, "all-at-once does not recover partial depletion");
            ability.TryConsume(); ability.TryConsume(); set.Advance(2); ability.AddCharges(1);
            Check(ability.Current == 1 && ability.CooldownRemaining == 3, "partial external grant keeps all-at-once countdown");
            set.Advance(3); Check(ability.Current == 3 && !ability.IsRecovering, "all-at-once restores full capacity after period");
        }
        using (var set = new PNAbilityCollection(new[] { Charge(seconds: 0) }))
        {
            PNAbilityChange used = null; set.Changed += c => used = c;
            var ability = set.GetCooldown(ChargeId); ability.TryConsume();
            Check(ability.Current == 3 && !ability.IsRecovering, "zero cooldown restores synchronously without a frame delay");
            Check(used.Reason == PNAbilityChangeReason.Used && used.Quantity == 1, "instant recovery still reports one consumed charge");
        }
        using (var set = new PNAbilityCollection(new[] { Charge(initial: 0, automatic: false) }))
        {
            var ability = set.GetCooldown(ChargeId); set.Advance(100);
            Check(ability.Current == 0 && !ability.IsRecovering, "disabled auto-recovery leaves quantity controlled by gameplay");
            ability.AddCharges(3); ability.TryConsume(); ability.SetAutoRecovery(true);
            Check(ability.CooldownRemaining == 5, "enabling recovery starts eligible missing charge timer");
            ability.SetReduction(1, 50); ability.RestartRecovery();
            Check(Near(ability.CooldownRemaining, 1.5), "cooldown reduction uses percent then flat and explicit restart");
            Reject(() => ability.SetReduction(-1, 0), "negative flat reduction is rejected");
            ability.SetReduction(0, -20);
            Check(ability.ActualCooldown == 6 && Near(ability.CooldownRemaining, 1.5), "negative CDR lengthens next period without restarting recovery");
            ability.SetReduction(0, 150);
            Check(ability.ActualCooldown == 0 && Near(ability.CooldownRemaining, 1.5), "CDR above 100 clamps next period to zero");
            Reject(() => ability.SetReduction(0, double.NaN), "nonfinite percent reduction is rejected");
            set.Advance(1.5);
            Check(ability.Current == 3 && !ability.IsRecovering, "zero next period fills remaining charges after current period finishes");
        }
        using (var set = new PNAbilityCollection(new[] { Charge(PNAbilityRecovery.AllAtOnce, seconds: 10) }))
        {
            var ability = set.GetCooldown(ChargeId); ability.SetReduction(0, 20);
            ability.TryConsume(); ability.TryConsume(); ability.TryConsume();
            Check(ability.CooldownRemaining == 8, "all-at-once recovery applies CDR when empty");
            set.Advance(3); ability.SetReduction(0, 50);
            Check(ability.CooldownRemaining == 5, "all-at-once CDR changes preserve the active countdown");
            set.Advance(5);
            Check(ability.Current == 3, "all-at-once recovers full amount at original adjusted deadline");
        }
        using (var set = new PNAbilityCollection(new[] { State(cooldown: 10, duration: 5) }))
        {
            var ability = set.GetStates(StateId).Single(); ability.SetReduction(0, 20); ability.TryActivate();
            Check(ability.CooldownRemaining == 8 && ability.DurationRemaining == 5, "state OnActivate CDR does not shorten duration");
            set.Advance(2); ability.SetReduction(0, -20);
            Check(ability.CooldownRemaining == 6 && ability.ActualCooldown == 12, "state CDR changes preserve current cooldown");
            set.Advance(6); ability.TryActivate();
            Check(ability.CooldownRemaining == 12, "state next activation uses updated CDR");
            ability.Reset();
            Check(ability.ActualCooldown == 10, "state reset clears runtime reductions");
        }
        using (var set = new PNAbilityCollection(new[] { State(start: PNAbilityCooldownStart.OnEnd, cooldown: 10, duration: 5) }))
        {
            var ability = set.GetStates(StateId).Single(); ability.SetReduction(1, 50); ability.TryActivate();
            set.Advance(6);
            Check(!ability.IsActive && ability.CooldownRemaining == 3, "state OnEnd applies percent then flat and preserves expiry surplus");
            ability.SetReduction(0, 100); set.Advance(3); ability.TryActivate(); ability.End();
            Check(ability.CanActivate, "state zero cooldown is immediately ready after end");
        }
    }
    private static void RunCounter()
    {
        using (var set = new PNAbilityCollection(new[] { Counter(maximum: 3) }))
        {
            var ability = set.GetCounter(CounterId); var notices = new List<PNAbilityChange>(); set.Changed += notices.Add;
            Check(ability.Add(int.MaxValue) == 3 && notices.Last().Quantity == 3, "counter caps huge additions and reports actual amount");
            set.Advance(4); Check(ability.Add(2) == 0 && ability.DurationRemaining == 6, "adding at full does not refresh expiry");
            ability.TryConsume(); set.Advance(3); ability.Add(1);
            Check(ability.Current == 3 && ability.DurationRemaining == 3, "shared addition after consumption preserves original expiry");
            ability.TryConsume(); set.Advance(3);
            Check(ability.Current == 0 && ability.Grants.Count == 0, "shared timer removes unconsumed stacks");
            Check(notices.Last().Reason == PNAbilityChangeReason.Expired && notices.Last().Quantity == 2,
                "counter expiry is distinct from usage and reports remainder");
            Check(notices.Count(n => n.Reason == PNAbilityChangeReason.Used) == 2, "expiration never raises a consumption notice");
            ability.Add(3); ability.TryConsume(3); set.Advance(100);
            Check(ability.DurationRemaining == null && notices.Last().Reason == PNAbilityChangeReason.Used,
                "using all stacks removes timer without a later expiry");
            Reject(() => ability.Add(0), "counter rejects nonpositive grants");
            Check(!ability.TryConsume(0), "counter cannot consume a nonpositive cost");
        }
        using (var set = new PNAbilityCollection(new[] { Counter(PNAbilityLifetime.PerGrant) }))
        {
            var ability = set.GetCounter(CounterId); ability.Add(3); set.Advance(5); ability.Add(2);
            Check(ability.Grants.Count == 2 && ability.Grants[0].Remaining == 5 && ability.Grants[1].Remaining == 10,
                "per-grant groups retain independent durations");
            ability.TryConsume(2); Check(ability.Grants[0].Amount == 1 && ability.Grants[1].Amount == 2,
                "counter consumes earliest expiry first");
            set.Advance(5); Check(ability.Current == 2 && ability.DurationRemaining == 5, "only earliest remaining grant expires");
            set.Advance(5); Check(ability.Current == 0, "later grant expires at its own deadline");
            ability.Add(2, 20); ability.Add(2, 3); ability.TryConsume();
            Check(ability.Grants[0].Amount == 2 && ability.Grants[1].Amount == 1, "expiry priority overrides chronological grant order");
            long oldGrant = ability.Grants[0].Id; ability.Reset(); ability.Add(1);
            Check(ability.Grants[0].Id > oldGrant, "counter grant IDs are not reused across reset");
            ability.RefreshDuration(7, ability.Grants[0].Id); Check(ability.DurationRemaining == 7, "explicit per-grant refresh updates deadline");
            Reject(() => ability.RefreshDuration(5, oldGrant), "expired grant IDs cannot refresh new groups");
        }
        using (var set = new PNAbilityCollection(new[] { Counter(duration: null, initial: 2) }))
        {
            var ability = set.GetCounter(CounterId); set.Advance(1000);
            Check(ability.Current == 2 && ability.DurationRemaining == null, "permanent counters remain until consumed or explicitly cleared");
            ability.SetAmount(5); ability.SetMaximum(3);
            Check(ability.Current == 3, "counter capacity changes clamp stored quantity");
            ability.Clear(); ability.Reset(); Check(ability.Current == 2, "counter reset restores configured initial amount");
        }
    }
    private static void RunState()
    {
        using (var set = new PNAbilityCollection(new[] { State(cooldown: 3) }))
        {
            var ability = set.GrantState(StateId); set.Advance(3);
            Check(ability.IsActive && !ability.CanActivate && ability.CooldownRemaining == 0 && ability.DurationRemaining == 2,
                "completed cooldown cannot reactivate a still-active state");
            set.Advance(2); Check(!ability.IsActive && ability.CanActivate, "duration end unlocks recovered state");
        }
        using (var set = new PNAbilityCollection(new[] { State(start: PNAbilityCooldownStart.OnEnd, cooldown: 10) }))
        {
            var ability = set.GrantState(StateId); set.Advance(5.3);
            Check(!ability.IsActive && Near(ability.CooldownRemaining, 9.7), "duration-to-cooldown transition retains same-tick overshoot");
            set.Advance(9.7); Check(ability.CanActivate, "post-duration cooldown reaches exact completion");
            ability.TryActivate(); set.Advance(2); ability.End();
            Check(ability.CooldownRemaining == 10, "early explicit end starts full on-end cooldown");
            ability.SetCooldown(2); set.Advance(1);
            Check(ability.CooldownRemaining == 9, "state cooldown parameter changes preserve active cycle");
            ability.RestartCooldown(); Check(ability.CooldownRemaining == 2, "state cooldown can be restarted explicitly");
        }
        foreach (var mode in new[] { PNAbilityReapply.Keep, PNAbilityReapply.Refresh, PNAbilityReapply.Extend })
        using (var set = new PNAbilityCollection(new[] { State(mode) }))
        {
            var ability = set.GrantState(StateId); set.Advance(2); var again = set.GrantState(StateId);
            double expected = mode == PNAbilityReapply.Keep ? 3 : mode == PNAbilityReapply.Refresh ? 5 : 8;
            Check(ReferenceEquals(ability, again) && ability.DurationRemaining == expected, "state reapply policy: " + mode);
        }
        using (var set = new PNAbilityCollection(new[] { State(PNAbilityReapply.Independent) }))
        {
            var first = set.GrantState(StateId); set.Advance(2); var second = set.GrantState(StateId);
            Check(first.Handle != second.Handle && set.GetStates(StateId).Count == 2, "independent states share ID and have unique handles");
            set.Advance(3);
            Check(!first.IsActive && second.IsActive && second.DurationRemaining == 2, "independent state durations expire separately");
            set.Advance(2);
            Check(!set.TryGet(second.Handle, out _) && !second.IsAttached, "finished transient state retires its handle");
            Reject(() => second.TryActivate(), "retired state reference cannot reactivate");
            set.GrantState(StateId); Check(first.IsActive, "named state remains reusable after independent extras expire");
        }
        using (var set = new PNAbilityCollection(new[] { State(active: true) }))
        {
            var ability = set.GetStates(StateId)[0]; set.Advance(.1);
            Check(ability.IsActive && Near(ability.DurationRemaining.Value, 4.9), "initially active states initialize duration correctly");
            Check(ability.TryConsume() && !ability.IsActive, "consuming a flag ends it without performing gameplay effects");
            Check(!ability.TryConsume(), "inactive states cannot be consumed successfully");
            ability.TryActivate(2); set.Advance(2); Check(!ability.IsActive, "override duration controls actual expiry");
        }
        using (var set = new PNAbilityCollection(new[] { State(duration: null) }))
        {
            var ability = set.GrantState(StateId); set.Advance(999);
            Check(ability.IsActive && ability.DurationRemaining == null, "untimed state stays active until explicitly ended");
            ability.RefreshDuration(1); set.Advance(1); Check(!ability.IsActive, "explicit refresh can set a permanent state deadline");
        }
    }
    private static void RunCollection()
    {
        using (var set = new PNAbilityCollection())
        {
            PNAbilityChange created = null; set.Changed += c => created = c;
            var counter = (PNAbilityCounter)set.Define(Counter(initial: 3));
            Check(created.Reason == PNAbilityChangeReason.Created && created.Before == null && created.After.Amount == 3 &&
                set.TryGet(created.After.Handle, out var found) && ReferenceEquals(found, counter),
                "creation notifies after a configured instance is attached and queryable");
            set.Define(State(PNAbilityReapply.Independent)); set.GrantState(StateId);
            bool activeBeforeCreatedObserver = false;
            set.Changed += c => { if (c.Reason == PNAbilityChangeReason.Created) activeBeforeCreatedObserver = ((PNAbilityState)set.Get(c.After.Handle)).IsActive; };
            set.GrantState(StateId);
            Check(activeBeforeCreatedObserver, "independent grant attaches and activates before dispatching creation observers");
        }
        using (var set = new PNAbilityCollection(new[] { Charge(), Counter(), State() }))
        {
            var charge = set.GetCooldown(ChargeId); var counter = set.GetCounter(CounterId);
            counter.Add(2); var costs = new[] { new PNAbilityCost(charge.Handle), new PNAbilityCost(counter.Handle, 3) };
            Check(!set.TryConsume(costs) && charge.Current == 3 && counter.Current == 2, "multi-ability cost failure consumes nothing");
            costs[1] = new PNAbilityCost(counter.Handle, 2);
            bool committedBeforeObservers = true; int used = 0;
            set.Changed += c => { if (c.Reason == PNAbilityChangeReason.Used) { used++; committedBeforeObservers &= charge.Current == 2 && counter.Current == 0; } };
            Check(set.TryConsume(costs) && used == 2 && committedBeforeObservers, "group consumption commits all quantities before notifications");
            Reject(() => set.TryConsume(new[] { costs[0], costs[0] }), "duplicate cost handles cannot consume twice through precheck");
            Reject(() => set.GetCounter(ChargeId), "typed access rejects mismatched ability kinds");
            Reject(() => set.Find(EveId.Parse("test:typo")), "missing ID is an error rather than implicitly ready");
            var state = set.GrantState(StateId); var handle = state.Handle; set.Remove(handle);
            var recreated = (PNAbilityState)set.CreateInstance(StateId);
            Check(handle != recreated.Handle && !set.TryGet(handle, out _), "removed handles cannot address replacement instances");
            using (var other = new PNAbilityCollection(new[] { Charge() }))
                Check(!other.TryGet(charge.Handle, out _), "ability handles are entity-collection scoped");
            var snapshot = charge.Snapshot(); charge.ResetToFull();
            Check(snapshot.Amount == 2, "change/read snapshots remain immutable after mutation");
            int failures = 0; set.ObserverFailed += error => failures++;
            set.Changed += c => throw new Exception("test observer");
            charge.TryConsume(); Check(failures == 1 && charge.Current == 2, "observer failures do not roll back committed state");
            set.Changed += c => set.Advance(1);
            charge.AddCharges(1); Check(failures == 3, "recursive clock advancement from observers is rejected and isolated");
            var old = charge.Handle; set.Dispose();
            Check(!charge.IsAttached && !set.TryGet(old, out _), "dispose invalidates existing handles and runtime objects");
            Reject(() => charge.TryConsume(), "unloaded direct references cannot modify entity state");
        }
        foreach (var mode in new[] { PNAbilityClock.Game, PNAbilityClock.Entity, PNAbilityClock.EntityWithoutMinimum })
        using (var set = new PNAbilityCollection(new[] { Counter(initial: 3, clock: mode) }))
        {
            var counter = set.GetCounter(CounterId); set.Advance(2, 1, 0);
            Check(counter.DurationRemaining == (mode == PNAbilityClock.EntityWithoutMinimum ? 10 : 8),
                "player minimum one versus raw time zero: " + mode);
            set.Advance(2, 0, -1);
            Check(counter.DurationRemaining == (mode == PNAbilityClock.Game ? 6 : mode == PNAbilityClock.Entity ? 8 : 10),
                "zero/negative raw time never runs clocks backward: " + mode);
            double before = counter.DurationRemaining.Value;
            Reject(() => set.Advance(double.PositiveInfinity), "invalid elapsed time rejected before clock mutation");
            Reject(() => set.Advance(1, double.NaN), "invalid scale rejected before clock mutation");
            Check(counter.DurationRemaining == before, "invalid clock inputs leave ability state unchanged");
        }
    }
    private static void RunConfiguration()
    {
        var defs = PNAbilityDefinition.Parse("[{\"id\":\"test:c\",\"showInUI\":true,\"kind\":\"Counter\",\"maximum\":3,\"duration\":10,\"lifetime\":\"PerGrant\"}," +
            "{\"id\":\"test:s\",\"showInUI\":true,\"kind\":\"State\",\"reapply\":\"Independent\",\"clock\":\"EntityWithoutMinimum\"}]");
        Check(defs.Count == 2 && defs[0].Duration == 10 && defs[1].Duration == null, "standalone ability JSON requires no skill or action");
        var input = new PNAbilitySettings { Id = "test:c", Kind = PNAbilityKind.Counter, Maximum = 3 };
        var copied = input.Create(); input.Maximum = 99;
        Check(copied.Maximum == 3, "ability runtime configuration isolates mutable authoring inputs");
        Reject(() => PNAbilityDefinition.Parse("[{\"id\":\"test:c\",\"showInUI\":true,\"kind\":\"Counter\",\"cooldown\":3}]"), "counter JSON rejects cooldown field");
        Reject(() => PNAbilityDefinition.Parse("[{\"id\":\"test:c\",\"showInUI\":true,\"kind\":\"Cooldown\",\"duration\":3}]"), "cooldown JSON rejects duration field");
        Reject(() => PNAbilityDefinition.Parse("[{\"id\":\"test:c\",\"showInUI\":true,\"kind\":\"State\",\"maximum\":3}]"), "state JSON rejects quantity fields");
        Reject(() => PNAbilityDefinition.Parse("[{\"id\":\"test:c\",\"showInUI\":true,\"kind\":\"State\",\"duration\":0}]"), "zero duration is not interpreted as permanent");
        Reject(() => PNAbilityDefinition.Parse("[{\"id\":\"test:c\",\"showInUI\":true,\"kind\":\"State\",\"duration\":null}]"), "explicit null duration is rejected");
        Reject(() => PNAbilityDefinition.Parse("[{\"id\":\"test:c\",\"showInUI\":true,\"kind\":\"Counter\",\"maximum\":1.5}]"), "fractional capacity is rejected");
        Reject(() => PNAbilityDefinition.Parse("[{\"id\":\"test:c\",\"showInUI\":true,\"kind\":\"State\",\"clock\":\"2\"}]"), "numeric enum strings are rejected");
        Reject(() => PNAbilityDefinition.Parse("[{\"id\":\"test:c\",\"id\":\"test:d\",\"showInUI\":true,\"kind\":\"State\"}]"), "duplicate JSON fields are rejected");
        Reject(() => new PNAbilityCollection(new[] { copied, copied }), "duplicate ability IDs are rejected before load");
        Reject(() => new PNAbilitySettings { Id = "test:c", Kind = PNAbilityKind.Counter, Maximum = 3, InitialAmount = 4 }.Create(),
            "initial quantity must fit capacity");
        Reject(() => new PNAbilitySettings { Id = "test:c", Cooldown = double.NaN }.Create(), "invalid cooldown configuration is rejected");
    }
    private static void RunManager()
    {
        var hub = new EveContentBootstrap().Build(new[] { new PNContentModule() }, Array.Empty<EveContentDocument>());
        var compatibility = EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:abilities");
        EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, compatibility);
        try
        {
            var manager = new PNAbilityManager(); manager.Configure(new[] { Counter(initial: 3), State(clock: PNAbilityClock.EntityWithoutMinimum) });
            manager.gameObject.SetActive(true);
            var clock = new EveTimeScale { MinScale = 1 }; var loader = new EveTestLoader(); var identity = new EveEntityIdentity();
            var skill = new PNSkill();
            // EN: This entity intentionally has no action/skill payload; abilities are independent.
            // VI: Entity cố ý không có payload skill/action; Ability độc lập hoàn toàn.
            MonoBehaviour[] components = { manager, clock, loader, identity };
            foreach (var component in components) component.Components = components;
            skill.Components = components;
            Reject(() => manager.Abilities.Find(CounterId), "manager state cannot be read before load");
            loader.Load(hub); var counter = manager.Abilities.GetCounter(CounterId); var old = counter.Handle;
            Check(ReferenceEquals(skill.Abilities, manager.Abilities), "skill accesses entity-owned collection without owning its lifecycle");
            skill.enabled = false; manager.Advance(2);
            Check(counter.DurationRemaining == 8, "disabling skill does not pause independent ability timers");
            manager.gameObject.SetActive(false); manager.Advance(3); manager.gameObject.SetActive(true);
            Check(counter.DurationRemaining == 8, "disabled entity retains and pauses state instead of clearing it");
            manager.Advance(1); Check(counter.DurationRemaining == 7, "reenabling entity resumes from retained remaining time");
            clock.AddTimedModifier(EveId.Parse("test:zone"), EveTimeModifierKind.Scale, -1, 10);
            var state = manager.Abilities.GrantState(StateId); manager.Advance(2);
            Check(state.DurationRemaining == 5 && counter.DurationRemaining == 5, "manager chooses raw or game clock independently of player minimum");
            manager.Abilities.ResetAll(); Check(counter.Current == 3 && counter.DurationRemaining == 10, "death/reset policy is an explicit collection operation");
            loader.Unload(); Check(!manager.IsReady && !counter.IsAttached, "entity unload disposes passive state");
            loader.Load(hub); Check(!manager.Abilities.TryGet(old, out _), "new load invalidates prior entity ability handles");
            Check(manager.Abilities.GetCounter(CounterId).Current == 3, "new load recreates configured initial quantities");
            EveRegistrySession.End(); Check(!manager.IsReady, "manager does not tick or expose a closed session");
            loader.Unload();
        }
        finally { if (EveRegistrySession.IsReady) EveRegistrySession.End(); }
    }
}
