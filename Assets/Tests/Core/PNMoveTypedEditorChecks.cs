#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using EveEngine.Movement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PNMoveTypedEditorChecks
{
    public static string Run()
    {
        if(Application.isPlaying||EveRegistrySession.State!=EveSessionState.Stopped||SceneManager.GetActiveScene().rootCount!=0)throw new InvalidOperationException("Empty disposable stopped scene required.");
        int checks=0;void Check(bool ok,string label){if(!ok)throw new Exception(label);checks++;}
        var docs=new EveDirectoryContentSource(Path.Combine(Application.streamingAssetsPath,"BaseContent"),Path.Combine(Application.persistentDataPath,"Mods"),Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveContentBootstrap().Build(new EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},docs);var content=EveContentCompatibility.Create(hub,docs,"test:p4_hold");
        var host=Guid.NewGuid();GameObject root=null;
        try
        {
            EveRegistrySession.Begin();EveRegistrySession.Start(hub,EveSessionRole.Client,content);var session=EveRegistrySession.Context;session.Coop.AdmitHost(host,content,7);
            root=new GameObject("Disposable P4 held owner");root.SetActive(false);root.transform.position=new Vector3(0,2,0);
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(5002),EveId.Parse("project_n:eve"),7);
            var body=root.AddComponent<Rigidbody>();body.constraints=RigidbodyConstraints.FreezeRotation;body.useGravity=true;
            root.AddComponent<CapsuleCollider>();root.AddComponent<EveCharacterStat>();root.AddComponent<EveTimeScale>();root.AddComponent<PNBuffManager>().RenderPresentation=false;
            var move=root.AddComponent<PNMoveNativeProbe>();var loader=root.AddComponent<EveEntityLoader>();move.ConfigureNetworkMovement();loader.Load(hub);
            Check(body.isKinematic&&move.MovementHeld&&!move.IsReady,"inactive publication keeps physics held");root.SetActive(true);
            move.BindOwnerMovement(host);move.OwnerMovement.ExpectBaseline(1);var id=move.NetworkIdentity(host);
            var mirror=new MoveMirror(id,1,1,false,MoveRights.All,5,6,1,1,new[]{"base:voluntary_motion"});
            var state=new MovementState(new MoveVector(3,4,5),new MoveVector(0,2,0));
            var correction=new MoveCorrection(id,1,1,1,state,new MoveRecovery(new MoveVector(0,2,0),default),MoveCorrectionReason.Baseline,0,1);
            int typed=0,legacy=0;move.MovementReportPublished+=q=>typed++;move.OwnerPoseReport+=q=>legacy++;
            move.StageMovementControl(mirror,correction,1);Check(!move.MovementBaselineApplied,"receive alone is not applied");move.Tick();
            Check(move.MovementBaselineApplied&&move.AppliedMovementControl==1&&body.position==new Vector3(3,4,5),"fixed boundary applies target while held");
            Check(body.isKinematic&&typed==0&&legacy==0,"no physics/report escape before session unlock");move.SetMovementHeld(false);move.Tick();
            Check(!body.isKinematic&&move.IsReady&&typed==1&&legacy==0,"hold transfers original dynamic state; typed transport avoids codec event");
            move.SetMovementHeld(true);move.SetMoveDirection(Vector2.right);var before=body.position;move.Tick();Check(body.position==before&&body.isKinematic&&!move.CanWriteMotion,"live pause gates writers");
            move.SetMovementHeld(false);move.DisableMove();Check(body.isKinematic&&move.MovementHeld,"disable holds physics even while component cannot tick");move.EnableMove();move.BindOwnerMovement(host);move.OwnerMovement.ExpectBaseline(2);
            move.StageMovementControl(mirror,new MoveCorrection(id,2,2,1,state,default,MoveCorrectionReason.Baseline,10,2),2);move.Tick();move.SetMovementHeld(false);Check(!body.isKinematic&&move.AppliedMovementControl==2,"same-generation rebind applies fresh target and returns original physics");
            move.ReleaseNetworkMovement();Check(!body.isKinematic&&body.constraints==RigidbodyConstraints.FreezeRotation&&body.useGravity,"release restores original physics");
            // An originally kinematic body must remain kinematic across hold transfer too.
            body.isKinematic=true;move.ConfigureNetworkMovement();move.SetMovementHeld(true);move.BindOwnerMovement(host);move.ReleaseNetworkMovement();Check(body.isKinematic,"original kinematic state retained");
        }
        finally{if(EveRegistrySession.State!=EveSessionState.Stopped)EveRegistrySession.End();if(root!=null)UnityEngine.Object.DestroyImmediate(root);}
        return "PASS "+checks+" P4 native hold/typed-publication checks";
    }
}
#endif
