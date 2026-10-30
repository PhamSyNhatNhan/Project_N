#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class PNVisualStringEffectsCheck
{
    public static string RunCapacity()
    {
        var ink=PNVisualPaperSettings.Ink;var mode=PNVisualPaperSettings.Mode;
        try
        {
            using(var rig=new PNVisualBakeCheck.Rig(true,128,320,240))
            {
                PNVisualStringSystem.CameraOverride=rig.Camera;
                foreach(var actor in rig.Actors)
                {
                    actor.ShowStrings();
                    actor.AddString(new PNVisualStringEntry("head_top",EveEngine.EveId.Parse("project_n:puppet_main"),null,null));
                    actor.AddString(new PNVisualStringEntry("tail_tip",EveEngine.EveId.Parse("project_n:puppet_drape"),null,null));
                    for(int s=0;s<4;s++){var state=actor.Strings.State(s);var p=state.Current;p.Segments=48;state.Retarget(p,0);}
                    actor.Tick(0);
                }
                PNVisualStringSystem.Draw();
                for(int f=0;f<60;f++)
                {
                    foreach(var actor in rig.Actors){actor.transform.position+=Vector3.right*.016f;actor.PulseStringEffects();actor.Tick(.016f);}
                    PNVisualStringSystem.Draw();
                    if(PNVisualStringSystem.LastStrings!=512||PNVisualStringSystem.LastVertices>65000)throw new Exception("Optional effects displaced primary ribbons");
                }
                return "PASS capacity: 128 puppets, 512 strings at 48 segments, active effects respect 65000 vertices and preserve every primary ribbon";
            }
        }
        finally{PNVisualStringSystem.CameraOverride=null;PNVisualPaperSettings.Ink=ink;PNVisualPaperSettings.Mode=mode;}
    }
    public static string Run()
    {
        const string output="F:/unity/Project_N/.utmp/StringEffects20261030/Frames";
        Directory.CreateDirectory(output);
        var ink=PNVisualPaperSettings.Ink;var mode=PNVisualPaperSettings.Mode;
        int maxDust=0,maxTrails=0;
        try
        {
            using(var rig=new PNVisualBakeCheck.Rig(true,1,720,900))
            {
                var actor=rig.Actors[0];
                rig.Camera.orthographicSize=.95f;rig.Camera.transform.position=new Vector3(.1f,.8f,-5);
                PNVisualStringSystem.CameraOverride=rig.Camera;PNVisualStringSystem.FixedTime=-1;
                actor.ShowStrings();actor.Tick(0);
                for(int frame=0;frame<120;frame++)
                {
                    float x=frame<20?0:frame<55?(frame-20)*.018f:frame<90?.63f-(frame-55)*.018f:0;
                    actor.transform.position=new Vector3(x,0,0);
                    if(frame==65)actor.PulseStringEffects();
                    actor.Tick(1f/30);PNVisualStringSystem.Draw();
                    if(PNVisualStringSystem.LastStrings!=2)throw new Exception("Primary string lost");
                    maxDust=Math.Max(maxDust,PNVisualStringSystem.LastDust);maxTrails=Math.Max(maxTrails,PNVisualStringSystem.LastTrails);
                    rig.Camera.Render();var previous=RenderTexture.active;RenderTexture.active=rig.Target;
                    var t=new Texture2D(720,900,TextureFormat.RGB24,false);
                    try{t.ReadPixels(new Rect(0,0,720,900),0,0);t.Apply();File.WriteAllBytes(output+"/"+frame.ToString("D3")+".png",t.EncodeToPNG());}
                    finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(t);}
                }
                if(maxDust==0||maxTrails!=2)throw new Exception("Effects did not render");
                rig.Camera.GetComponent<UniversalAdditionalCameraData>().SetRenderer(0);
                PNVisualStringSystem.Draw();rig.Camera.Render();
                for(int i=0;i<30;i++){actor.transform.position+=Vector3.right*.005f;actor.Tick(.016f);PNVisualStringSystem.Draw();}
                long start=GC.GetAllocatedBytesForCurrentThread();
                for(int i=0;i<300;i++){actor.transform.position+=Vector3.right*.005f;if(i%30==0)actor.PulseStringEffects();actor.Tick(.016f);PNVisualStringSystem.Draw();}
                long allocated=GC.GetAllocatedBytesForCurrentThread()-start;
                if(allocated!=0)throw new Exception("Active effects allocated "+allocated);
                actor.HideStrings();PNVisualStringSystem.Draw();
                if(PNVisualStringSystem.LastVertices!=0)throw new Exception("Hide left effects visible");
                return "PASS animated frames 120, peak dust "+maxDust+", trails "+maxTrails+", active tick/draw 300 frames 0B; hide clears effects; both renderer paths rendered";
            }
        }
        finally{PNVisualStringSystem.CameraOverride=null;PNVisualStringSystem.FixedTime=-1;PNVisualPaperSettings.Ink=ink;PNVisualPaperSettings.Mode=mode;}
    }
}
#endif
