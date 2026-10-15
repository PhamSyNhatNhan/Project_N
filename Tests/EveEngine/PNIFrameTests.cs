using System;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static EveSessionSmokeTests;

// Older calculation suites explicitly opt out; iframe behavior is tested below with real defaults.
public class PNUnprotectedDamageTestStat : PNStat
{
    public override double DefaultIFrameDuration => 0d;
}

public static class PNIFrameTests
{
    public static void Run()
    {
        Check(PNDamage.ParseIFrameDuration(JObject.Parse("{}")) == null, "iframe omission preserves receiver fallback");
        Check(PNDamage.ParseIFrameDuration(JObject.Parse("{iframeDuration:0}")) == 0, "explicit zero differs from omission");
        Check(PNDamage.ParseIFrameDuration(JObject.Parse("{iframeDuration:-0.2}")) == -.2, "negative iframe content accepted");
        Reject(() => PNDamage.ParseIFrameDuration(JObject.Parse("{iframeDuration:null}")), "null iframe is malformed, not omission");
        Reject(() => PNDamage.ParseIFrameDuration(JObject.Parse("{iframeDuration:'0.2'}")), "string iframe rejected");
        Reject(() => PNDamage.ParseIFrameDuration(new JObject { ["iframeDuration"] = double.PositiveInfinity }), "infinite iframe rejected");
        var slash = JObject.Parse("{value:{amount:100},hitbox:{},iframeDuration:-0.2}");
        Check(PNSlashHitSettings.Parse(slash).IFrameDuration == -.2, "slash registered perform parses signed iframe");
        slash["iframeDuration"] = "bad";
        Reject(() => PNSlashHitSettings.Parse(slash), "slash rejects malformed iframe before execution");
        var invalidHub = new EveRegistryHub(); new PNContentModule().Register(invalidHub);
        var gameplay = new PNGameplayContentModule(); gameplay.Register(invalidHub);
        invalidHub.GetOrCreate<EveSkillDefinition>().Register(new EveSkillDefinition(EveId.Parse("test:bad_iframe"),
            JObject.Parse("{action:{id:'base:damage_action',perform:{value:{amount:1},tags:['base:true'],iframeDuration:'bad'}}}")));
        invalidHub.Seal();
        Reject(() => gameplay.ValidateContent(invalidHub), "registered damage malformed iframe fails content validation");
        var hero = EveId.Parse("test:iframe_hero");
        var hub = new EveRegistryHub(); new PNContentModule().Register(hub);
        hub.GetOrCreate<EveCharacterDefinition>().Register(new EveCharacterDefinition(hero, new[] {
            new EveInitialStat(PNContentIds.Health, 10000d), new EveInitialStat(PNContentIds.AttackPower, 100d),
            new EveInitialStat(PNContentIds.Defense, 0d) })); hub.Seal();
        EveRegistrySession.Begin();
        EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:iframe"));
        var source = Make(new PNStat(), 991001); var other = Make(new PNStat(), 991002);
        var enemy = Make(new PNEnemyStat(), 991003); var player = Make(new PNPlayerStat(), 991004);
        try
        {
            foreach (var entity in new[] { source, other, enemy, player }) entity.loader.Load(hub);
            PNDamage Packet(double? duration) => source.stat.CreateDamage(100, false, duration, PNContentIds.True);
            var missing = Packet(null);
            var first = enemy.stat.ReceiveDamage(missing);
            Check(first.AppliedDamage == 100 && Near(enemy.stat.IFrameRemaining, .2), "enemy first hit full and fallback 0.2");
            var second = enemy.stat.ReceiveDamage(other.stat.CreateDamage(100, false, PNContentIds.True));
            Check(second.AppliedDamage == 30 && second.IFrameMultiplier == .3 && enemy.stat.IFrameRemaining == 0,
                "second source shares receiver timer; breaking hit remains reduced");
            Check(enemy.stat.ReceiveDamage(missing).AppliedDamage == 100, "following hit full after iframe broken");
            enemy.stat.AdvanceCombatTime(1);
            var initial = player.stat.ReceiveDamage(missing);
            Check(initial.AppliedDamage == 100 && player.stat.IFrameRemaining == 1, "player missing duration defaults 1 second");
            player.stat.AdvanceCombatTime(.4);
            Check(Near(player.stat.IFrameRemaining, .6), "Game delta advances passive timer");
            var zero = player.stat.ReceiveDamage(Packet(0));
            Check(zero.AppliedDamage == 30 && Near(player.stat.IFrameRemaining, .6), "explicit zero hit reduced without consuming timer");
            var extend = player.stat.ReceiveDamage(Packet(-.2));
            Check(extend.AppliedDamage == 30 && Near(player.stat.IFrameRemaining, .8), "negative duration extends existing timer");
            player.stat.AdvanceCombatTime(0);
            Check(Near(player.stat.IFrameRemaining, .8), "Unity pause zero delta preserves iframe");
            var breaking = player.stat.ReceiveDamage(Packet(2));
            Check(breaking.AppliedDamage == 30 && player.stat.IFrameRemaining == 0, "positive nuke duration clamps timer at zero");
            Check(player.stat.ReceiveDamage(Packet(0)).AppliedDamage == 100 && player.stat.IFrameRemaining == 0,
                "explicit zero cannot create iframe");
            Check(player.stat.ReceiveDamage(Packet(-.2)).AppliedDamage == 100 && Near(player.stat.IFrameRemaining, .2),
                "negative duration creates absolute timer when inactive");
            Check(enemy.stat.IFrameRemaining == 0, "receiver iframe is entity-local");
            player.stat.AdvanceCombatTime(10);
            Check(player.stat.IFrameRemaining == 0, "large Game delta expires iframe");
            player.stat.ReceiveDamage(source.stat.CreateDamage(0, false, -.2, PNContentIds.True));
            Check(Near(player.stat.IFrameRemaining, .2), "zero damage retains original signed timer rules");
            var self = player.stat.ReceiveDamage(player.stat.CreateDamage(10, false, 0d, PNContentIds.True));
            Check(self.AppliedDamage == 3 && Near(player.stat.IFrameRemaining, .2), "self damage uses same iframe rules");
            double hp = player.stat.GetCurrent(PNContentIds.Health), time = player.stat.IFrameRemaining;
            Reject(() => player.stat.ReceiveDamage(new PNDamage(Guid.NewGuid(), source.stat.EntityId, 100, new[] {PNContentIds.True}, -.2)),
                "foreign session cannot touch timer");
            Reject(() => Packet(double.NaN), "API NaN duration rejected");
            Check(player.stat.GetCurrent(PNContentIds.Health) == hp && player.stat.IFrameRemaining == time, "rejected payload is atomic for HP and iframe");
            source.loader.Unload();
            Check(player.stat.ReceiveDamage(missing).AppliedDamage == 30, "delayed snapshot retains fallback after source unload");
            player.stat.ReceiveDamage(new PNDamage(player.stat.SessionId, other.stat.EntityId, 0, new[] {PNContentIds.True}, -.2));
            player.loader.Unload(); player.loader.Load(hub);
            Check(player.stat.IFrameRemaining == 0, "unload/reload recreates clean passive iframe state");
            player.stat.ReceiveDamage(new PNDamage(player.stat.SessionId,other.stat.EntityId,0,new[]{PNContentIds.True},-double.MaxValue));
            double overflowTime=player.stat.IFrameRemaining, overflowHP=player.stat.GetCurrent(PNContentIds.Health);
            Reject(()=>player.stat.ReceiveDamage(new PNDamage(player.stat.SessionId,other.stat.EntityId,1,new[]{PNContentIds.True},-double.MaxValue)),
                "signed timer accumulation rejects overflow before commit");
            Check(player.stat.IFrameRemaining==overflowTime && player.stat.GetCurrent(PNContentIds.Health)==overflowHP,
                "timer overflow preserves HP and prior time");
            player.stat.AdvanceCombatTime(double.MaxValue);
            player.stat.ReceiveDamage(new PNDamage(player.stat.SessionId,other.stat.EntityId,100000,new[]{PNContentIds.True},-.2));
            Check(player.stat.IsDead && Near(player.stat.IFrameRemaining,.2),"death retains iframe per original rule");
            player.stat.ReceiveDamage(new PNDamage(player.stat.SessionId,other.stat.EntityId,0,new[]{PNContentIds.True},-.2));
            Check(Near(player.stat.IFrameRemaining,.2),"dead receiver does not mutate iframe on another hit");
            var overrideSender = new PNOverrideDamageStat { OutgoingFactor = 2 };
            var overridden = Make(overrideSender,991005); overridden.loader.Load(hub);
            try
            {
                var selected = overrideSender.CreateDamage(10,false,-.2,PNContentIds.True);
                Check(selected.Amount == 20 && selected.IFrameDuration == -.2,
                    "iframe overload preserves selected outgoing implementation and signed data");
            }
            finally { overridden.loader.Unload(); }
        }
        finally { source.loader.Unload(); other.loader.Unload(); enemy.loader.Unload(); player.loader.Unload(); EveRegistrySession.End(); }

        (EveTestLoader loader, PNStat stat) Make(PNStat stat, long id)
        {
            var loader = new EveTestLoader(); var identity = new EveEntityIdentity();
            identity.Assign(new EveEntityId(id), hero, 0);
            MonoBehaviour[] components = {loader, identity, stat};
            foreach(var component in components) { component.Components = components; component.gameObject.SetActive(true); }
            return (loader, stat);
        }
    }
    private static bool Near(double a, double b) => Math.Abs(a-b) < 1e-9;
    private static void Reject(Action action, string name)
    { bool rejected = false; try { action(); } catch (Exception) { rejected = true; } Check(rejected, name); }
}
