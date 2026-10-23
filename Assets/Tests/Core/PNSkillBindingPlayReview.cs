#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public sealed class PNSkillBindingPlayReview:MonoBehaviour
{
    public static string Status="Idle";
    private readonly List<string> checks=new List<string>();
    private readonly List<GameObject> roots=new List<GameObject>();
    private Keyboard keyboard;
    private void Check(bool ok,string text){if(!ok)throw new Exception(text);checks.Add("PASS "+text);}
    private IEnumerator Start()
    {
        Status="Running";var iterator=Run();
        try
        {
            while(true){object next;try{if(!iterator.MoveNext())break;next=iterator.Current;}catch(Exception error){Status="FAIL "+error;checks.Add(Status);yield break;}yield return next;}
            Status="PASS "+checks.Count+" native binding Play checks";checks.Add(Status);
        }
        finally
        {
            (iterator as IDisposable)?.Dispose();
            try{foreach(var root in roots)if(root!=null)root.GetComponent<EveEntityLoader>()?.Unload();EveRegistrySession.End();}
            finally{if(keyboard!=null)InputSystem.RemoveDevice(keyboard);foreach(var root in roots)if(root!=null)Destroy(root);Directory.CreateDirectory(".utmp/SkillOverride");File.WriteAllLines(".utmp/SkillOverride/PlayResults.txt",checks);}
        }
    }
    private IEnumerator Run()
    {
        if(EveRegistrySession.State!=EveSessionState.Stopped)throw new InvalidOperationException("Clean session required.");
        var docs=new PNGameContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),Path.Combine(Application.persistentDataPath,"Mods"),Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule(),new PNSkillBindingFixtureModule()},docs);
        EveRegistrySession.Begin();EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,docs,"test:binding"));
        GameObject Root(long id,string character)
        {var root=new GameObject("Binding fixture "+id);roots.Add(root);root.SetActive(false);root.transform.position=new Vector3(500,5,id==991501?500:510);root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id),EveId.Parse(character),0);root.AddComponent<EveTimeScale>();root.AddComponent<PNAbilityManager>();root.AddComponent<Rigidbody>().useGravity=false;root.AddComponent<CapsuleCollider>();return root;}
        var root=Root(991501,"project_n:eve");var stat=root.AddComponent<EveCharacterStat>();var move=root.AddComponent<PNPlayerMove>();var buffs=root.AddComponent<PNBuffManager>();var skill=root.AddComponent<EveCharacterSkill>();root.AddComponent<EveEntityLoader>().Load(hub);root.SetActive(true);
        yield return null;move.PC.devices=Array.Empty<InputDevice>();
        foreach(var action in new[]{skill.PC.Controller.Attack,skill.PC.Controller.Skill,skill.PC.Controller.Ulti,skill.PC.Controller.Dash,skill.PC.Controller.Burst}){action.Disable();action.bindingMask=new InputBinding{groups="BindingReview"};}
        skill.PC.Controller.Attack.AddBinding("<Keyboard>/f12",groups:"BindingReview");skill.RefreshInput();keyboard=InputSystem.AddDevice<Keyboard>();
        var source=PNBuffSource.Capture(stat);var buff=buffs.Buffs.Apply(new PNBuffApplyRequest(PNSkillBindingFixtureModule.Buff,source));
        yield return null;
        skill.enabled=false;Check(PNSkillChargeFixture.SelectionLeases==0,"Native disable returns selection resources");skill.enabled=true;yield return null;Check(PNSkillChargeFixture.SelectionLeases==1&&!skill.ExecutingProfile.IsValid,"Native enable restores selection without replaying cast");
        var starts=PNSkillChargeFixture.Starts;var origin=root.transform.position;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F12));InputSystem.Update();
        Check(PNSkillChargeFixture.Starts==starts,"Hold profile does not fire at key down");
        yield return new WaitForSecondsRealtime(.14f);
        Check(PNSkillChargeFixture.Starts==starts+1,"Actual Hold interaction starts replacement once");
        Check(skill.ExecutingProfile==PNSkillBindingFixtureModule.Profile,"Selected Attack slot executes custom hybrid behavior");
        buffs.Buffs.Remove(buff.Handle);
        Check(skill.ExecutingProfile==PNSkillBindingFixtureModule.Profile,"Buff revoke retains committed cast under FinishCurrent");
        Check(PNSkillChargeFixture.SelectionLeases==0,"Deselection returns selection resources while cast remains pinned");
        yield return new WaitForSecondsRealtime(.4f);
        Check(!skill.ExecutingProfile.IsValid&&root.transform.position.x>origin.x+2.8f,"Custom behavior finishes real Rigidbody motion and cleans execution");
        Check(skill.CurrentCast==EveCast.None,"Held key does not start restored Eve Attack after binding change");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();yield return null;
        var recovery=skill.Abilities.GetCooldown(PNSkillBindingFixtureModule.Recovery);var handle=recovery.Handle;
        buff=buffs.Buffs.Apply(new PNBuffApplyRequest(PNSkillBindingFixtureModule.Buff,source));
        Check(skill.Abilities.GetCooldown(PNSkillBindingFixtureModule.Recovery).Handle==handle&&recovery.Current==1,"Re-equip keeps same recovery and spent charge");
        int inputStarts=PNSkillChargeFixture.InputStarts;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F12));InputSystem.Update();
        Check(PNSkillChargeFixture.InputStarts==inputStarts+1,"Neutral binding grant accepts a new press in the same frame");
        buffs.Buffs.Remove(buff.Handle);yield return new WaitForSecondsRealtime(.2f);
        Check(PNSkillChargeFixture.Starts==starts+1,"Revoke during uncommitted charge does not fabricate performed");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();yield return null;
        Check(skill.TrySkill()&&skill.Empowerment.IsActive,"Direct Eve Skill identity still grants its own empowerment");yield return new WaitForSecondsRealtime(.4f);
        buff=buffs.Buffs.Apply(new PNBuffApplyRequest(PNSkillBindingFixtureModule.Buff,source));yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F12));InputSystem.Update();yield return new WaitForSecondsRealtime(.14f);
        Check(skill.Empowerment.IsActive,"Replacement does not consume Eve next-attack state");
        buffs.Buffs.Remove(buff.Handle);InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();yield return new WaitForSecondsRealtime(.4f);
        Check(skill.TryAttack()&&!skill.Empowerment.IsActive,"Restored exact Eve attack consumes retained empowerment");yield return new WaitForSecondsRealtime(.6f);
        // Same behavior on a non-Eve player: no Eve runtime object required by the factory.
        var other=Root(991502,"project_n:dummy");other.AddComponent<PNPlayerStat>();var generic=other.AddComponent<PNPlayerSkill>();other.AddComponent<EveEntityLoader>().Load(hub);other.SetActive(true);yield return null;
        var lease=generic.Bindings.AcquireOverride("fixture",new PNSkillOverrideRequest(new[]{new KeyValuePair<EveId,EveId>(PNSkillSlots.Dash,PNSkillBindingFixtureModule.Profile)})).Lease;
        generic.RequestSlot(PNSkillSlots.Dash,new PNSkillInputEvent(PNSkillInputPhase.Started,PNSkillInteraction.Hold));generic.RequestSlot(PNSkillSlots.Dash,new PNSkillInputEvent(PNSkillInputPhase.Performed,PNSkillInteraction.Hold));
        Check(generic.ExecutingProfile==PNSkillBindingFixtureModule.Profile&&generic.Abilities.GetCooldown(PNSkillBindingFixtureModule.Recovery).Current==1,"Independent non-Eve entity runs custom behavior with own recovery");
        lease.Dispose();yield return new WaitForSecondsRealtime(.4f);Check(!generic.ExecutingProfile.IsValid,"Non-Eve Update advances and retires replacement");
        foreach(var action in new[]{generic.PC.Controller.Attack,generic.PC.Controller.Skill,generic.PC.Controller.Ulti,generic.PC.Controller.Dash,generic.PC.Controller.Burst}){action.Disable();action.bindingMask=new InputBinding{groups="DefaultDashReview"};}
        generic.PC.Controller.Dash.AddBinding("<Keyboard>/f11",groups:"DefaultDashReview");generic.RefreshInput();yield return null;
        var dashPosition=other.transform.position;InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F11));InputSystem.Update();
        Check(generic.IsDashing&&generic.DashRecovery.Current==2,"Actual press routes restored default Dash through legacy binding once");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());InputSystem.Update();yield return new WaitForSecondsRealtime(.3f);
        Check(!generic.IsDashing&&Vector3.Distance(other.transform.position,dashPosition)>2.8f,"Default bound Dash drives real motion and completes after replacement");
    }
}
public static class PNSkillBindingPlayRunner
{
    public const string ScenePath="Assets/Tests/Core/SkillOverrideFixture.unity";
    private const string Key="ProjectN.SkillOverride.PlaySettings";
    [Serializable]private sealed class Settings{public string start;public bool background;public int fps;}
    [InitializeOnLoadMethod]private static void Install()=>EditorApplication.playModeStateChanged+=Restore;
    private static void Restore(PlayModeStateChange state)
    {if(state!=PlayModeStateChange.EnteredEditMode)return;var data=SessionState.GetString(Key,"");if(data.Length==0)return;var settings=JsonUtility.FromJson<Settings>(data);EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(settings.start);Application.runInBackground=settings.background;Application.targetFrameRate=settings.fps;SessionState.EraseString(Key);}
    public static void Prepare()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Idle editor required.");
        bool existing=File.Exists(ScenePath);
        var original=SceneManager.GetActiveScene();bool dirty=original.isDirty;
        if(!existing)
        {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try{var root=new GameObject("Skill override review");SceneManager.MoveGameObjectToScene(root,scene);root.AddComponent<PNSkillBindingPlayReview>();EditorSceneManager.SaveScene(scene,ScenePath);}
        finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);}
        }
        if(dirty!=original.isDirty)throw new Exception("Authored dirty state changed.");
        SessionState.SetString(Key,JsonUtility.ToJson(new Settings{start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),background=Application.runInBackground,fps=Application.targetFrameRate}));
        Application.runInBackground=true;Application.targetFrameRate=60;EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
    }
}
#endif
