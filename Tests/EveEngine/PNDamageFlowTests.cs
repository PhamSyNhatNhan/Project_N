using System;
using System.Collections.Generic;
using EveEngine;
using UnityEngine;
using static EveSessionSmokeTests;

public class PNOverrideDamageStat : PNStat
{
    public override double DefaultIFrameDuration => 0d; // These checks isolate calculation overrides.
    public double OutgoingFactor = 1d;
    public double IncomingFactor = 1d;
    public override double Calculate(EveId calculationId, double baseValue, params EveId[] ids)
        => base.Calculate(calculationId, baseValue, ids) * OutgoingFactor;
    public override double CalculateDamage(double damage, params EveId[] tags)
        => base.CalculateDamage(damage, tags) * IncomingFactor;
}

// EN: Exercises authoritative send/receive, delayed payloads, health commits, death and lifecycle boundaries.
// VI: Kiểm tra gửi/nhận có thẩm quyền, payload trì hoãn, ghi HP, chết và ranh giới vòng đời.
public static class PNDamageFlowTests
{
    private static readonly EveId Hero = EveId.Parse("test:damage_hero");
    private static readonly EveId Star = EveId.Parse("test:star");
    private static readonly EveId Bonus = EveId.Parse("test:outgoing_bonus");
    private static readonly EveId Resistance = EveId.Parse("test:incoming_resistance");

