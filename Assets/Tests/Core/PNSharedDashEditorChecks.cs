#if UNITY_EDITOR
using System;
using System.IO;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PNSharedDashEditorProbe : PNPlayerSkill
{
    public void TickDash(float delta)=>AdvancePlayerDash(delta);
    public void InitializeInput()=>Awake();
    public void DisableSkill(){enabled=false;OnDisable();}
    public void EnableSkill(){enabled=true;OnEnable();}
}
public static class PNSharedDashEditorChecks
{
    public static string Run()
    {
        if(EditorApplication.isPlaying || EveRegistrySession.State!=EveSessionState.Stopped)throw new InvalidOperationException("Shared Dash requires idle Edit Mode.");
        var original=SceneManager.GetActiveScene();bool dirty=original.isDirty;
        var preview=EditorSceneManager.NewPreviewScene();int checks=0;
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;}
        var doc=new EveContentDocument("dummy",File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"BaseContent/characters/DummyCharacter.json")),"project_n");
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},new[]{doc});
        EveRegistrySession.Begin();EveRegistrySession.Start(hub,EveSessionRole.Offline,EveContentCompatibility.Create(hub,new[]{doc},"test:shared_dash"));
        PNSharedDashEditorProbe Player(long id,Vector3 position)
        {
            var root=new GameObject("Shared Dash player "+id);root.SetActive(false);SceneManager.MoveGameObjectToScene(root,preview);root.transform.position=position;
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id),EveId.Parse("project_n:dummy"),0);
            root.AddComponent<PNAbilityManager>();root.AddComponent<DummyStat>();
            var body=root.AddComponent<Rigidbody>();body.useGravity=false;body.interpolation=RigidbodyInterpolation.Interpolate;
            var capsule=root.AddComponent<CapsuleCollider>();capsule.radius=.12f;capsule.height=.6f;capsule.center=Vector3.up*.3f;
            var skill=root.AddComponent<PNSharedDashEditorProbe>();skill.InitializeInput();root.AddComponent<EveEntityLoader>().Load(hub);root.SetActive(true);return skill;
        }
        try
        {
            var first=Player(989001,Vector3.zero);var second=Player(989002,new Vector3(0,0,4));
            Check(first.Skills.Count==0 && first.DashRecovery.Current==3,"Shared Dash depends on character skill data.");
            Check(first.DashInput.PressImmediately,"Default Dash is not configured for immediate press.");
            var body=first.GetComponent<Rigidbody>();Check(first.TryDash(),"Default Dash cannot activate.");
            first.TickDash(.08f);Check(Math.Abs(body.position.x-1.5f)<.01,"Default Dash did not move real Rigidbody during motion.");
            Check(!first.TryDash(),"Default Dash reentered its own invocation.");
            first.TickDash(.08f);Check(Math.Abs(body.position.x-3)<.01 && !first.IsDashing,"Default Dash did not finish configured distance.");
            Check(body.interpolation==RigidbodyInterpolation.Interpolate,"Default Dash did not restore interpolation.");
            Check(first.DashRecovery.Current==2 && second.DashRecovery.Current==3,"Dash charges are shared between entities.");
            var manager=first.GetComponent<PNAbilityManager>();manager.Advance(2);Check(first.TryDash(),"Second Dash failed.");first.TickDash(.2f);
            Check(first.DashRecovery.CooldownRemaining==3,"Second Dash reset the recovery timer.");
            Check(first.TryDash(),"Third Dash failed.");first.TickDash(.2f);Check(!first.TryDash(),"Exhausted default Dash activated.");
            manager.Advance(3);Check(first.DashRecovery.Current==1,"Default Dash did not recover sequentially.");
            Check(first.TryDash(),"Default Dash cannot activate before disable.");first.DisableSkill();
            Check(!first.IsDashing && body.interpolation==RigidbodyInterpolation.Interpolate,"Disabling player skill leaked motion/interpolation state.");first.EnableSkill();
            first.GetComponent<EveEntityLoader>().Unload();first.GetComponent<EveEntityLoader>().Load(hub);
            Check(first.DashRecovery.Current==3 && !first.IsDashing,"Reload leaked shared Dash state.");
            return "PASS "+checks+" native shared Dash checks; two non-Eve players, no Eve content, Rigidbody movement, independent recovery and lifecycle.";
        }
        finally
        {
            try {EveRegistrySession.End();} finally {EditorSceneManager.ClosePreviewScene(preview);}
            if(original.isDirty!=dirty)throw new Exception("Shared Dash checks modified authored scene.");
        }
    }
}
#endif
