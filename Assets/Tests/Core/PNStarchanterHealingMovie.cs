#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class PNVisualEditorChecks
{
    // Bounded batches of actual Unity renders; GIF encoding is separate. No Play Mode or authored scene edits.
    public static string RenderStarchanterHealingMovie(string output,int first,int count,bool teleportClose=false,int review=0,int fps=15)
    {
        if(Application.isPlaying)throw new InvalidOperationException("Stopped Editor required");
        Directory.CreateDirectory(output);Stage stage=null;GameObject source=null,allyRoot=null;RenderTexture target=null;Texture2D image=null;
        var oldCamera=PNVisualStringSystem.CameraOverride;float oldTime=PNVisualStringSystem.FixedTime;
        string contentRoot=Application.streamingAssetsPath+"/BaseContent";PNVisualPacks.Set("project_n",contentRoot);
        var documents=new PNGameContentSource(contentRoot,"unused",Array.Empty<string>()).ReadDocuments().ToArray();
        var hub=new EveEngine.EveContentBootstrap().Build(new EveEngine.EveIContentModule[]{new PNContentModule(),new PNGameplayContentModule()},documents);
        try
        {
            stage=new Stage();stage.Camera.backgroundColor=new Color(.20f,.26f,.23f);stage.Camera.orthographicSize=2.22f;stage.Camera.aspect=4f/3;
            stage.Camera.transform.position=new Vector3(0,4,-5);stage.Camera.transform.LookAt(new Vector3(0,.03f,0));
            if(review==1||review==2){stage.Camera.orthographicSize=review==1?2.2f:.75f;stage.Camera.transform.position=new Vector3(.22f,1.7f,-3);stage.Camera.transform.LookAt(new Vector3(.22f,review==1?.65f:.20f,0));}
            if(teleportClose){stage.Camera.orthographicSize=.66f;stage.Camera.transform.position=new Vector3(-.35f,1.6f,-3);stage.Camera.transform.LookAt(new Vector3(-.35f,.22f,0));}
            target=new RenderTexture(1024,768,24);target.Create();stage.Camera.targetTexture=target;image=new Texture2D(1024,768,TextureFormat.RGB24,false);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starchanter/Prefab/Starchanter.prefab");
            source=UnityEngine.Object.Instantiate(prefab);source.SetActive(false);SceneManager.MoveGameObjectToScene(source,stage.Scene);
            source.transform.position=new Vector3(-.70f,0,-.10f);
            foreach(var b in source.GetComponents<MonoBehaviour>())b.enabled=false;source.SetActive(true);
            var body=source.GetComponent<PNVisualBody>();body.Begin(hub.Characters.Get(EveEngine.EveId.Parse("project_n:starchanter")),hub);
            var actor=body.Actor;actor.ShowStrings();var facing=actor.GetComponent<PNCameraFacingTarget>();facing.ViewCamera=stage.Camera;facing.Align();
            var ally=SpawnAppearance(stage,hub,"project_n:starkeeper",out allyRoot);allyRoot.transform.position=new Vector3(.85f,0,.3f);ally.HideStrings();
            var fx=source.GetComponent<PNStarchanterPresentation>();fx.Preview(actor,stage.Camera);
            PNVisualStringSystem.CameraOverride=stage.Camera;
            int nextPulse=0;bool ended=false;string previousState=null;
            if(review>0){source.transform.position=Vector3.zero;allyRoot.transform.position=new Vector3(.45f,0,0);}
            if(review==2)fx.BeginStrike(.25f,Vector3.right*.45f);
            float[] pulses={2.8f,4.3f,5.8f,6.3f};
            for(int frame=0;frame<first+count;frame++)
            {
                float t=frame/(float)fps;string state=review==2?"idle":review==1?(t<.6f?"heal_channel":"idle"):t<.6f?"heal_teleport_out":t<1.3f?"heal_teleport_in":t<6.3f?"heal_channel":"idle";
                if(state!=previousState){actor.Play(state,true);previousState=state;}
                if(review==0||review==3)
                {
                    if(frame==Mathf.CeilToInt(.6f*fps)){source.transform.position=Vector3.zero;fx.TeleportArrived();}
                    actor.Tick(1f/fps);ally.Tick(1f/fps);
                    if(!ended)fx.Heal(t,.6f,.7f,5,1.5f,2);
                    while(nextPulse<pulses.Length&&t>=pulses[nextPulse]&&!ended)
                    {fx.PulseReady();fx.HealRecipient(source.transform.position,nextPulse==3);fx.HealRecipient(allyRoot.transform.position,nextPulse==3);nextPulse++;}
                    if(t>=(review==3?3.3f:6.3f)&&!ended){fx.EndCast(review==0);ended=true;}
                }
                else
                {
                    actor.Play(review==1&&t<.6f?"heal_channel":"idle",false);actor.Tick(1f/fps);ally.Tick(1f/fps);
                    if(review==1)
                    {
                        if(t<.6f)fx.Boost(t,.3f,2);
                        if(frame==Mathf.CeilToInt(.3f*fps)){fx.BoostRecipient(source.transform.position);fx.BoostRecipient(allyRoot.transform.position);fx.BoostPulse();}
                        if(t>=.6f&&!ended){fx.EndCast(true);ended=true;}
                        if(t>.9f&&t<4.5f){allyRoot.transform.position=new Vector3(.45f+Mathf.Sin((t-.9f)*1.5f)*.32f,0,Mathf.Sin((t-.9f)*3)*.10f);ally.Play("run",false);}
                    }
                    else if(frame==Mathf.CeilToInt(.25f*fps)){fx.ReleaseStrike(allyRoot.transform.position);}
                }
                fx.Advance(1f/fps);PNVisualStringSystem.FixedTime=t;PNVisualStringSystem.Draw();
                if(frame<first)continue;
                var saved=RenderTexture.active;
                try{stage.Camera.Render();RenderTexture.active=target;image.ReadPixels(new Rect(0,0,1024,768),0,0);image.Apply();File.WriteAllBytes(output+"/frame_"+frame.ToString("000")+".png",image.EncodeToPNG());}
                finally{RenderTexture.active=saved;}
            }
        }
        finally
        {
            if(source!=null){source.GetComponent<PNStarchanterPresentation>()?.Dispose();source.GetComponent<PNVisualBody>()?.Clear();UnityEngine.Object.DestroyImmediate(source);}
            if(allyRoot!=null){UnityEngine.Object.DestroyImmediate(allyRoot);}
            if(stage!=null)stage.Camera.targetTexture=stage.Target;
            if(target!=null){target.Release();UnityEngine.Object.DestroyImmediate(target);}if(image!=null)UnityEngine.Object.DestroyImmediate(image);
            stage?.Dispose();PNVisualRuntime.Release();PNVisualPacks.Clear();PNVisualStringSystem.Release();PNVisualStringSystem.CameraOverride=oldCamera;PNVisualStringSystem.FixedTime=oldTime;
        }
        return "Native full-radius healing frames "+first+".."+(first+count-1)+" rendered at 1024x768, "+fps+" fps.";
    }
}
#endif
