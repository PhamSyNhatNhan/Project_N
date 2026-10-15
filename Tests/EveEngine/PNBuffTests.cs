using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static EveSessionSmokeTests;

public static class PNBuffTests
{
    private static readonly EveId A = EveId.Parse("test:buff_a"), B = EveId.Parse("test:buff_b"), C = EveId.Parse("test:buff_c"), Multi = EveId.Parse("test:multi"), SourceMulti = EveId.Parse("test:source_multi"), Protected = EveId.Parse("test:protected"), Bad = EveId.Parse("test:bad"), X = EveId.Parse("test:abc");
    private sealed class Probe : PNBuffBehavior
    {
        public readonly string Name;
        public readonly List<string> Log;
        public int Stacks = 1;
        public double Maximum = 3;
        public int Ticks, Applies, Resumes;
        public Action<PNBuffContext> Tick, End, Start;
        public bool ThrowCleanup, Migrate, Lock, ThrowApply;
        public PNBuffClock Clock;
        public Probe(string name, List<string> log) { Name = name; Log = log; }
        public override double? StackCount => Stacks;
        public override bool Supports(Type contract) => contract == typeof(PNBuffStackLimitContract);
        private void Setup(PNBuffContext c) { c.SetClock(Clock); c.SetDuration(3); c.Schedule("tick", 1, 1); if (Lock) c.Lock(new[] { PNControlIds.Move }); }
        public override void OnApply(PNBuffContext c, JToken data) { Applies++; Log.Add(Name + ":apply"); Setup(c); Start?.Invoke(c); if (ThrowApply) throw new Exception("apply fault"); }
        public override bool OnReapply(PNBuffContext c, PNBuffApplyRequest request) { Stacks = Math.Min(Stacks + 1, (int)Maximum); return true; }
        public override void OnScheduled(PNBuffContext c, string key) { Ticks++; Log.Add(Name + ":tick"); Tick?.Invoke(c); }
        public override void OnEnding(PNBuffContext c, PNBuffEndReason reason, PNBuffSnapshot final) { Log.Add(Name + ":end:" + reason); End?.Invoke(c); }
        public override void OnCleanup(PNBuffContext c) { Log.Add(Name + ":cleanup"); if (ThrowCleanup) throw new Exception("cleanup fault"); }
        public override void OnResume(PNBuffContext c) { Resumes++; if (Lock) c.Lock(new[] { PNControlIds.Move }); }
        public override void OnContributionsChanged(PNBuffContext c)
        { Maximum = c.Additive(typeof(PNBuffStackLimitContract), "maximum", 3); Stacks = Math.Min(Stacks, (int)Maximum); }
        public override bool TryMigrateFrom(PNBuffBehavior previous, PNBuffContext context)
        { if (!Migrate || !(previous is Probe probe)) return false; Stacks = probe.Stacks; return true; }
        public override void OnBehaviorStarted(PNBuffContext c, bool migrated) { if (!migrated) Setup(c); else OnResume(c); }
    }
    private sealed class Empty : PNBuffBehavior { }
    private sealed class CapturedFactor : PNCombatSourceSnapshot
    {
        private readonly double factor;
        public CapturedFactor(PNCombatSourceSnapshot original, double factor) : base(original) { this.factor = factor; }
        public override PNDamage CreateDamage(double amount, bool canCrit, double? iframeDuration, params EveId[] tags)
        {
            var packet = base.CreateDamage(amount, canCrit, iframeDuration, tags);
            return new PNDamage(packet.SessionId, packet.SourceId, packet.Amount * factor, packet.Tags, packet.IFrameDuration, packet.CanCrit, packet.IsCritical, packet.CritRate, packet.CritDamage);
        }
    }
    public static void Run()
    {
        EveHotPathDiagnosticTests.Run();
        PNPolicyFixturePlanTests.Run();
        Content(); Runtime(); Integration(); Dot(); Elemental(); PNBuffPresentationTests.Run(); PNBuffCountdownTests.Run(); PNBuffReplicationTests.Run(); PNSkillEffectsTests.Run(); PNTimeScaleContractTests.Run(); PNBuffMembershipTests.Run(); PNBuffR234Tests.Run(); PNBuffD5Tests.Run();
    }
    private static void Runtime()
    {
        var hub = new EveRegistryHub(); new EveBuiltInContentModule().Register(hub); new PNContentModule().Register(hub);
        var log = new List<string>(); var probes = new List<Probe>();
        Probe Make(string name) { var probe = new Probe(name, log); probes.Add(probe); return probe; }
        foreach (var id in new[] { A, Multi, SourceMulti, Protected }) hub.Buffs().Register(new PNBuffDefinition(id, EveId.Parse("test:pnbufftests_factory_1"), _ => Make(id.Path), null,
            multipleInstances: id == Multi || id == SourceMulti, maximumInstances: id == Multi || id == SourceMulti ? 2 : 1,
            scope: id == SourceMulti ? PNBuffInstanceScope.Source : PNBuffInstanceScope.Target, canDispel: id != Protected));
        hub.Buffs().Register(new PNBuffDefinition(B, EveId.Parse("test:pnbufftests_factory_2"), _ => new Empty(), null, new[] { X }));
        hub.Buffs().Register(new PNBuffDefinition(C, EveId.Parse("test:pnbufftests_factory_3"), _ => { var p = Make("C"); p.Migrate = true; return p; }, null));
        hub.Buffs().Register(new PNBuffDefinition(Bad, EveId.Parse("test:pnbufftests_factory_4"), _ => { var p = Make("bad"); p.ThrowApply = true; p.ThrowCleanup = true; p.Lock = true; return p; }, null));
        hub.BuffTags().Register(new PNBuffTagDefinition(X)); hub.Buffs().RegisterBase(EveId.Parse("base:buff_a"), A, "test"); hub.Seal();
        var session = Guid.NewGuid(); var source = new PNBuffSource(session, new EveEntityId(1)); var source2 = new PNBuffSource(session, new EveEntityId(2));
        var coordinator = new PNBuffMutationCoordinator(); using var controls = new PNControlState(hub);
        using var buffs = new PNBuffCollection(hub, session, coordinator, new PNBuffServices(controls: controls));
        using var other = new PNBuffCollection(hub, session, coordinator);
        var a = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_a"), source));
        Check(a.Status == PNBuffStatus.Applied && buffs.Get(a.Handle).Id == A && other.Snapshot().Count == 0, "Buff canonical identity and entity isolation");
        Reject(() => buffs.Apply(new PNBuffApplyRequest(A, source, new JObject { ["maximum"] = 99 })), "Caller cannot invent tuning overrides");
        Reject(() => buffs.Apply(new PNBuffApplyRequest(A, new PNBuffSource(Guid.NewGuid(), new EveEntityId(1)))), "Cross-session source rejected");
        var frozen = buffs.Snapshot(); buffs.Apply(new PNBuffApplyRequest(A, source)); Check(frozen[0].Stacks == 1 && buffs.Get(a.Handle).Stacks == 2, "Snapshot is immutable while reapply changes state");
        var number = buffs.Contribute("ownerA", new PNBuffNumberContribution(typeof(PNBuffStackLimitContract), "maximum", 2), A);
        var number2 = buffs.Contribute("ownerB", new PNBuffNumberContribution(typeof(PNBuffStackLimitContract), "maximum", 1), A);
        for (int i = 0; i < 6; i++) buffs.Apply(new PNBuffApplyRequest(A, source));
        Check(buffs.Get(a.Handle).Stacks == 6, "Independent numeric contributions use receiver arithmetic");
        number.Dispose(); number.Dispose(); Check(buffs.Get(a.Handle).Stacks == 4, "Removing one owner discards excess only");
        number2.Dispose(); Check(buffs.Get(a.Handle).Stacks == 3, "Last owner restores registered limit");
        var restored = buffs.Contribute("again", new PNBuffNumberContribution(typeof(PNBuffStackLimitContract), "maximum", 2), A);
        Check(buffs.Get(a.Handle).Stacks == 3, "Discarded stacks never resurrect");
        var low = buffs.Contribute("low", new PNBuffOverrideContribution(B, 1), A);
        Check(a.Handle.IsAlive && buffs.Get(a.Handle).ProfileId == B && buffs.Get(a.Handle).Tags.Contains(X), "Full override preserves identity and changes tags");
        Reject(() => buffs.Contribute("unsupported", new PNBuffNumberContribution(typeof(PNBuffStackLimitContract), "maximum", 1), A), "Initial unsupported contract rejected");
        var high = buffs.Contribute("high", new PNBuffOverrideContribution(C, 2), A);
        Check(buffs.Get(a.Handle).ProfileId == C && buffs.Get(a.Handle).Stacks == 1, "Higher override wins with default reset when migration unsupported");
        var tie = buffs.Contribute("tie", new PNBuffOverrideContribution(B, 2), A);
        Check(buffs.Get(a.Handle).ProfileId == B, "Equal priority chooses newest authority grant");
        tie.Dispose(); Check(buffs.Get(a.Handle).ProfileId == C, "Losing override lease remains eligible");
        high.Dispose(); low.Dispose(); Check(buffs.Get(a.Handle).ProfileId == A && a.Handle.IsAlive, "Removing all overrides restores original behavior");
        Check(probes.Last().Maximum == 5 && !log.Any(x => x.StartsWith("buff_a:end")), "Dormant external contributions restore and transitions do not end instance");
        restored.Dispose();
        var suspendedGrant = buffs.Contribute("suspended limit", new PNBuffNumberContribution(typeof(PNBuffStackLimitContract), "maximum", 2), A);
        for (int i = 0; i < 5; i++) buffs.Apply(new PNBuffApplyRequest(A, source));
        buffs.Suspend(true); suspendedGrant.Dispose(); Check(buffs.Get(a.Handle).Stacks == 3, "Suspended Buff discards excess immediately when limit shrinks");
        var suspendedOverride = buffs.Contribute("suspended profile", new PNBuffOverrideContribution(C, 4), A);
        buffs.Suspend(false); Check(buffs.Get(a.Handle).Remaining == 3 && buffs.Get(a.Handle).ProfileId == C, "Override while suspended initializes replacement on resume"); suspendedOverride.Dispose();
        var migrate = buffs.Contribute("migrate", new PNBuffOverrideContribution(C, 0), A);
        Check(buffs.Get(a.Handle).Stacks == 1 && log.Last(x => x.EndsWith(":cleanup")) == "buff_a:cleanup", "Custom migration transfers state and cleans old behavior"); migrate.Dispose();
        var add = buffs.Contribute("tagA", new PNBuffTagContribution(new[] { X }), A);
        var suppress = buffs.Contribute("tagB", new PNBuffTagContribution(suppress: new[] { X }), A);
        Check(!buffs.Get(a.Handle).Tags.Contains(X), "Tag suppress wins add"); suppress.Dispose(); Check(buffs.Get(a.Handle).Tags.Contains(X), "Removing suppression exposes active add"); add.Dispose();
        using (buffs.Immune(ids: new[] { A }))
        { Check(a.Handle.IsAlive && buffs.Apply(new PNBuffApplyRequest(A, source)).Status == PNBuffStatus.Rejected, "Default immunity retains existing but denies reapply"); }
        using (buffs.Immune(tags: new[] { X }, purge: true))
        {
            var replace = buffs.Contribute("immuneOverride", new PNBuffOverrideContribution(B, 8), A);
            Check(!a.Handle.IsAlive && log.Count(x => x.EndsWith(":end:ImmunityRemoved")) == 1, "Dynamic override tags trigger bypass immunity purge"); replace.Dispose();
        }
        Check(buffs.Snapshot().Count == 0, "Ending immunity does not recreate purged instance");
        var protectedBuff = buffs.Apply(new PNBuffApplyRequest(Protected, source));
        Check(buffs.Remove(protectedBuff.Handle).Status == PNBuffStatus.Rejected && protectedBuff.Handle.IsAlive, "Normal dispel respects CanDispel");
        Check(buffs.Remove(protectedBuff.Handle, PNBuffEndReason.Bypass, true).Status == PNBuffStatus.Removed, "Bypass still performs normal lifecycle");
        var many1 = buffs.Apply(new PNBuffApplyRequest(Multi, source)); var many2 = buffs.Apply(new PNBuffApplyRequest(Multi, source2));
        Check(many1.Handle.IsAlive && many2.Handle.IsAlive && !many1.Handle.Equals(many2.Handle), "Multiple Buff instances independent");
        Check(buffs.Apply(new PNBuffApplyRequest(Multi, source)).Status == PNBuffStatus.Reapplied && buffs.Snapshot().Count == 2, "Full multiple-instance group delegates decision to behavior without violating cap");
        var cap = buffs.Contribute("cap", new PNBuffNumberContribution(typeof(PNBuffInstanceLimitContract), "maximum", 1), Multi);
        var many3 = buffs.Apply(new PNBuffApplyRequest(Multi, source)); Check(many3.Handle.IsAlive, "Group contribution raises instance limit");
        Reject(() => buffs.Contribute("badgroup", new PNBuffNumberContribution(typeof(PNBuffInstanceLimitContract), "maximum", 1), selection: PNBuffSelection.Current, exact: many1.Handle), "Instance override cannot silently mutate group limit");
        cap.Dispose(); Check(many1.Handle.IsAlive && many2.Handle.IsAlive && !many3.Handle.IsAlive, "Limit shrink keeps oldest and ends excess");
        foreach (var item in buffs.Snapshot()) buffs.Remove(item.Handle, PNBuffEndReason.Bypass, true);
        var per1 = buffs.Apply(new PNBuffApplyRequest(SourceMulti, source)); var per2 = buffs.Apply(new PNBuffApplyRequest(SourceMulti, source));
        var per3 = buffs.Apply(new PNBuffApplyRequest(SourceMulti, source2)); Check(per1.Handle.IsAlive && per2.Handle.IsAlive && per3.Handle.IsAlive, "Per-source limits separate entity sources");
        buffs.Dispose(); Check(!per1.Handle.IsAlive && !per2.Handle.IsAlive && !per3.Handle.IsAlive, "Dispose invalidates every handle");
        Timers(hub, session, source, log, probes, coordinator, controls);
        using (var cyclic = new PNBuffCollection(hub, session, services: new PNBuffServices(controls: controls)))
        {
            var handle = cyclic.Apply(new PNBuffApplyRequest(A, source)).Handle;
            cyclic.Changed += () => cyclic.Apply(new PNBuffApplyRequest(A, source));
            cyclic.Apply(new PNBuffApplyRequest(A, source));
            Check(cyclic.Coordinator.IsFaulted && cyclic.IsDisposed && !handle.IsAlive && controls.IsAllowed(PNControlIds.Move), "Mutation cycle fails and retires collection without stranding ownership");
        }
        using var replica = new PNBuffCollection(hub, session, canMutate: () => false);
        Reject(() => replica.Apply(new PNBuffApplyRequest(A, source)), "Replica cannot apply Buff");
        Reject(() => replica.Immune(ids: new[] { A }), "Replica cannot grant immunity");
    }
    private static void Timers(EveRegistryHub hub, Guid session, PNBuffSource source, List<string> log, List<Probe> probes, PNBuffMutationCoordinator coordinator, PNControlState controls)
    {
        using var buffs = new PNBuffCollection(hub, session, coordinator, new PNBuffServices(controls: controls));
        int errors = 0; buffs.Failed += _ => errors++;
        var bad = buffs.Apply(new PNBuffApplyRequest(Bad, source));
        Check(bad.Status == PNBuffStatus.Failed && !bad.Handle.IsAlive && controls.IsAllowed(PNControlIds.Move) && errors == 2, "Apply and cleanup faults cannot strand locks");
        var value = buffs.Apply(new PNBuffApplyRequest(A, source)); var probe = probes.Last();
        buffs.Advance(10); Check(probe.Ticks == 3 && !value.Handle.IsAlive && log.IndexOf("buff_a:end:Expired") > log.LastIndexOf("buff_a:tick"), "Long frame replays ticks through exact expiry before ending");
        value = buffs.Apply(new PNBuffApplyRequest(A, source)); probe = probes.Last();
        buffs.Advance(.5); buffs.Suspend(true); buffs.Advance(10);
        Check(value.Handle.IsAlive && buffs.Get(value.Handle).Remaining == 2.5 && buffs.Apply(new PNBuffApplyRequest(Multi, source)).Status == PNBuffStatus.Rejected, "Suspended target preserves time and rejects incoming apply");
        buffs.Suspend(false); buffs.Advance(.5); Check(probe.Ticks == 1 && probe.Applies == 1 && probe.Resumes == 1, "Resume neither catches up nor repeats Apply");
        probe.Tick = c => c.End(); buffs.Advance(10); Check(probe.Ticks == 2 && !value.Handle.IsAlive, "Removal during tick cancels all later events");
        value = buffs.Apply(new PNBuffApplyRequest(A, source)); probe = probes.Last(); PNBuffReceipt callbackApply = null;
        probe.Tick = c => { callbackApply = buffs.Apply(new PNBuffApplyRequest(Multi, source)); Check(callbackApply.Status == PNBuffStatus.Pending, "Reentrant apply receipt is pending until callback boundary"); probe.Tick = null; };
        buffs.Advance(2); Check(callbackApply.Status == PNBuffStatus.Applied && buffs.Get(callbackApply.Handle).Remaining == 3, "Callback-created Buff does not receive historical frame elapsed");
        probe.Tick = c => buffs.Advance(1); buffs.Advance(1); Check(!value.Handle.IsAlive && errors == 3, "Recursive clock mutation fails cleanly");
        foreach (var item in buffs.Snapshot()) buffs.Remove(item.Handle, PNBuffEndReason.Bypass, true);
        var future = buffs.Contribute("future", new PNBuffNumberContribution(typeof(PNBuffStackLimitContract), "maximum", 2), A, PNBuffSelection.Future);
        value = buffs.Apply(new PNBuffApplyRequest(A, source)); Check(probes.Last().Maximum == 5, "Future contribution joins before initial behavior");
        buffs.Remove(value.Handle, PNBuffEndReason.Bypass, true); value = buffs.Apply(new PNBuffApplyRequest(A, source)); Check(probes.Last().Maximum == 5, "Future scope survives retirement of a target"); future.Dispose();
        Check(probes.Last().Maximum == 3, "Source scope revoke removes future and current ownership");
        buffs.Remove(value.Handle, PNBuffEndReason.Bypass, true);
        var futureNumber = buffs.Contribute("future numeric", new PNBuffNumberContribution(typeof(PNBuffStackLimitContract), "maximum", 2), A, PNBuffSelection.Future);
        var futureOverride = buffs.Contribute("future override", new PNBuffOverrideContribution(B, 1), A, PNBuffSelection.Future);
        value = buffs.Apply(new PNBuffApplyRequest(A, source));
        Check(value.Status == PNBuffStatus.Applied && buffs.Get(value.Handle).ProfileId == B, "Future scope permits dormant base contracts when instance starts overridden");
        futureOverride.Dispose(); Check(probes.Last().Maximum == 5, "Born-overridden instance restores still-owned dormant contributions"); futureNumber.Dispose();
        var prior = value.Handle; buffs.Dispose(); buffs.Dispose(); Check(!prior.IsAlive, "Idempotent disposal and stale handle");
        using var left = new PNBuffCollection(hub, session, coordinator);
        using var right = new PNBuffCollection(hub, session, coordinator);
        var first = left.Apply(new PNBuffApplyRequest(A, source)); var leftProbe = probes.Last();
        var second = right.Apply(new PNBuffApplyRequest(A, source)); var rightProbe = probes.Last();
        PNBuffReceipt cross = null; leftProbe.Tick = c => cross = right.Remove(second.Handle, PNBuffEndReason.Bypass, true);
        rightProbe.End = c => left.Remove(first.Handle, PNBuffEndReason.Bypass, true);
        left.Advance(10); Check(cross.Status == PNBuffStatus.Removed && !first.Handle.IsAlive && !second.Handle.IsAlive && leftProbe.Ticks == 1, "Session coordinator serializes cross-entity callback mutation");
        first = left.Apply(new PNBuffApplyRequest(A, source)); leftProbe = probes.Last();
        leftProbe.Tick = c => { left.Suspend(true); left.Suspend(false); };
        left.Advance(1); Check(!left.Get(first.Handle).Suspended, "Queued suspend/resume requests preserve their order");
        leftProbe.ThrowCleanup = true;
        left.Contribute("faulting transition", new PNBuffOverrideContribution(B, 1), A);
        Check(!first.Handle.IsAlive && log.Count(x => x == "buff_a:cleanup") > 0, "Transition cleanup failure terminates instance instead of activating replacement");
    }
    private static void Content()
    {
        var modules = new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() };
        string json = @"{'schemaVersion':1,'extensions':{'project_n':{'buffTags':[{'id':'test:tag'}],'buffs':[{'id':'test:json','factoryId':'project_n:buff_control_template','data':{'duration':2,'clock':'Game','permissionIds':['base:skill_all'],'interrupt':false},'tags':['test:tag'],'multipleInstances':false,'maximumInstances':1,'scope':'Target','canDispel':false,'showInUI':true}]}}}";
        var document = new EveContentDocument("Buff test", json);
        var hub = new EveContentBootstrap().Build(modules, new[] { document });
        Check(hub.Buffs().IsSealed && hub.BuffTags().IsSealed && hub.Buffs().Get(EveId.Parse("test:json")).CanDispel == false, "JSON Buff and tag extension validates atomically");
        Check(hub.Buffs().Get(EveId.Parse("base:buff_control_template")).Id == EveId.Parse("project_n:buff_control_template"), "Buff public alias");
        Reject(() => new EveContentBootstrap().Build(modules, new[] { new EveContentDocument("bad", json.Replace("'duration':2", "'duration':0")) }), "Zero Buff duration rejected before session startup");
        Reject(() => new EveContentBootstrap().Build(modules, new[] { new EveContentDocument("bad", json.Replace("'scope':'Target'", "'scope':'Projectile'")) }), "Invalid instance scope rejected");
        Reject(() => new EveContentBootstrap().Build(modules, new[] { new EveContentDocument("bad", json.Replace("'showInUI':true", "'showInUI':true,'random':1")) }), "Unknown Buff metadata rejected");
        Reject(() => new EveContentBootstrap().Build(modules, new[] { new EveContentDocument("bad", json.Replace("['test:tag']", "['test:missing']")) }), "Unknown tag rejected");
        var data = hub.Buffs().Get(EveId.Parse("test:json")).Payload; ((JObject)data)["duration"] = 99;
        Check(hub.Buffs().Get(EveId.Parse("test:json")).Payload["duration"].Value<double>() == 2, "Payload defensive copy");
        Reject(() => hub.Buffs().Register(new PNBuffDefinition(A, EveId.Parse("test:pnbufftests_factory_5"), _ => new Empty(), null)), "Sealed Buff registry rejects additions");
        var atomic = new EveRegistryHub(); new EveBuiltInContentModule().Register(atomic); foreach (var module in modules) module.Register(atomic);
        Reject(() => new EveJsonContentLoader().Register(new[] { new EveContentDocument("atomic", json.Replace("'duration':2", "'duration':0")) }, atomic,
            null, null, new[] { (EveIContentExtension)modules[1] }), "Invalid batch rejected");
        Check(!atomic.BuffTags().TryGet(EveId.Parse("test:tag"), out _) && !atomic.Buffs().TryGet(EveId.Parse("test:json"), out _), "Invalid batch commits neither tags nor Buffs");
        Reject(() => new EveContentBootstrap().Build(modules, new[] { new EveContentDocument("dependency", json, "test", dependencies: System.Array.Empty<string>()) }), "Buff factory and permission references require pack dependency");
        var scoped = new EveContentBootstrap().Build(modules, new[] { new EveContentDocument("dependency", json.Replace("project_n:buff_control_template", "base:buff_control_template"), "test", dependencies: new[] { "project_n" }) });
        Check(scoped.Buffs().Get(EveId.Parse("test:json")).Id == EveId.Parse("test:json"), "Scoped JSON can derive from registered base template");
        string replacementJson = json.Replace("}}}", "}},'overrides':[{'registry':'buffs','id':'base:buff_control_template','implementationId':'test:json','version':'1'}]}");
        var selected = new EveContentBootstrap().Build(modules, new[] { new EveContentDocument("override", replacementJson, "test", dependencies: new[] { "project_n" }) });
        Check(selected.Buffs().Get(EveId.Parse("base:buff_control_template")).Id == EveId.Parse("test:json") && selected.Buffs().ResolveIdentity(EveId.Parse("base:buff_control_template")) == EveId.Parse("project_n:buff_control_template"), "Selected content Buff override preserves canonical identity");
        using (var collection = new PNBuffCollection(selected, Guid.NewGuid()))
        {
            var receipt = collection.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_control_template"), new PNBuffSource(collection.SessionId, new EveEntityId(1))));
            // No controls in this pure fixture: factory still selects replacement, then fails cleanly.
            Check(receipt.Status == PNBuffStatus.Failed && !receipt.Handle.IsAlive, "Selected factory failure does not attach a partially active Buff");
        }
        Reject(() => new EveContentBootstrap().Build(modules, new[] { new EveContentDocument("bad", json.Replace("'project_n':", "'unknown':")) }), "Unknown extension namespace rejected");
        Reject(() => new EveContentBootstrap().Build(modules, new[] { new EveContentDocument("bad", json.Replace("'duration':2", "'duration':2,'duration':3")) }), "Duplicate JSON fields rejected");
        string statJson = @"{'schemaVersion':1,'statDefinitions':[{'id':'test:power','kind':'number'}],'extensions':{'project_n':{'buffs':[{'id':'test:power_buff','factoryId':'base:buff_stat_template','data':{'duration':5,'clock':'Game','statId':'test:power','value':1,'group':0,'maximumStacks':3},'tags':['base:buff'],'multipleInstances':false,'maximumInstances':1,'scope':'Target','canDispel':true,'showInUI':false}]}}}";
        var sameBatch = new EveContentBootstrap().Build(modules, new[] { new EveContentDocument("same batch", statJson, "test", dependencies: new[] { "project_n" }) });
        Check(sameBatch.Stats.TryGet(EveId.Parse("test:power"), out _) && sameBatch.Buffs().TryGet(EveId.Parse("test:power_buff"), out _), "Buff can reference a stat staged in the same atomic JSON batch");
        Reject(() => new EveContentBootstrap().Build(modules, new[] { new EveContentDocument("same batch", statJson.Replace("'statId':'test:power'", "'statId':'test:missing'"), "test", dependencies: new[] { "project_n" }) }), "Unknown Buff stat fails atomic reference validation");
    }
    private static void Integration()
    {
        var runtime = new EveGameRuntime(); runtime.Initialize(); var hub = EveRegistrySession.Current;
        var source = Entity(980001); var target = Entity(980002);
        try
        {
            source.loader.Load(hub); target.loader.Load(hub);
            var manager = target.stat.GetComponent<PNBuffManager>(); var buffs = manager.Buffs;
            Check(manager.IsReady && target.stat.GetComponents<PNBuffManager>().Length == 1, "Stat preparation ensures one Buff manager");
            var snapshot = PNBuffSource.Capture(source.stat);
            var customCapture = new CapturedFactor(snapshot.Snapshot, 2);
            Check(customCapture.CreateDamage(5, false, 0, PNContentIds.True).Amount == 10, "Custom immutable snapshot wrapper can preserve implementation formula state");
            var data = new PNSkillData(null, new PNSkillActionData(EveId.Parse("base:buff_action"), perform: JObject.Parse("{'operationId':'base:apply_buff','data':{'buffId':'base:buff_stat_template'}}")));
            var context = new PNSkillActionContext(source.stat.GetComponent<PNSkill>(), PNGameplayContentModule.EveSkillEntry, data, hub, new[] { target.stat });
            using (var action = hub.Actions().Get(EveId.Parse("base:buff_action")).Create()) { action.Start(context); action.Perform(context); action.Exit(context); }
            Check(buffs.Snapshot().Count == 1, "Registered Buff Action and Operation grant lifetime to collection");
            using (var remove = hub.Operations().Get(EveId.Parse("base:remove_buff")).Create()) remove.Execute(context, JObject.Parse("{'buffId':'base:buff_stat_template'}"));
            Check(buffs.Snapshot().Count == 0, "Registered removal Operation uses ordinary dispel pipeline");
            var statBuff = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_stat_template"), snapshot));
            Reject(() => buffs.Contribute("invalid stack", new PNBuffNumberContribution(typeof(PNBuffStackLimitContract), "maximum", -3), EveId.Parse("base:buff_stat_template")), "Receiver prevalidates invalid aggregate without ending target Buff");
            Check(statBuff.Handle.IsAlive && buffs.Get(statBuff.Handle).Stacks == 1, "Rejected contribution retains existing state");
            double initial = hub.Characters.Get(EveId.Parse("project_n:eve")).Stats.First(x => hub.Stats.ResolveIdentity(x.Id) == PNContentIds.AttackPower).BaseValue;
            Check(target.stat.Evaluate(PNContentIds.AttackPower) == initial + 10, "Buff stat source modifies actual stat set");
            buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_stat_template"), snapshot)); Check(target.stat.Evaluate(PNContentIds.AttackPower) == initial + 20, "Stat Buff reapply owns stack formula");
            var floor = target.stat.EndFloorAndCaptureSnapshot(); Check(target.stat.Evaluate(PNContentIds.AttackPower) == initial + 20 && floor.PersistentSources.Count == 0, "Floor cleanup keeps live Buff ownership without persisting it in snapshot");
            buffs.Suspend(true); Check(target.stat.Evaluate(PNContentIds.AttackPower) == initial, "Suspend returns stat source"); buffs.Suspend(false); Check(target.stat.Evaluate(PNContentIds.AttackPower) == initial + 20, "Resume restores stat source without stacking again");
            var limit = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_stack_limit_template"), snapshot));
            for (int i = 0; i < 6; i++) buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_stat_template"), snapshot));
            Check(buffs.Get(statBuff.Handle).Stacks == 5, "Another Buff owns max-stack contribution"); buffs.Remove(limit.Handle, PNBuffEndReason.Bypass, true);
            Check(buffs.Get(statBuff.Handle).Stacks == 3 && target.stat.Evaluate(PNContentIds.AttackPower) == initial + 30, "Ending source Buff revokes max-stack lease and clamps excess");
            buffs.Remove(statBuff.Handle, PNBuffEndReason.Bypass, true); Check(target.stat.Evaluate(PNContentIds.AttackPower) == initial && target.stat.Set.SourceSnapshot().Count == 0, "Ending Buff returns all stat sources");
            int deathNotices = 0; target.stat.Died += _ => deathNotices++;
            double hp = target.stat.GetCurrent(PNContentIds.Health);
            var dot = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_dot_template"), snapshot));
            source.stat.SetCurrent(PNContentIds.Health, 0); Reject(() => source.stat.CreateDamage(1, PNContentIds.Physical), "Ordinary dead source remains prohibited");
            buffs.Advance(2); Check(target.stat.GetCurrent(PNContentIds.Health) < hp && dot.Handle.IsAlive, "DOT damages through combat after source death");
            source.loader.Unload(); hp = target.stat.GetCurrent(PNContentIds.Health); buffs.Advance(1); Check(target.stat.GetCurrent(PNContentIds.Health) < hp, "DOT fallback survives component unload");
            source.loader.Load(hub); snapshot = PNBuffSource.Capture(source.stat);
            var control = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_control_template"), snapshot));
            var state = target.stat.GetComponent<PNControlManager>().State;
            Check(control.Handle.IsAlive && !state.IsAllowed(PNControlIds.SkillAll), "Buff control lease gates skill categories");
            buffs.Suspend(true); Check(state.IsAllowed(PNControlIds.SkillAll), "Suspend returns control lease"); buffs.Suspend(false); Check(!state.IsAllowed(PNControlIds.SkillAll), "Resume reacquires control lease");
            var protectedOwner = state.Acquire("other", new[] { PNControlIds.Move }); buffs.Remove(control.Handle, PNBuffEndReason.Bypass, true);
            Check(!state.IsAllowed(PNControlIds.Move) && state.IsAllowed(PNControlIds.SkillAll), "Buff cleanup preserves unrelated Action owner"); protectedOwner.Dispose();
            var bomb = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_bomb_template"), snapshot)); hp = target.stat.GetCurrent(PNContentIds.Health);
            buffs.Remove(bomb.Handle); Check(target.stat.GetCurrent(PNContentIds.Health) == hp, "Dispelled bomb does not run expiration effect");
            bomb = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_bomb_template"), snapshot)); buffs.Advance(3);
            Check(!bomb.Handle.IsAlive && target.stat.GetCurrent(PNContentIds.Health) < hp, "Expired bomb executes exactly once through combat");
            var heal = snapshot.Snapshot.CreateHeal(5); source.loader.Unload(); target.stat.ReceiveHeal(heal);
            Check(heal.SourceId == new EveEntityId(980001), "Snapshot heal preserves original source attribution");
            var old = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_control_template"), snapshot)).Handle;
            target.loader.Unload(); target.loader.Load(hub); Check(!old.IsAlive && target.stat.GetComponent<PNBuffManager>().Buffs.Snapshot().Count == 0, "Native source runtime reload invalidates Buff handles");
        }
        finally { source.loader.Unload(); target.loader.Unload(); EveRegistrySession.End(); }
    }
    private static void Dot()
    {
        var runtime = new EveGameRuntime(); runtime.Initialize(); var hub = EveRegistrySession.Current;
        var source = Entity(981001); var second = Entity(981002); var target = Entity(981003);
        var burnId = EveId.Parse("base:burn"); var tune = EveId.Parse("test:dot_tune");
        try
        {
            source.loader.Load(hub); second.loader.Load(hub); target.loader.Load(hub);
            var buffs = target.stat.GetComponent<PNBuffManager>().Buffs;
            source.stat.SetSource(tune, new[] { new EveStatModifier(PNContentIds.AttackPower, 90, 0), new EveStatModifier(PNContentIds.CritRate, 100, 0) });
            target.stat.SetSource(tune, new[] { new EveStatModifier(PNContentIds.Health, 100000, 0) }); target.stat.SetCurrent(PNContentIds.Health, target.stat.GetMax(PNContentIds.Health));
            Check(hub.BuffTags().TryGet(EveId.Parse("base:dot"), out _) && hub.BuffTags().TryGet(EveId.Parse("base:fire"), out _) && !hub.BuffTags().TryGet(EveId.Parse("project_n:damage_over_time"), out _), "DOT tag renamed without old identity");
            Reject(() => hub.BuffTags().Get(EveId.Parse("base:damage_over_time")), "Old DOT alias removed");
            var definition = hub.Buffs().Get(burnId);
            Check(definition.CanDispel && definition.Scope == PNBuffInstanceScope.Source && !definition.MultipleInstances && definition.Tags.Count == 3, "Burn tags and source grouping registered");
            foreach (var field in new[] { "maximumStacks", "period", "attackRatio", "sourceMode" })
            {
                var payload = (JObject)definition.Payload;
                if (field == "sourceMode") payload[field] = "Snapshot";
                else payload[field] = field == "attackRatio" ? -1 : 0;
                var invalid = definition.Derive(EveId.Parse("test:invalid_dot"), payload, definition.Tags, false, 1, PNBuffInstanceScope.Source, true);
                Reject(() => invalid.Validate(hub), "DOT payload rejects invalid or removed field " + field);
            }
            var captured = PNBuffSource.Capture(source.stat);
            foreach (var json in new[] { "{'stacks':0}", "{'stacks':-1}", "{'stacks':1.5}", "{'amount':99}", "{'stacks':null}" })
                Reject(() => buffs.Apply(new PNBuffApplyRequest(burnId, captured, JObject.Parse(json))), "Invalid DOT contextual input " + json);
            var burn = buffs.Apply(new PNBuffApplyRequest(burnId, captured, new JObject()));
            Check(burn.Status == PNBuffStatus.Applied && buffs.Get(burn.Handle).Stacks == 1, "DOT omitted stack amount defaults to one");
            double hp = target.stat.GetCurrent(PNContentIds.Health);
            buffs.Advance(.4);
            buffs.Apply(new PNBuffApplyRequest(burnId, captured, JObject.Parse("{'stacks':34}")));
            Check(buffs.Get(burn.Handle).Stacks == 35 && Math.Abs(buffs.Get(burn.Handle).Remaining.Value - 15) < 1e-8, "Burn incoming stacks clamp and reapply refreshes lifetime");
            buffs.Advance(.4);
            var expected = source.stat.CaptureBuffCombatSource().CreateDamage(175, false, 0, PNContentIds.Magic, PNContentIds.Fire);
            double mitigation = target.stat.CalculateDamage(expected.Amount, expected.Tags.ToArray());
            Check(Math.Abs(hp - target.stat.GetCurrent(PNContentIds.Health) - mitigation) < 1e-6, "Burn tick at original .8 cadence deals 175 percent ATK at cap without crit");
            var full = buffs.Apply(new PNBuffApplyRequest(burnId, captured, JObject.Parse("{'stacks':int}".Replace("int", int.MaxValue.ToString()))));
            Check(full.Status == PNBuffStatus.Reapplied && buffs.Get(burn.Handle).Stacks == 35 && buffs.Get(burn.Handle).Remaining == 15, "Full stack refresh and overflow-safe addition");
            source.stat.SetSource(tune, new[] { new EveStatModifier(PNContentIds.AttackPower, 190, 0) });
            double remaining = buffs.Get(burn.Handle).Remaining.Value; hp = target.stat.GetCurrent(PNContentIds.Health);
            var triggered = buffs.Trigger(burn.Handle);
            expected = source.stat.CaptureBuffCombatSource().CreateDamage(350, false, 0, PNContentIds.Magic, PNContentIds.Fire);
            mitigation = target.stat.CalculateDamage(expected.Amount, expected.Tags.ToArray());
            Check(triggered.Status == PNBuffStatus.Triggered && Math.Abs(hp - target.stat.GetCurrent(PNContentIds.Health) - mitigation) < 1e-6 && buffs.Get(burn.Handle).Remaining == remaining, "Manual trigger uses live ATK without changing time or stacks");
            PNDamageResult changed = null; Action<PNDamageResult> captureChanged = value => changed = value;
            target.stat.DamageReceived += captureChanged;
            try
            {
                using (var valueLease = buffs.Contribute("dynamic dot value", new PNBuffNumberContribution(typeof(PNBuffValueContract), "amount", 2), selection: PNBuffSelection.Current, exact: burn.Handle))
                {
                    buffs.Trigger(burn.Handle);
                    Check(Math.Abs(changed.Damage.Amount - 420) < 1e-8, "DOT contribution updates cached amount immediately");
                    buffs.ChangeStacks(burn.Handle, -1); buffs.Trigger(burn.Handle);
                    Check(Math.Abs(changed.Damage.Amount - 408) < 1e-8, "DOT uses changed stack with prepared contribution");
                    buffs.ChangeStacks(burn.Handle, 1);
                }
                buffs.Trigger(burn.Handle);
                Check(Math.Abs(changed.Damage.Amount - 350) < 1e-8, "revoking DOT contribution restores amount without manual invalidation");
                Action<PNDamageResult> changeSource = _ => source.stat.SetSource(tune, new[] { new EveStatModifier(PNContentIds.AttackPower, 290, 0) });
                target.stat.DamageReceived += changeSource;
                try { buffs.Trigger(burn.Handle); } finally { target.stat.DamageReceived -= changeSource; }
                buffs.Trigger(burn.Handle);
                Check(Math.Abs(changed.Damage.Amount - 525) < 1e-8, "next same-frame DOT hit sees stat changed by preceding hit callback");
                source.stat.SetSource(tune, new[] { new EveStatModifier(PNContentIds.AttackPower, 190, 0) });
            }
            finally { target.stat.DamageReceived -= captureChanged; }
            hp = target.stat.GetCurrent(PNContentIds.Health); buffs.Advance(.8);
            Check(Math.Abs(hp - target.stat.GetCurrent(PNContentIds.Health) - mitigation) < 1e-6, "Manual trigger does not move automatic tick");
            source.stat.SetCurrent(PNContentIds.Health, 0); source.stat.SetSource(tune, new[] { new EveStatModifier(PNContentIds.AttackPower, 290, 0) });
            hp = target.stat.GetCurrent(PNContentIds.Health); buffs.Trigger(burn.Handle);
            expected = source.stat.CaptureBuffCombatSource().CreateDamage(525, false, 0, PNContentIds.Magic, PNContentIds.Fire);
            mitigation = target.stat.CalculateDamage(expected.Amount, expected.Tags.ToArray());
            Check(burn.Handle.IsAlive && Math.Abs(hp - target.stat.GetCurrent(PNContentIds.Health) - mitigation) < 1e-6, "Dead loaded source still supplies live ATK");
            source.loader.Unload(); hp = target.stat.GetCurrent(PNContentIds.Health); buffs.Trigger(burn.Handle);
            Check(Math.Abs(hp - target.stat.GetCurrent(PNContentIds.Health) - mitigation) < 1e-6, "Unloaded source retains last valid ATK/calculation attribution");
            var other = buffs.Apply(new PNBuffApplyRequest(burnId, PNBuffSource.Capture(second.stat), JObject.Parse("{'stacks':35}")));
            Check(!other.Handle.Equals(burn.Handle) && buffs.Get(other.Handle).Stacks == 35 && buffs.Get(burn.Handle).Stacks == 35, "Two sources own independent capped Burn instances");
            using (var cap = buffs.Contribute("cap", new PNBuffNumberContribution(typeof(PNBuffStackLimitContract), "maximum", -25), burnId))
                Check(buffs.Get(burn.Handle).Stacks == 10 && buffs.Get(other.Handle).Stacks == 10, "DOT cap contribution clamps each source");
            Check(buffs.Get(burn.Handle).Stacks == 10, "Cap restoration does not resurrect discarded stacks");
            remaining = buffs.Get(burn.Handle).Remaining.Value; buffs.ChangeStacks(burn.Handle, 2);
            Check(buffs.Get(burn.Handle).Stacks == 12 && buffs.Get(burn.Handle).Remaining == remaining, "Direct stack change does not refresh duration");
            buffs.Suspend(true); hp = target.stat.GetCurrent(PNContentIds.Health);
            Check(buffs.Trigger(burn.Handle).Status == PNBuffStatus.Rejected && buffs.ChangeStacks(burn.Handle, 1).Status == PNBuffStatus.Rejected, "Suspended DOT cannot trigger or mutate stacks");
            buffs.Advance(5); buffs.Suspend(false);
            Check(target.stat.GetCurrent(PNContentIds.Health) == hp && buffs.Get(burn.Handle).Remaining == remaining, "Suspend pauses timer and damage without replaying initial apply");
            buffs.ChangeStacks(burn.Handle, int.MinValue);
            Check(!burn.Handle.IsAlive && other.Handle.IsAlive && buffs.Trigger(burn.Handle).Status == PNBuffStatus.Rejected, "Zero stacks ends only selected instance and stale trigger rejects");
            buffs.Remove(other.Handle);
            var operationData = new PNSkillData(null, new PNSkillActionData(EveId.Parse("base:buff_action"), perform: JObject.Parse("{'operationId':'base:trigger_buff','data':{'buffId':'base:burn','sourceSelection':'Caster'}}")));
            var context = new PNSkillActionContext(second.stat.GetComponent<PNSkill>(), PNGameplayContentModule.EveSkillEntry, operationData, hub, new[] { target.stat });
            other = buffs.Apply(new PNBuffApplyRequest(burnId, PNBuffSource.Capture(second.stat)));
            PNBuffReceipt queued = null; bool enqueueOnce = true;
            Action<PNDamageResult> queueTrigger = _ => { if (enqueueOnce) { enqueueOnce = false; queued = buffs.Trigger(other.Handle); Check(queued.Status == PNBuffStatus.Pending, "Reentrant DOT trigger returns Pending"); } };
            target.stat.DamageReceived += queueTrigger;
            try { buffs.Trigger(other.Handle); } finally { target.stat.DamageReceived -= queueTrigger; }
            Check(queued != null && queued.Status == PNBuffStatus.Triggered && other.Handle.IsAlive, "Queued DOT trigger completes at mutation boundary");
            using (var operation = hub.Operations().Get(EveId.Parse("base:trigger_buff")).Create()) operation.Execute(context, JObject.Parse("{'buffId':'base:burn','sourceSelection':'Caster'}"));
            Check(other.Handle.IsAlive && buffs.Get(other.Handle).Stacks == 1, "Registered trigger Operation selects caster instance");
            using (var operation = hub.Operations().Get(EveId.Parse("base:change_buff_stacks")).Create()) operation.Execute(context, JObject.Parse("{'buffId':'base:burn','sourceSelection':'All','delta':-1}"));
            Check(!other.Handle.IsAlive, "Registered stack Operation uses collection cleanup");
            using (var immunity = buffs.Immune(tags: new[] { EveId.Parse("base:dot") }))
                Check(buffs.Apply(new PNBuffApplyRequest(burnId, PNBuffSource.Capture(second.stat))).Status == PNBuffStatus.Rejected, "DOT immunity blocks Burn before effects");
            var plain = buffs.Apply(new PNBuffApplyRequest(EveId.Parse("base:buff_dot_template"), PNBuffSource.Capture(second.stat), JObject.Parse("{'stacks':5}")));
            Check(buffs.Get(plain.Handle).Stacks == 1, "Generic DOT template defaults to stack cap one"); buffs.Remove(plain.Handle);
            other = buffs.Apply(new PNBuffApplyRequest(burnId, PNBuffSource.Capture(second.stat))); hp = target.stat.GetCurrent(PNContentIds.Health);
            int ticks = 0; target.stat.DamageReceived += _ => ticks++;
            buffs.Advance(14.8); double after = target.stat.GetCurrent(PNContentIds.Health); buffs.Advance(.201);
            Check(!other.Handle.IsAlive && after < hp && ticks == 18 && target.stat.GetCurrent(PNContentIds.Health) == after, "Burn expiry has 18 ticks and no extra final tick");
        }
        finally { source.loader.Unload(); second.loader.Unload(); target.loader.Unload(); EveRegistrySession.End(); }
    }
    private static void Elemental()
    {
        new EveGameRuntime().Initialize(); var hub = EveRegistrySession.Current;
        var source = Entity(982001); var second = Entity(982002); var target = Entity(982003);
        try
        {
            source.loader.Load(hub); second.loader.Load(hub); target.loader.Load(hub);
            target.stat.SetSource(EveId.Parse("test:element_hp"), new[] { new EveStatModifier(PNContentIds.Health, 100000, 0) });
            target.stat.SetCurrent(PNContentIds.Health, target.stat.GetMax(PNContentIds.Health));
            var buffs = target.stat.GetComponent<PNBuffManager>().Buffs; var captured = PNBuffSource.Capture(source.stat);
            Check(hub.BuffTags().TryGet(EveId.Parse("base:stat"), out _) && hub.BuffTags().TryGet(EveId.Parse("base:vulnerability"), out _), "Stat and vulnerability vocabulary registered");
            foreach (var name in new[] { "fire", "ice", "lightning" })
            {
                var element = EveId.Parse("base:" + name); var id = EveId.Parse("base:" + name + "_vulnerability");
                var definition = hub.Buffs().Get(id);
                Check(definition.Tags.Count == 3 && !definition.Tags.Contains(EveId.Parse("project_n:stat")) && definition.Scope == PNBuffInstanceScope.Target, "Vulnerability tags and target grouping " + name);
                var invalidPayload=(JObject)definition.Payload;invalidPayload["group"]=-1;
                Reject(()=>definition.Derive(EveId.Parse("test:bad_vulnerability"),invalidPayload,definition.Tags,false,1,PNBuffInstanceScope.Target,true).Validate(hub),"Vulnerability rejects negative group " + name);
                double original = target.stat.CalculateDamage(100, PNContentIds.Magic, element);
                double physical = target.stat.CalculateDamage(100, PNContentIds.Physical);
                var grant = buffs.Apply(new PNBuffApplyRequest(id, captured));
                Check(Math.Abs(target.stat.CalculateDamage(100, PNContentIds.Magic, element) - original*1.75) < 1e-8, "Vulnerability uses receiver mitigation " + name);
                Check(target.stat.CalculateDamage(100, PNContentIds.Physical) == physical && source.stat.Calculate(PNContentIds.Scale, 100, element) == 100, "Vulnerability leaves unrelated damage and sender Scale unchanged " + name);
                buffs.Advance(2, .5); Check(buffs.Get(grant.Handle).Remaining == 14, "Vulnerability Entity duration " + name);
                var reapply = buffs.Apply(new PNBuffApplyRequest(id, PNBuffSource.Capture(second.stat)));
                Check(reapply.Handle.Equals(grant.Handle) && buffs.Get(grant.Handle).Remaining == 15 && buffs.Get(grant.Handle).Stacks == null, "Vulnerability other source refreshes without stacking " + name);
                target.stat.SetSource(EveId.Parse("test:resistance"), new[] {new EveStatModifier(element,-.2,1,PNContentIds.Mitigation)});
                Check(Math.Abs(target.stat.CalculateDamage(100, PNContentIds.Magic, element)-original*.8*1.75)<1e-8,"Vulnerability group2 multiplies group1 resistance " + name);
                buffs.Suspend(true); Check(Math.Abs(target.stat.CalculateDamage(100,PNContentIds.Magic,element)-original*.8)<1e-8,"Suspend returns only vulnerability modifier " + name);
                buffs.Suspend(false); buffs.Remove(grant.Handle);
                Check(Math.Abs(target.stat.CalculateDamage(100,PNContentIds.Magic,element)-original*.8)<1e-8,"Dispel preserves independent resistance " + name);
                target.stat.RemoveSource(EveId.Parse("test:resistance"));
            }
            foreach (var name in new[] {"electrocution","frostbite"})
            {
                var id=EveId.Parse("base:"+name); var grant=buffs.Apply(new PNBuffApplyRequest(id,captured));
                PNDamageResult last=null;Action<PNDamageResult> watch=r=>last=r;target.stat.DamageReceived+=watch;
                try
                {
                    double atk=source.stat.Evaluate(PNContentIds.AttackPower);
                    foreach(int n in name=="electrocution" ? new[]{1,2,3,10} : new[]{1,2,5})
                    {
                        buffs.ChangeStacks(grant.Handle,n-(int)buffs.Get(grant.Handle).Stacks.Value);buffs.Trigger(grant.Handle);
                        double ratio=name=="electrocution" ? .10+(n-1)*.10+(n*(n+1d)/2-1)*.03 : 1.5+(n-1)*.5;
                        Check(Math.Abs(last.Damage.Amount-atk*ratio)<1e-8 && !last.Damage.CanCrit,"Incremental DOT amount at stack " + n + " " + name);
                    }
                    buffs.ChangeStacks(grant.Handle, -1); buffs.Trigger(grant.Handle);
                    int current=(int)buffs.Get(grant.Handle).Stacks.Value;
                    double reduced=name=="electrocution" ? .10+(current-1)*.10+(current*(current+1d)/2-1)*.03 : 1.5+(current-1)*.5;
                    Check(Math.Abs(last.Damage.Amount-atk*reduced)<1e-8,"DOT recomputes reduced stacks " + name);
                    double period=((JObject)hub.Buffs().Get(id).Payload)["period"].Value<double>();
                    last=null;buffs.Advance(period,.5);Check(last==null,"DOT Entity half scale delays tick " + name);
                    buffs.Advance(period,.5);Check(last!=null,"DOT Entity half scale reaches tick " + name);
                }
                finally{target.stat.DamageReceived-=watch;buffs.Remove(grant.Handle);}
                var payload=(JObject)hub.Buffs().Get(id).Payload;payload.Remove("additionalStackGrowth");
                Reject(()=>hub.Buffs().Get(id).Derive(EveId.Parse("test:bad_incremental"),payload,hub.Buffs().Get(id).Tags,false,1,PNBuffInstanceScope.Source,true).Validate(hub),"Missing paired incremental field rejects " + name);
                int tickCount=0;Action<PNDamageResult> countTick=_=>tickCount++;target.stat.DamageReceived+=countTick;
                var expiry=buffs.Apply(new PNBuffApplyRequest(id,captured));
                try{buffs.Advance(name=="electrocution"?5:12);}finally{target.stat.DamageReceived-=countTick;}
                Check(!expiry.Handle.IsAlive && tickCount==(name=="electrocution"?12:8),"DOT exact lifetime catches all ticks including expiry before cleanup " + name);
            }
            foreach(var json in new[]{"{'kind':'Unknown','slot':0,'color':[1,0,0],'size':0.1}","{'kind':'Arrow','slot':0,'color':[1,0],'size':0.1}","{'kind':'Arrow','slot':0,'color':[1,0,0],'size':0}","{'kind':'Arrow','slot':0,'color':[1,0,0],'size':0.1,'unexpected':true}"})
                Reject(()=>PNBuffElementVisualSettings.Validate(JObject.Parse(json)),"Malformed visual rejects " + json);
        }
        finally{source.loader.Unload();second.loader.Unload();target.loader.Unload();EveRegistrySession.End();}
    }
    private static (EveTestLoader loader, PNStat stat) Entity(long id)
    {
        var loader = new EveTestLoader(); var stat = new EveCharacterStat(); var identity = new EveEntityIdentity(); identity.Assign(new EveEntityId(id), EveId.Parse("project_n:eve"), 0);
        MonoBehaviour[] components = { loader, identity, stat, new PNSkill(), new PNAbilityManager() }; foreach (var c in components) { c.Components = components; c.gameObject.SetActive(true); } return (loader, stat);
    }
}
