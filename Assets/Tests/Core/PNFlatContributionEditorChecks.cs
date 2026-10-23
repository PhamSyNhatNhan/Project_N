#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PNFlatNativeContent : EveIContentModule
{
    public void Register(EveRegistryHub hub)
    {
        var payload=JObject.Parse("{'duration':5,'clock':'Game','statId':'base:heal','value':10,'group':0,'maximumStacks':3,'calculationId':'base:mitigation','contributionId':'base:before'}");
        var parent=hub.Buffs().Get(EveId.Parse("base:buff_stat_template"));
        hub.Buffs().Register(parent.Derive(EveId.Parse("test:flat_buff"),payload,parent.Tags,false,1,PNBuffInstanceScope.Target,true));
    }
}
public static class PNFlatContributionEditorChecks
{
    public static string Run()
    {
        if(EditorApplication.isPlaying || EveRegistrySession.State!=EveSessionState.Stopped)throw new InvalidOperationException("Idle stopped Edit required");
        var original=SceneManager.GetActiveScene();bool dirty=original.isDirty;var preview=EditorSceneManager.NewPreviewScene();
        var evidence=new List<string>();int checks=0;
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;evidence.Add("PASS "+text);}
        bool Near(double a,double b)=>Math.Abs(a-b)<1e-8;
        PNStat Entity(EveRegistryHub hub,long id)
        {
            var root=new GameObject("Disposable flat contribution entity");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,preview);
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id),EveId.Parse("project_n:dummy"),0);
            var stat=root.AddComponent<PNEnemyStat>();root.AddComponent<EveEntityLoader>().Load(hub);root.SetActive(true);root.GetComponent<PNBuffManager>().Advance(0);return stat;
        }
        try
        {
            var docs=new PNGameContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),Path.Combine(Application.persistentDataPath,"Mods"),Array.Empty<string>()).ReadDocuments().ToArray();
            var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule(),new PNFlatNativeContent()},docs);
            EveRegistrySession.Begin();EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,docs,"test:flat_contribution"));
            var source=Entity(hub,998601);var target=Entity(hub,998602);var key=EveId.Parse("test:flat_modifier");
            foreach(var route in new[]{PNContentIds.Scale,PNContentIds.Mitigation})foreach(var tag in new[]{PNContentIds.Fire,PNContentIds.Heal})
            {
                source.SetSource(key,new[]{new EveStatModifier(tag,10,0,route,PNContentIds.Before),new EveStatModifier(tag,20,0,route),new EveStatModifier(tag,.5,1,route)});
                Check(Near(source.Calculate(route,100,tag),185),"Native before/after grouping "+route+" "+tag);
            }
            source.RemoveSource(key);
            source.SetSource(key,new[]{new EveStatModifier(PNContentIds.Heal,10,0,PNContentIds.Scale,PNContentIds.Before),new EveStatModifier(PNContentIds.Heal,20,0),new EveStatModifier(PNContentIds.Heal,.5,1)});
            target.SetSource(key,new[]{new EveStatModifier(PNContentIds.Heal,30,0,PNContentIds.Mitigation,PNContentIds.Before),new EveStatModifier(PNContentIds.Heal,10,0,PNContentIds.Mitigation),new EveStatModifier(PNContentIds.Heal,-.5,1,PNContentIds.Mitigation)});
            target.SetCurrent(PNContentIds.Health,10);var heal=source.CreateHeal(100);var result=target.ReceiveHeal(heal);
            Check(Near(heal.Amount,185) && Near(result.CalculatedHeal,117.5),"Native outgoing and incoming Heal flats");
            target.SetCurrent(PNContentIds.Health,target.GetMax(PNContentIds.Health)-20);
            var capped=target.ReceiveHeal(heal);
            Check(Near(capped.AppliedHeal,20) && target.GetCurrent(PNContentIds.Health)==target.GetMax(PNContentIds.Health),"Heal still caps to missing HP");
            var dead=Entity(hub,998603);dead.SetCurrent(PNContentIds.Health,0);
            Check(dead.ReceiveHeal(heal).Status==PNHealStatus.DeadTarget,"Flat Heal does not revive dead target");
            dead.GetComponent<EveEntityLoader>().Unload();
            target.SetCurrent(PNContentIds.Health,10);target.SetSource(key,new[]{new EveStatModifier(PNContentIds.Heal,20,0,PNContentIds.Mitigation)});
            source.RemoveSource(key);Check(target.ReceiveHeal(source.CreateHeal(0)).AppliedHeal==20,"Zero incoming Heal can receive after flat");
            source.SetSource(key,new[]{new EveStatModifier(PNContentIds.Fire,10,0,PNContentIds.Scale,PNContentIds.Before),new EveStatModifier(PNContentIds.Fire,.5,1)});
            var snapshot=source.CaptureCombatSource();source.RemoveSource(key);
            Check(Near(snapshot.CreateDamage(100,false,0,PNContentIds.True,PNContentIds.Fire).Amount,165),"Combat snapshot retains contribution ID after source removal");
            target.RemoveSource(key);target.SetCurrent(PNContentIds.Health,100);
            target.SetSource(key,new[]{new EveStatModifier(PNContentIds.Fire,-10,0,PNContentIds.Mitigation),new EveStatModifier(PNContentIds.Fire,-.5,1,PNContentIds.Mitigation)});
            var damage=target.ReceiveDamage(source.CreateDamage(100,false,PNContentIds.True,PNContentIds.Fire));
            Check(Near(damage.CalculatedDamage,40),"Incoming damage Mitigation default after flat");
            target.AdvanceCombatTime(1);target.SetSource(key,new[]{new EveStatModifier(PNContentIds.Fire,20,0,PNContentIds.Mitigation),new EveStatModifier(PNContentIds.Fire,-1,1,PNContentIds.Mitigation)});
            Check(Near(target.ReceiveDamage(source.CreateDamage(100,false,PNContentIds.True,PNContentIds.Fire)).CalculatedDamage,20),"Incoming after flat survives zero percentage factor");
            target.AdvanceCombatTime(1);
            Check(Near(target.ReceiveDamage(source.CreateDamage(0,false,PNContentIds.True,PNContentIds.Fire)).CalculatedDamage,20),"Zero incoming damage can receive after flat");
            source.SetPersistentSource(key,new[]{new EveStatModifier(PNContentIds.Fire,10,0,PNContentIds.Scale,PNContentIds.Before)});
            var floor=source.EndFloorAndCaptureSnapshot();Check(floor.PersistentSources[key][0].ContributionId==PNContentIds.Before,"Floor persistent snapshot preserves contribution ID");
            var payload=JObject.Parse("{'duration':5,'clock':'Game','statId':'base:heal','value':10,'group':0,'maximumStacks':3,'calculationId':'base:mitigation','contributionId':'base:before'}");
            PNBuffBuiltinSettings.Validate("buff_stat_template",payload,hub);
            var definition=hub.Buffs().Get(EveId.Parse("test:flat_buff"));
            // Parser roundtrip and profile fingerprint, without registering definitions after seal.
            var modifier=PNStatModifierData.Parse((JObject)definition.Payload);Check(modifier.ContributionId==EveId.Parse("base:before"),"Registered Buff payload parses routing IDs");
            target.SetSource(key,new[]{new EveStatModifier(PNContentIds.Heal,-.5,1,PNContentIds.Mitigation)});
            var buffs=target.GetComponent<PNBuffManager>().Buffs;
            var receipt=buffs.Apply(new PNBuffApplyRequest(definition.Id,PNBuffSource.Capture(source)));
            var buffResult=target.ReceiveHeal(source.CreateHeal(100));
            Check(Near(buffResult.CalculatedHeal,55),"Registered Buff applies before Mitigation to Heal: status="+receipt.Status+";reason="+receipt.Message+";actual="+buffResult.CalculatedHeal);
            buffs.Remove(receipt.Handle);Check(Near(target.Calculate(PNContentIds.Mitigation,100,PNContentIds.Heal),50),"Buff removal cleans routed flat source");
            source.GetComponent<EveEntityLoader>().Unload();target.GetComponent<EveEntityLoader>().Unload();
        }
        finally {if(EveRegistrySession.State!=EveSessionState.Stopped)EveRegistrySession.End();EditorSceneManager.ClosePreviewScene(preview);}
        Check(SceneManager.GetActiveScene()==original && original.isDirty==dirty,"Authored scene preserved");
        Directory.CreateDirectory(".utmp/FlatContributionReview");File.WriteAllLines(".utmp/FlatContributionReview/NativeFlat.txt",evidence);
        return "PASS "+checks+" native flat contribution checks";
    }
}
#endif