    public static void Run()
    {
        var tags = new[] { PNContentModule.Fire, PNContentModule.Physical, PNContentModule.Fire };
        var packet = new PNDamage(Guid.NewGuid(), new EveEntityId(99), 100d, tags);
        tags[0] = Star;
        Check(packet.Tags.Count == 2 && packet.Tags[0] == PNContentModule.Fire && packet.Amount == 100d,
            "damage payload copies, sorts and deduplicates tags");
        Reject(() => ((IList<EveId>)packet.Tags)[0] = Star, "damage tags are immutable");
        Reject(() => new PNDamage(Guid.Empty, new EveEntityId(99), 100d, tags), "missing damage session rejected");
        Reject(() => new PNDamage(Guid.NewGuid(), default, 100d, tags), "missing damage source rejected");
        Reject(() => new PNDamage(Guid.NewGuid(), new EveEntityId(99), -1d, tags), "negative payload amount rejected");
        Reject(() => new PNDamage(Guid.NewGuid(), new EveEntityId(99), double.NaN, tags), "NaN payload rejected");
        Reject(() => new PNDamage(Guid.NewGuid(), new EveEntityId(99), double.PositiveInfinity, tags), "infinite payload rejected");
        Reject(() => new PNDamage(Guid.NewGuid(), new EveEntityId(99), 100d, null), "null damage tags rejected");
        Reject(() => new PNDamage(Guid.NewGuid(), new EveEntityId(99), 100d, Array.Empty<EveId>()), "empty damage tags rejected");
        Reject(() => new PNDamage(Guid.NewGuid(), new EveEntityId(99), 100d, new[] { default(EveId) }), "invalid tag ID rejected");

        var hub = Hub();
        Begin(hub, EveSessionRole.Offline);
        var source = Entity(1001);
        var target = Entity(1002);
        PNDamage delayed = null;
        int receives = 0, deaths = 0;
        bool observedCommittedHealth = true, observedDeath = true;
        try
        {
            Reject(() => source.stat.SendDamage(target.stat, 100d, PNContentModule.Physical), "send before Ready rejected");
            source.loader.Load(hub);
            target.loader.Load(hub);
            source.stat.SetSource(Bonus, new[] {
                new EveStatModifier(PNContentModule.Fire, .2d, 1, PNContentModule.Scale),
                new EveStatModifier(PNContentModule.AttackPower, .2d, 1, PNContentModule.Scale),
                new EveStatModifier(PNContentModule.Fire, -.9d, 1, PNContentModule.Mitigation)
            });
            target.stat.SetSource(Resistance, new[] {
                new EveStatModifier(PNContentModule.Fire, -.2d, 1, PNContentModule.Mitigation),
                new EveStatModifier(PNContentModule.Fire, .9d, 1, PNContentModule.Scale)
            });
            target.stat.DamageReceived += result => { receives++; observedCommittedHealth &= target.stat.GetCurrent(PNContentModule.Health) == result.HealthAfter; };
            target.stat.Died += result => { deaths++; observedDeath &= result.Killed && target.stat.IsDead; };
            var result = source.stat.SendDamage(target.stat, 100d, PNContentModule.Physical, PNContentModule.Fire);
            Check(Near(result.Damage.Amount, 120d) && Near(result.CalculatedDamage, 48d) &&
                Near(result.AppliedDamage, 48d) && result.HealthBefore == 100d && Near(result.HealthAfter, 52d),
                "send applies outgoing Scale then receiver DEF/resistance and HP loss");
            Check(result.Damage.SourceId == source.identity.Id && result.TargetId == target.identity.Id &&
                result.Damage.SessionId == EveRegistrySession.Context.SessionId, "damage result preserves source, target and session identity");
            Check(receives == 1 && deaths == 0 && !result.Killed && !target.stat.IsDead, "nonlethal hit emits received but not death");
            Check(source.stat.GetCurrent(PNContentModule.Health) == 100d && target.stat.GetMax(PNContentModule.Health) == 100d,
                "sending does not damage the sender or change receiver maximum");
            Check(Near(source.stat.CreateDamage(source.stat.Evaluate(PNContentModule.AttackPower), PNContentModule.Physical, PNContentModule.Fire).Amount, 144d),
                "evaluated ATK is not buffed a second time when applying unrelated damage tags");
            Check(Near(source.stat.CreateDamage(100d, PNContentModule.Fire, PNContentModule.Physical, PNContentModule.Fire).Amount, 120d),
                "repeated tags do not apply outgoing bonus twice");
            Reject(() => source.stat.CreateDamage(-1d, PNContentModule.True), "negative raw send value rejected");
            Reject(() => source.stat.CreateDamage(double.NaN, PNContentModule.True), "NaN raw send value rejected");
            Reject(() => source.stat.CreateDamage(100d), "sender rejects missing damage type");
            Reject(() => source.stat.CreateDamage(100d, PNContentModule.Physical, PNContentModule.Magic), "sender rejects conflicting damage types");
            Reject(() => source.stat.SendDamage(null, 100d, PNContentModule.True), "null target rejected");
            double before = target.stat.GetCurrent(PNContentModule.Health);
            Reject(() => target.stat.ReceiveDamage(null), "null incoming payload rejected");
            Reject(() => target.stat.ReceiveDamage(packet), "foreign session payload rejected");
            Reject(() => target.stat.ReceiveDamage(new PNDamage(source.stat.SessionId, source.identity.Id, 100d,
                new[] { EveId.Parse("test:unknown") })), "unknown incoming tag rejected");
            Reject(() => target.stat.ReceiveDamage(new PNDamage(source.stat.SessionId, source.identity.Id, 100d,
                new[] { PNContentModule.Fire })), "incoming payload without damage type rejected");
            Check(target.stat.GetCurrent(PNContentModule.Health) == before && receives == 1 && deaths == 0,
                "invalid damage leaves HP and notifications unchanged");

            target.stat.SetCurrent(PNContentModule.Health, 100d);
            var trueResult = source.stat.SendDamage(target.stat, 100d, PNContentModule.True, PNContentModule.Fire);
            Check(Near(trueResult.CalculatedDamage, 96d) && Near(trueResult.HealthAfter, 4d), "true bypasses DEF but preserves both sides of tag scaling");
            target.stat.SetCurrent(PNContentModule.Health, 100d);
            delayed = source.stat.CreateDamage(100d, PNContentModule.Physical, PNContentModule.Fire);
            source.stat.RemoveSource(Bonus);
            var captured = target.stat.ReceiveDamage(delayed);
            Check(Near(captured.CalculatedDamage, 48d) && Near(captured.Damage.Amount, 120d), "delayed damage snapshots bonuses instead of reevaluating the sender");
            source.loader.Unload();
            target.stat.SetCurrent(PNContentModule.Health, 100d);
            Check(Near(target.stat.ReceiveDamage(delayed).CalculatedDamage, 48d), "delayed damage remains valid after source unloads");
            Reject(() => source.stat.CreateDamage(100d, PNContentModule.True), "unloaded source cannot create new damage");
            source.loader.Load(hub);

            target.stat.RemoveSource(Resistance);
            target.stat.SetCurrent(PNContentModule.Health, 10d);
            var kill = source.stat.SendDamage(target.stat, 25d, PNContentModule.True);
            Check(kill.CalculatedDamage == 25d && kill.AppliedDamage == 10d && kill.HealthAfter == 0d && kill.Killed && deaths == 1,
                "overkill clamps HP and emits one death transition");
            int previousReceives = receives;
            var alreadyDead = target.stat.ReceiveDamage(kill.Damage);
            Check(alreadyDead.AppliedDamage == 0d && !alreadyDead.Killed && deaths == 1 && receives == previousReceives,
                "hitting dead targets is a no-op without repeated death or received notifications");
            target.stat.SetCurrent(PNContentModule.Health, 100d);
            var zero = source.stat.SendDamage(target.stat, 0d, PNContentModule.True);
            Check(zero.AppliedDamage == 0d && zero.HealthAfter == 100d && !zero.Killed, "zero damage does not kill or change HP");
            target.stat.SetSource(Resistance, new[] { new EveStatModifier(PNContentModule.Fire, -1d, 1, PNContentModule.Mitigation) });
            Check(source.stat.SendDamage(target.stat, 100d, PNContentModule.True, PNContentModule.Fire).AppliedDamage == 0d,
                "full tag resistance blocks health loss");
            target.stat.RemoveSource(Resistance);

            int healthyObserver = 0;
            target.stat.DamageReceived += value => throw new Exception("test observer failure");
            target.stat.DamageReceived += value => healthyObserver++;
            target.stat.Died += value => throw new Exception("test death observer failure");
            target.stat.SetCurrent(PNContentModule.Health, 1d);
            var observed = source.stat.SendDamage(target.stat, 25d, PNContentModule.True);
            Check(observed.AppliedDamage == 1d && observed.Killed && healthyObserver == 1 && deaths == 2,
                "observer failures cannot roll back committed damage, skip later listeners or prevent death");
            source.stat.SetCurrent(PNContentModule.Health, 0d);
            Reject(() => source.stat.CreateDamage(100d, PNContentModule.True), "dead sources cannot create new damage");
            target.stat.SetCurrent(PNContentModule.Health, 100d);
            Check(Near(target.stat.ReceiveDamage(delayed).CalculatedDamage, 60d), "existing payload can land after sender death");
            source.stat.SetCurrent(PNContentModule.Health, 100d);

            source.stat.SetSource(Bonus, new[] { new EveStatModifier(PNContentModule.True, 1d, 1, PNContentModule.Scale) });
            double saved = target.stat.GetCurrent(PNContentModule.Health);
            Reject(() => source.stat.SendDamage(target.stat, double.MaxValue, PNContentModule.True), "outgoing overflow rejects before HP mutation");
            Check(target.stat.GetCurrent(PNContentModule.Health) == saved, "overflow leaves target health unchanged");
            source.stat.RemoveSource(Bonus);
            target.stat.SetSource(Resistance, new[] { new EveStatModifier(PNContentModule.Defense, -1000d, 0) });
            Reject(() => target.stat.ReceiveDamage(new PNDamage(source.stat.SessionId, source.identity.Id, double.MaxValue,
                new[] { PNContentModule.Physical })), "incoming DEF amplification overflow rejects before mutation");
            Check(target.stat.GetCurrent(PNContentModule.Health) == saved, "incoming overflow leaves health unchanged");

            int oldReceives = receives;
            target.loader.Unload();
            Reject(() => target.stat.ReceiveDamage(delayed), "unloaded receiver cannot apply damage");
            target.loader.Load(hub);
            source.stat.SendDamage(target.stat, 1d, PNContentModule.True);
            Check(receives == oldReceives && healthyObserver == 2 && target.stat.GetCurrent(PNContentModule.Health) == 99d,
                "unload clears previous-lifetime damage listeners");
            Check(observedCommittedHealth && observedDeath, "all observers see committed HP and death transitions");
        }
        finally { source.loader.Unload(); target.loader.Unload(); EveRegistrySession.End(); }

        foreach (var role in new[] { EveSessionRole.Host, EveSessionRole.Client, EveSessionRole.Offline })
        {
            Begin(hub, role);
            var actor = Entity(2001);
            var receiver = Entity(2002);
            try
            {
                actor.loader.Load(hub);
                receiver.loader.Load(hub);
                if (role == EveSessionRole.Client)
                {
                    Reject(() => actor.stat.CreateDamage(1d, PNContentModule.True), "client cannot create authoritative damage");
                    Reject(() => actor.stat.SendDamage(receiver.stat, 1d, PNContentModule.True), "client cannot send authoritative damage");
                    Reject(() => receiver.stat.ReceiveDamage(new PNDamage(EveRegistrySession.Context.SessionId, actor.identity.Id, 1d,
                        new[] { PNContentModule.True })), "client cannot mutate HP through receive");
                    Check(receiver.stat.GetCurrent(PNContentModule.Health) == 100d, "client rejection preserves HP");
                }
                else
                {
                    Check(actor.stat.SendDamage(receiver.stat, 1d, PNContentModule.True).AppliedDamage == 1d,
                        "host and offline roles can apply authoritative damage");
                    Reject(() => receiver.stat.ReceiveDamage(delayed), "old-session delayed payload cannot enter a new session");
                    Check(receiver.stat.GetCurrent(PNContentModule.Health) == 99d, "stale session payload rejection preserves HP");
                }
            }
            finally { actor.loader.Unload(); receiver.loader.Unload(); EveRegistrySession.End(); }
        }
        Begin(hub, EveSessionRole.Offline);
        var customSender = new PNOverrideDamageStat { OutgoingFactor = 2d };
        var customReceiver = new PNOverrideDamageStat { IncomingFactor = 2d };
        var customSource = Entity(3001, customSender);
        var customTarget = Entity(3002, customReceiver);
        try
        {
            customSource.loader.Load(hub);
            customTarget.loader.Load(hub);
            var customized = customSender.SendDamage(customReceiver, 10d, PNContentModule.Physical);
            Check(customized.Damage.Amount == 20d && customized.AppliedDamage == 20d && customized.HealthAfter == 80d,
                "damage flow respects component outgoing and incoming calculation overrides");
            customReceiver.IncomingFactor = -1d;
            Reject(() => customSender.SendDamage(customReceiver, 10d, PNContentModule.True), "negative custom received damage rejected");
            customReceiver.IncomingFactor = double.NaN;
            Reject(() => customSender.SendDamage(customReceiver, 10d, PNContentModule.True), "non-finite custom received damage rejected");
            customSender.OutgoingFactor = -1d;
            Reject(() => customSender.CreateDamage(10d, PNContentModule.True), "negative custom outgoing damage rejected");
            Check(customReceiver.GetCurrent(PNContentModule.Health) == 80d, "invalid calculation overrides cannot mutate health");
        }
        finally { customSource.loader.Unload(); customTarget.loader.Unload(); EveRegistrySession.End(); }
        Reject(() => new PNStat().CreateDamage(1d, PNContentModule.True), "stopped session and uninitialized stats cannot send");
    }

