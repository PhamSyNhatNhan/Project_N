#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using ProjectN.Coop.Tests;
public sealed class PNActionA2Probe : PNSkillAction {
 public double Progress;
 public override void Start(PNSkillActionContext c) { c.BeginAction(); }
 public override void Perform(PNSkillActionContext c) { }
 public void Tick(float step) { Progress += step; }
 public override void Exit(PNSkillActionContext c) { c.CompleteAction(); }
 public override void Cancel(PNSkillActionContext c) { c.CancelEffects(); }
}
public sealed class PNActionA2Module : PNGameplayContentModule {
 public override void Register(EveRegistryHub hub) { base.Register(hub); hub.Actions().Register(new PNSkillActionDefinition(EveId.Parse("test:a2_action"), typeof(PNActionA2Probe)).ReplayOnObservers().WithDriverProgress()); }
}
public static class PNActionA2NativeChecks {
 public static string Boundaries() {
  int checks=0;
  void Check(bool ok,string message){if(!ok)throw new Exception("C0 "+message);checks++;}
  var ctor=typeof(PNSkill.PNActionExecutionFrame).GetConstructors(BindingFlags.NonPublic|BindingFlags.Instance).Single(c=>c.GetParameters().Length==5);
  var stampProperty=typeof(PNSkill.PNActionExecutionFrame).GetProperty("Stamp",BindingFlags.NonPublic|BindingFlags.Instance);
  var go=new GameObject("C0 boundary fixture");
  try {
   var skill=go.AddComponent<EveClientRoleProbeSkill>();
   foreach(double origin in new[]{0d,1d,1000000d}) foreach(double speed in new[]{.125,1,8}) foreach(bool floats in new[]{false,true}) {
    double dt=floats?(double)(float).017:.017, end=origin+dt;
    using(var frame=(PNSkill.PNActionExecutionFrame)ctor.Invoke(new object[]{skill,dt,dt,end,origin})) {
     double callback=0;float first=(float)(dt*speed*.3);frame.Advance(null,first,speed,x=>callback+=x);
     double mid=(double)stampProperty.GetValue(frame);
     Check(mid>origin && mid<end,"partial phase remains partial");
     float last=(float)((dt-first/speed)*speed);frame.Advance(null,last,speed,x=>callback+=x);
     Check((double)stampProperty.GetValue(frame)==end,"float remaining budget maps exact tail");
     Check(callback==(double)first+last,"callback steps untouched");
    }
   }
   using(var budget=new PNActionRunBudget()) using(var store=new PNActionRunStore(budget)) {
    var key=new PNActionRunKey(1,1);store.Admit(key,"test:a","test:b","test:c",1,0,1);
    store.AdvanceInterval(key,1,1.25,.25,1);store.CaptureHolds(1.5);
    var held=store.Snapshot(1.5).Single();Check(held.Progress==.25 && held.Rate==0,"partial frame tail holds");
    long rev=held.Revision;store.CaptureHolds(1.5);store.Snapshot(1.5);Check(store.Snapshot(1.5).Single().Revision==rev,"seal/read idempotent");
    store.AdvanceInterval(key,1.5,1.625,1,8);store.CaptureHolds(1.75);
    Check(store.Snapshot(1.75).Single().Progress==1.25,"hold resume eight zero");
    store.Finish(key,1.75,PNActionRunEnd.Cancelled);int charged=budget.Used;
    store.Finish(key,1.75,PNActionRunEnd.Cancelled);Check(charged==budget.Used,"duplicate terminal accounting");
    store.Prune(4.75);Check(budget.Used==0,"terminal retirement");
   }
   foreach(double origin in new[]{1d,1000000d}) using(var budget=new PNActionRunBudget()) using(var store=new PNActionRunStore(budget)) {
    var key=new PNActionRunKey(1,1);store.Admit(key,"test:a","test:b","test:c",1,0,origin);
    double now=origin,expected=0;
    for(int i=0;i<10000;i++) {
     float dt=i%3==0?.007f:i%3==1?.016f:.033f;double end=now+dt;
     store.AdvanceInterval(key,now,end,dt,1);expected+=dt;store.CaptureHolds(end);
     var row=store.Snapshot(end).Single();
     if(Math.Abs(row.Progress-expected)>1e-5 || row.History.Any(a=>a.Rate==0 && a.Stamp!=origin))throw new Exception("Variable-cadence drift/false hold");
     now=end;
    }
    Check(store.HistoryCuts==0,"long variable float cadence without false cuts");
    store.Finish(key,now,PNActionRunEnd.Completed);Check(store.Snapshot(now).Single().Terminal==now,"exact completion stamp");
   }
   return "PASS "+checks+" C0 boundary checks (18 frame cases + hold/lifecycle + 2x10000 variable-cadence samples)";
  } finally {UnityEngine.Object.DestroyImmediate(go);}
 }
 public static string Verify(string folder) {
  using(var rec=new PNActionTimePlayChecks.A2Recorder(Path.Combine(folder,"cap-oracle.jsonl"),false,0))
  using(var b=new PNActionRunBudget())using(var s=new PNActionRunStore(b)) {
   s.Admit(new PNActionRunKey(1,1),"test:a","test:b","test:c",1,0,1);if(rec.Errors!=1)throw new Exception("Cap oracle failed");
  }
  using(var b=new PNActionRunBudget())using(var s=new PNActionRunStore(b)) {
   var key=new PNActionRunKey(1,1);s.Admit(key,"test:a","test:b","test:c",1,0,1);
   for(int i=1;i<=100;i++)s.AdvanceInterval(key,1+(i-1)*.01,1+i*.01,(float).01,1);
   var row=s.Snapshot(2).Single();File.WriteAllText(Path.Combine(folder,"constant-rate-repro.json"),PNActionRunWire.Row(row).ToString());
   if(row.Cut!=0 || row.History.Any(a=>a.Rate==0 && a.Stamp!=1))throw new Exception("False constant-rate boundary");
   return "PASS cap oracle; explicit constant-rate100steps cuts="+row.Cut+" anchors="+row.History.Count;
  }
 }
 public static string Run(string folder, int entities=1, int seconds=60, bool live=false, bool stagger=false, int density=0) {
  if(EditorApplication.isPlaying || EveRegistrySession.State!=EveSessionState.Stopped) throw new Exception("Idle editor required");
  Directory.CreateDirectory(folder);
  File.WriteAllText(Path.Combine(folder,"manifest.json"),new JObject { ["entities"]=entities,["seconds"]=seconds,["live"]=live,["stagger"]=stagger,["density"]=density,["dt"]=.01,["driver"]="injected unscaled frame; native registered inert Action",["action"]="test:a2_action",["productionCoreAssembly"]=typeof(PNActionRunStore).Assembly.Location }.ToString());
  var docs=new PNGameContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),Path.Combine(Application.persistentDataPath,"Mods"),Array.Empty<string>()).ReadDocuments().ToArray();
  var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNActionA2Module()},docs);
  var scene=EditorSceneManager.NewPreviewScene(); double now=1; int starts=0,peak=0; var skills=new List<EveClientRoleProbeSkill>(); var services=new List<PNSyncService>();
  var running=new List<(int owner, PNActionA2Probe action,PNSkillActionContext context,double end)>();
  using(var recorder=new PNActionTimePlayChecks.A2Recorder(Path.Combine(folder,"native-a2.jsonl")))
  try {
   EveRegistrySession.Begin(); EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,docs,"test:a2"));
   PNTimeService.Attach(EveRegistrySession.Context,new PNLocalTimeline(Guid.NewGuid(),()=>now));
   var area=new GameObject("A2 areas");SceneManager.MoveGameObjectToScene(area,scene);area.AddComponent<PNAreaManager>().Initialize(EveRegistrySession.Context);
   for(int i=0;i<entities;i++) {
    var go=new GameObject("A2 entity "+i); go.SetActive(false);SceneManager.MoveGameObjectToScene(go,scene);go.transform.position=new Vector3(i*4,0,0);go.layer=PNLayers.Player;
    go.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(996000+i),EveId.Parse("project_n:eve"),5);
    go.AddComponent<EveCharacterStat>();go.AddComponent<PNAbilityManager>();go.AddComponent<Rigidbody>().useGravity=false;go.AddComponent<CapsuleCollider>();go.AddComponent<PNPlayerMove>();
    var skill=go.AddComponent<EveClientRoleProbeSkill>();skill.Prepare();go.AddComponent<EveEntityLoader>().Load(hub);go.SetActive(true);go.GetComponent<PNAbilityManager>().Abilities.Advance(0);skills.Add(skill);
    var service=new PNSyncService(hub,true,0,EveRegistrySession.Context);services.Add(service);var target=PNSyncTarget.ForEntity(new EveEntityId(996000+i),1);service.RegisterObject(target,skill);
    go.AddComponent<PNSkillOwnerView>().OnBound(service,target);go.AddComponent<PNSkillRemoteInput>().OnBound(service,target);skill.Bindings.TryGet(PNSkillSlots.Attack,out var profile);
    foreach(int p in new[]{0,1}) service.ReceiveCommand(5,target,PNSkillRemoteInput.Command,new JObject{["k"]=p+1,["s"]=PNSkillSlots.Attack.ToString(),["p"]=p,["i"]=0,["f"]=profile.ToString()});
    if(skill.CurrentInvocation==null)throw new Exception("No invocation");
   }
   var ctor=typeof(PNSkill.PNActionExecutionFrame).GetConstructors(BindingFlags.NonPublic|BindingFlags.Instance).Single(c=>c.GetParameters().Length==5);
   var data=new PNSkillData(new JObject(),new PNSkillActionData(EveId.Parse("test:a2_action")));
   int lastTick=(live||density>0)?seconds*100:seconds*100+100;
   for(int tick=0;tick<=lastTick;tick++) {
    now=1+tick*.01;
    foreach(var item in running.Where(x=>x.end<=now).ToArray()){if(Math.Abs(item.action.Progress-recorder.ProgressFor(skills[item.owner].ActionRuns,new PNActionRunKey(item.context.Invocation.CastKey,item.context.ActionOrdinal),now))>1e-5)throw new Exception("Actual Action progress oracle");item.action.Exit(item.context);item.action.Dispose();running.Remove(item);}
    if(tick<(live||density>0?100:seconds*100)) for(int i=0;i<entities;i++) { if((tick+(stagger?i:0))%25!=0)continue;
     var skill=skills[i];var context=new PNSkillActionContext(skill,EveId.Parse("project_n:eve_attack_right"),data,hub,null,Vector3.right,skill.CurrentInvocation);
     var action=(PNActionA2Probe)hub.Actions().Get(data.Action.Id).Create();action.Start(context);action.Perform(context);running.Add((i,action,context,live||density>0?seconds+2:1+(tick+100)*.01));starts++;
    }
    peak=Math.Max(peak,running.Count);
    if(tick==lastTick)break;
    // Start/retire at the interval boundary, then advance the following interval.
    // The old fixture started at end and backdated its first step into the past.
    double begin=now; now=1+(tick+1)*.01;
    foreach(var item in running) {
     double speed=live?new[]{1d,2,0,8,0,1}[(tick/200)%6]:1;
     if(live && tick>=1800 && tick<2000)speed=0;
     if(density>0)speed=((int)(tick*.01*density)%2)==0?1:8;
     using(var frame=(PNSkill.PNActionExecutionFrame)ctor.Invoke(new object[]{skills[item.owner],.01,.01,now,begin})) {
      if(speed==0)frame.Hold(item.context);else frame.Advance(item.context,(float)(.01*speed),speed,item.action.Tick);
     }
    }
    foreach(var skill in skills)skill.SealActionRuns(now);
    foreach(var item in running)if(Math.Abs(item.action.Progress-recorder.ProgressFor(skills[item.owner].ActionRuns,new PNActionRunKey(item.context.Invocation.CastKey,item.context.ActionOrdinal),now))>1e-5)throw new Exception("Per-capture Action progress oracle");
   }
   now+=2;int ended=0;foreach(var item in running){if(Math.Abs(item.action.Progress-recorder.ProgressFor(skills[item.owner].ActionRuns,new PNActionRunKey(item.context.Invocation.CastKey,item.context.ActionOrdinal),now))>1e-5)throw new Exception("Actual Action final progress oracle");if(live && ended++<2)item.action.Exit(item.context);else item.action.Cancel(item.context);item.action.Dispose();}running.Clear();foreach(var skill in skills)skill.CancelCast("A2 cleanup");
   if(recorder.Errors!=0)throw new Exception("Recorder failures "+recorder.Errors);
   var text="PASS A2 framework starts="+starts+" peak="+peak+" entities="+entities+" live="+live;File.WriteAllText(Path.Combine(folder,"result.txt"),text);return text;
  } finally {
   foreach(var item in running){try{item.action.Cancel(item.context);}finally{item.action.Dispose();}}
   foreach(var service in services)service.Dispose();
   try {foreach(var go in scene.GetRootGameObjects())foreach(var loader in go.GetComponentsInChildren<EveEntityLoader>(true))loader.Unload();}
   finally{if(EveRegistrySession.State!=EveSessionState.Stopped)EveRegistrySession.End();EditorSceneManager.ClosePreviewScene(scene);}
  }
 }
}
#endif
