#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;
public static class PNVisualStringLifetimeChecks
{
    public static string Run()
    {
        PNVisualStringEditorLifetime.CleanOrphans();
        var ink=PNVisualPaperSettings.Ink;var mode=PNVisualPaperSettings.Mode;
        try
        {
            using(var rig=new PNVisualBakeCheck.Rig(true,1,640,480))
            {
                PNVisualStringSystem.CameraOverride=rig.Camera;rig.Actors[0].ShowStrings();rig.Actors[0].Tick(0);PNVisualStringSystem.Draw();
                var live=PNVisualStringSystem.Renderer;
                if(live==null||!live.enabled)throw new Exception("Live strings missing");
                var orphan=new GameObject("PN Visual Strings"){hideFlags=HideFlags.HideAndDontSave};
                var mesh=UnityEngine.Object.Instantiate(live.GetComponent<MeshFilter>().sharedMesh);mesh.name="PN Visual Strings";
                var material=new Material(live.sharedMaterial){name="PN Visual Strings",hideFlags=HideFlags.HideAndDontSave};
                orphan.AddComponent<MeshFilter>().sharedMesh=mesh;orphan.AddComponent<MeshRenderer>().sharedMaterial=material;
                PNVisualStringEditorLifetime.CleanOrphans();
                if(orphan!=null||mesh!=null||material!=null)throw new Exception("Orphan resources survived");
                if(live==null||!live.enabled||PNVisualStringSystem.LastStrings!=2)throw new Exception("Cleanup touched live strings");
                PNVisualStringSystem.Release();PNVisualStringSystem.Draw();
                if(PNVisualStringSystem.Renderer==null||PNVisualStringSystem.LastStrings!=2)throw new Exception("Batch failed to recreate");
            }
            PNVisualStringEditorLifetime.CleanOrphans();
            int remaining=Resources.FindObjectsOfTypeAll<MeshRenderer>().Count(r=>r!=null&&r.sharedMaterial!=null&&r.sharedMaterial.shader!=null&&r.sharedMaterial.shader.name=="PN/VisualStrings");
            if(remaining!=0)throw new Exception("Wire renderer remained without actors");
            return "PASS orphan host/mesh/material removed; live 2 strings preserved; release/recreate works; last actor leaves 0 wire renderers";
        }
        finally{PNVisualStringSystem.CameraOverride=null;PNVisualPaperSettings.Ink=ink;PNVisualPaperSettings.Mode=mode;}
    }
}
#endif