    private static EveRegistryHub Hub()
    {
        var hub = new EveRegistryHub();
        new PNContentModule().Register(hub);
        hub.Tags().Register(new PNTagDefinition(Star));
        hub.GetOrCreate<EveCharacterDefinition>().Register(new EveCharacterDefinition(Hero, new[] {
            new EveInitialStat(PNContentModule.Health, 100d, 100d),
            new EveInitialStat(PNContentModule.AttackPower, 100d),
            new EveInitialStat(PNContentModule.Defense, 500d)
        }));
        hub.Seal();
        return hub;
    }

    private static void Begin(EveRegistryHub hub, EveSessionRole role)
    {
        EveRegistrySession.Begin();
        EveRegistrySession.Start(hub, role, EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:damage_flow"));
    }

    private static (EveTestLoader loader, PNStat stat, EveEntityIdentity identity) Entity(long id, PNStat stat = null)
    {
        var loader = new EveTestLoader();
        stat = stat ?? new PNUnprotectedDamageTestStat();
        var identity = new EveEntityIdentity();
        identity.Assign(new EveEntityId(id), Hero, 0);
        MonoBehaviour[] components = { loader, stat, identity };
        foreach (var component in components) component.Components = components;
        return (loader, stat, identity);
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < .001d;
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (ArgumentException) { Check(true, name); return; }
        catch (InvalidOperationException) { Check(true, name); return; }
        catch (KeyNotFoundException) { Check(true, name); return; }
        catch (NotSupportedException) { Check(true, name); return; }
        catch (OverflowException) { Check(true, name); return; }
        throw new Exception("Expected rejection: " + name);
    }
}
