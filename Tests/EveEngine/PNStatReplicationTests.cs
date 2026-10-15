using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using UnityEngine;
using static EveSessionSmokeTests;

public static class PNStatReplicationTests
{
    private static readonly EveId Hero = EveId.Parse("test:stat_replica");
    private static readonly EveId MaxSource = EveId.Parse("test:stat_max");
    private static EveRegistryHub Hub(bool seal = true)
    {
        var hub = new EveRegistryHub(); new PNContentModule().Register(hub); new PNGameplayContentModule().Register(hub);
        hub.GetOrCreate<EveCharacterDefinition>().Register(new EveCharacterDefinition(Hero, new[] {
            new EveInitialStat(PNContentIds.Health,100d,100d), new EveInitialStat(PNContentIds.Defense,0d) }));
        if(seal) hub.Seal(); return hub;
    }
    private static void Begin(EveRegistryHub hub, EveSessionRole role)
    { EveRegistrySession.Begin(); EveRegistrySession.Start(hub,role,EveContentCompatibility.Create(hub,Array.Empty<EveContentDocument>(),"test:stat1")); }
    private static EveTestLoader Entity(bool prepare = false)
    {
        var loader = new EveTestLoader(); var id = new EveEntityIdentity(); id.Assign(new EveEntityId(77301),Hero,7);
        MonoBehaviour[] components = {loader,id,new PNStat(),new PNControlManager(),new PNBuffManager(),new PNStatReplication()};
        foreach(var c in components) { c.Components=components; c.gameObject.SetActive(true); }
        if(prepare) loader.Prepare(EveRegistrySession.Current); else loader.Load(EveRegistrySession.Current); return loader;
    }
    public static void Run()
    {
        var health=PNContentIds.Health;
        Check(typeof(EveStatDefinition).GetConstructor(new[]{typeof(EveId),typeof(EveStatKind),typeof(EveMaxChangePolicy)})!=null,"existing Stat definition constructor retained");
        EveContentDocument Doc(string policy) => new EveContentDocument("stat.json",
            "{\"schemaVersion\":1,\"statDefinitions\":[{\"id\":\"addon:resource\",\"kind\":\"resource\",\"replication\":\""+policy+"\"}]}","addon");
        var parsed = Hub(false);new EveJsonContentLoader().Register(new[]{Doc("everyone")},parsed);parsed.Seal();
        Check(parsed.Stats.Get(EveId.Parse("addon:resource")).Replication==EveStatReplication.Everyone,"JSON replication parsed");
        Reject(()=>new EveJsonContentLoader().Register(new[]{Doc("owner")},Hub(false)),"JSON owner unsupported rejected");
        Reject(()=>new EveJsonContentLoader().Register(new[]{Doc("typo")},Hub(false)),"unknown JSON replication rejected");
        var overridden = Hub(false);var replacement=EveId.Parse("addon:private_health");
        overridden.Stats.Register(new EveStatDefinition(replacement,EveStatKind.Resource));
        overridden.Stats.RequestOverride(EveId.Parse("base:health"),replacement,"addon","1.0.0",new[]{"project_n"});overridden.Seal();
        Begin(overridden,EveSessionRole.Host);
        try { var local=Entity();var snapshot=PNStatCodec.Decode(local.GetComponent<PNStatReplication>().CaptureReplication());
            Check(snapshot.Resources.Count==0,"replica policy follows selected None override instead of original Health Everyone"); }
        finally { EveRegistrySession.End(); }

        Reject(()=>new EveStatDefinition(health,EveStatKind.Resource,replication:EveStatReplication.Owner),"owner replication explicitly rejected");
        Reject(()=>new EveStatDefinition(health,EveStatKind.Number,replication:EveStatReplication.Everyone),"Number replication rejected");
        var values=new Dictionary<EveId,PNResourceValue>{{health,new PNResourceValue(23.125d,120d)}};
        var original=new PNStatFrame(1,1,false,values);values.Clear();
        Check(PNStatCodec.Decode(PNStatCodec.Encode(original)).Resources[health].Current==23.125d,"codec immutable exact double roundtrip");
        var bad=PNStatCodec.Encode(original).Concat(new byte[]{0}).ToArray();
        Reject(()=>PNStatCodec.Decode(bad),"trailing bytes rejected");
        Reject(()=>PNStatCodec.Decode(new byte[19]),"truncated Stat payload rejected");
        Reject(()=>new PNResourceValue(double.NaN,100),"NaN resource rejected");
        Reject(()=>new PNResourceValue(101,100),"resource exceeds max rejected");
        byte[] baseline=null, changed=null, dead=null; Guid host=Guid.Empty; long generation=0;
        var hub=Hub();
        Begin(hub,EveSessionRole.Host);
        try
        {
            var loader=Entity();var stat=loader.GetComponent<PNStat>();var part=loader.GetComponent<PNStatReplication>();
            generation=loader.Generation;host=EveRegistrySession.Context.SessionId;
            Check(hub.Stats.Get(health).Replication==EveStatReplication.Everyone,"Health selected policy everyone");
            var emitted=new List<byte[]>(); part.ReplicationDelta+=emitted.Add;
            baseline=part.CaptureReplication(); var frozen=part.FreezeReplication();
            stat.SetCurrent(health,64);part.CaptureReplication();part.Advance(.05);
            Check(emitted.Count==1 && PNStatCodec.Decode(emitted[0]).Resources[health].Current==64,"direct HP change publishes at sample");
            changed=emitted[0];part.Advance(.05);
            Check(emitted.Count==1,"unchanged sample publishes nothing");
            Check(PNStatCodec.Decode(frozen()).Resources[health].Current==100,"freeze is coherent immutable snapshot");
            stat.SetSource(MaxSource,new[]{new EveStatModifier(health,100d,0)});part.Advance(.05);
            var expanded=PNStatCodec.Decode(emitted.Last());
            Check(expanded.Resources[health].Max==200 && expanded.Resources[health].Current==128,"max change PreserveRatio pair together");
            stat.SetSource(MaxSource,new[]{new EveStatModifier(health,-100d,0)});part.Advance(.05);
            dead=emitted.Last();var death=PNStatCodec.Decode(dead);
            Check(death.Dead && death.LifeRevision==2,"non-combat max-zero sampled death version");
            stat.RemoveSource(MaxSource);part.Advance(.05);
            Check(PNStatCodec.Decode(emitted.Last()).LifeRevision==3 && !part.AuthoritativeState.Dead,"max-zero recovery produces newer alive version");
            int n=emitted.Count;
            loader.GetComponent<PNBuffManager>().Buffs.Coordinator.Submit(()=>stat.ReceiveDamage(new PNDamage(host,stat.EntityId,1000d,new[]{PNContentIds.Physical})));
            Check(emitted.Count==n+1 && part.AuthoritativeState.Dead,"combat death publishes immediately without sample");
            long life=part.AuthoritativeState.LifeRevision;part.Advance(.05);
            Check(part.AuthoritativeState.LifeRevision==life && emitted.Count==n+2,"Died plus sample no double life transition");
        }
        finally { EveRegistrySession.End(); }
        Begin(hub,EveSessionRole.Client);
        try
        {
            EveRegistrySession.Context.Coop.AdmitHost(host,EveRegistrySession.Context.Compatibility,7);
            Check(EveRegistrySession.Context.LocalOwnerId==7,"trusted admission sets local owner identity");
            Reject(()=>EveRegistrySession.Context.Coop.AdmitHost(host,EveRegistrySession.Context.Compatibility,8),"duplicate admission cannot change owner");
            var loader=Entity(true);var stat=loader.GetComponent<PNStat>();var part=loader.GetComponent<PNStatReplication>();
            Check(!PNVitals.TryRead(stat,health,out _,out _,out _),"Unknown before baseline/Ready");
            var aborted=part.StageReplication(baseline,host,generation);aborted.Dispose();
            Reject(()=>aborted.Commit(),"disposed baseline cannot commit");
            var stage=part.StageReplication(baseline,host,generation);
            EveEntityLoader.CommitBatch(new[]{loader},()=>stage.Commit());stage.Published();stage.Dispose();
            Check(PNVitals.TryRead(stat,health,out var hp,out _,out _) && hp==100,"staged baseline before publish");
            part.ApplyReplicationDelta(changed,host,generation);
            Check(PNVitals.TryRead(stat,health,out hp,out _,out _) && hp==64 && stat.GetCurrent(health)==100,"client view independent of initialized PNStat");
            Reject(()=>stat.SetCurrent(health,5),"client mutation remains denied");
            part.ApplyReplicationDelta(baseline,host,generation);
            Check(part.View.Resources[health].Current==64,"stale state ignored");
            Reject(()=>part.ApplyReplicationDelta(changed,Guid.NewGuid(),generation),"host identity mismatch rejected");
            Reject(()=>part.ApplyReplicationDelta(changed,host,generation+1),"generation mismatch rejected");
            int notices=0;stat.Died+=_=>notices++;part.ApplyReplicationDelta(dead,host,generation);
            Check(part.View.Dead && notices==0 && !stat.IsDead,"replicated death presentation only");
            var conflict=new PNStatFrame(part.View.Revision+1,part.View.LifeRevision,false,new Dictionary<EveId,PNResourceValue>(part.View.Resources));
            Reject(()=>part.ApplyReplicationDelta(PNStatCodec.Encode(conflict),host,generation),"same life version conflict rejected atomically");
            Check(part.View.Dead,"invalid frame preserves valid view");
            var staleStage=part.StageReplication(dead,host,generation);loader.Unload();
            Reject(()=>staleStage.Commit(),"unloaded actor cannot commit stale baseline");staleStage.Dispose();Check(part.View==null,"unload clears client view");
        }
        finally { EveRegistrySession.End(); }
    }
}
