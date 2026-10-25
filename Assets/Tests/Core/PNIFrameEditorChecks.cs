#if UNITY_EDITOR
using System;
using System.IO;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PNIFramePlayerProbe : PNPlayerStat
{
    public void TickGameClock() => Update();
}

// Real loaded receivers and selected registry actions, without changing the authored scene.
public static class PNIFrameEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped)
            throw new InvalidOperationException("Iframe checks require idle Edit Mode.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        float unityScale = Time.timeScale;
        var preview = EditorSceneManager.NewPreviewScene(); int checks = 0;
        bool started = false;
        void Check(bool ok, string message) { if(!ok) throw new Exception(message); checks++; }
        void Reject(Action action, string message) { bool rejected=false; try { action(); } catch(Exception) { rejected=true; } Check(rejected,message); }
        var eve = JObject.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"BaseContent/characters/EveCharacter.json")));
        bool checkedSweep=false;
        foreach(var entry in (JArray)eve["characters"][0]["skill"])
        {
            var action=entry["data"]?["action"] ?? entry["action"];
            if(action?["id"]?.Value<string>()!="base:eve_sweep_action") continue;
            var perform=(JObject)action["perform"].DeepClone();
            Check(EveSweepSettings.Parse(perform).IFrameDuration==null,"Authored Eve fallback changed.");
            perform["iframeDuration"]=-.2;
            Check(EveSweepSettings.Parse(perform).IFrameDuration==-.2,"Eve sweep must accept optional signed duration.");
            perform["iframeDuration"]="bad";
            Reject(()=>EveSweepSettings.Parse(perform),"Eve sweep malformed duration accepted.");
            checkedSweep=true;
            break;
        }
        Check(checkedSweep,"No authored Eve sweep found for parser verification.");
        var hero = EveId.Parse("test:iframe_native");
        var missing = EveId.Parse("test:iframe_missing"); var zero = EveId.Parse("test:iframe_zero");
        var negative = EveId.Parse("test:iframe_negative"); var breaker = EveId.Parse("test:iframe_breaker");
        var hub = new EveRegistryHub(); var module = new PNGameplayContentModule();
        new PNContentModule().Register(hub); module.Register(hub);
        void Register(EveId id, JToken duration)
        {
            var perform = JObject.Parse("{value:{amount:100},canCrit:false,tags:['base:true']}");
            if(duration!=null) perform["iframeDuration"]=duration;
            hub.GetOrCreate<EveSkillDefinition>().Register(new EveSkillDefinition(id,
                new JObject { ["action"]=new JObject { ["id"]="base:damage_action", ["perform"]=perform } }));
        }
        Register(missing,null); Register(zero,0); Register(negative,-.2); Register(breaker,2);
        hub.GetOrCreate<EveCharacterDefinition>().Register(new EveCharacterDefinition(hero,new[] {
            new EveInitialStat(PNContentIds.Health,10000d), new EveInitialStat(PNContentIds.AttackPower,100d),
            new EveInitialStat(PNContentIds.Defense,0d) },new[]{missing,zero,negative,breaker}));
        module.FinalizeContent(hub); hub.Seal(); module.ValidateContent(hub);
        try
        {
            EveRegistrySession.Begin(); started=true;
            EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,Array.Empty<EveContentDocument>(),"test:iframe_native"));
            PNStat Make(long id, bool player, bool skill)
            {
                var obj=new GameObject("Iframe native "+id); obj.SetActive(false); SceneManager.MoveGameObjectToScene(obj,preview);
                obj.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id),hero,0);
                PNStat stat=player?(PNStat)obj.AddComponent<PNIFramePlayerProbe>().AsPlayer():obj.AddComponent<PNEnemyStat>();
                obj.AddComponent<EveTimeScale>();
                if(skill) { obj.AddComponent<PNAbilityManager>(); obj.AddComponent<PNSkill>(); }
                obj.AddComponent<EveEntityLoader>().Load(hub); obj.SetActive(true); return stat;
            }
            var source=Make(992001,false,true); var secondSource=Make(992002,false,false);
            var enemy=Make(992003,false,false); var player=Make(992004,true,false);
            PNDamageResult Cast(EveId id, PNStat receiver, double? expected)
            {
                var skill=source.GetComponent<PNSkill>();
                using(var action=(PNDamageAction)skill.CreateAction(id,out var context,new[]{receiver}))
                {
                    action.Start(context); action.Perform(context);
                    Check(action.Damage.IFrameDuration==expected,"Selected registered data was not captured in damage.");
                    var hit=action.Results[0]; action.Exit(context); return hit;
                }
            }
            Check(Cast(missing,enemy,null).AppliedDamage==100 && Near(enemy.IFrameRemaining,.2),"Enemy fallback/full hit.");
            Check(enemy.ReceiveDamage(secondSource.CreateDamage(100,false,PNContentIds.True)).AppliedDamage==30 && enemy.IFrameRemaining==0,"Multiple sources do not share receiver state correctly.");
            Check(Cast(missing,player,null).AppliedDamage==100 && player.IFrameRemaining==1,"Player fallback/full hit.");
            player.AdvanceCombatTime(.4);
            Check(Cast(zero,player,0).AppliedDamage==30 && Near(player.IFrameRemaining,.6),"Explicit zero must preserve timer.");
            Check(Cast(negative,player,-.2).AppliedDamage==30 && Near(player.IFrameRemaining,.8),"Negative duration must extend timer.");
            Check(Cast(breaker,player,2).AppliedDamage==30 && player.IFrameRemaining==0,"Breaker itself remains reduced.");
            Check(Cast(zero,player,0).AppliedDamage==100 && player.IFrameRemaining==0,"Zero cannot create iframe.");
            Check(Cast(negative,player,-.2).AppliedDamage==100 && Near(player.IFrameRemaining,.2),"Negative inactive hit uses abs duration.");
            Check(enemy.IFrameRemaining==0,"Entity timer isolation.");
            var clock=player.GetComponent<EveTimeScale>(); clock.MinScale=0;
            clock.AddTimedModifier(EveId.Parse("test:iframe_stop"),EveTimeModifierKind.Scale,-1f,10f);
            Check(clock.Scale==0,"Fixture entity time stop failed.");
            player.AdvanceCombatTime(.1);
            Check(Near(player.IFrameRemaining,.1),"Entity stop must not stop Game iframe.");
            clock.MinScale=1;
            player.AdvanceCombatTime(0);
            Check(Near(player.IFrameRemaining,.1),"Pause must preserve timer regardless of entity minimum.");
            double before=player.IFrameRemaining, delta=Time.deltaTime;
            ((PNIFramePlayerProbe)player).TickGameClock();
            Check(Near(player.IFrameRemaining,Math.Max(0,before-delta)),"Actual Unity Update must use Time.deltaTime.");
            player.AdvanceCombatTime(10);
            Check(player.IFrameRemaining==0,"Natural expiration.");
            player.ReceiveDamage(source.CreateDamage(0,false,-.2,PNContentIds.True));
            Check(Near(player.IFrameRemaining,.2),"Zero damage retains legacy timer behavior.");
            var self=player.ReceiveDamage(player.CreateDamage(10,false,0d,PNContentIds.True));
            Check(self.AppliedDamage==3 && Near(player.IFrameRemaining,.2),"Self damage must apply iframe.");
            double hp=player.GetCurrent(PNContentIds.Health), timer=player.IFrameRemaining;
            Reject(()=>player.ReceiveDamage(new PNDamage(Guid.NewGuid(),source.EntityId,100,new[]{PNContentIds.True},-.2)),"Foreign session accepted.");
            Check(player.GetCurrent(PNContentIds.Health)==hp && player.IFrameRemaining==timer,"Rejected hit mutated HP/timer.");
            Reject(()=>PNDamage.ParseIFrameDuration(JObject.Parse("{iframeDuration:null}")),"Null duration accepted.");
            var loader=player.GetComponent<EveEntityLoader>(); loader.Unload(); loader.Load(hub);
            Check(player.IFrameRemaining==0,"Reload must restore empty iframe.");
        }
        finally
        {
            try { if(started) EveRegistrySession.End(); }
            finally { EditorSceneManager.ClosePreviewScene(preview); Time.timeScale=unityScale; }
        }
        Check(original.isDirty==dirty && SceneManager.GetActiveScene()==original,"Authored scene changed.");
        return "PASS iframe native "+checks+": registered damage, signed durations, zero/missing, two sources, Game clock, entity stop/minimum, lifecycle; scene dirty="+original.isDirty;
    }
    private static bool Near(double a,double b)=>Math.Abs(a-b)<1e-6;
}
#endif
