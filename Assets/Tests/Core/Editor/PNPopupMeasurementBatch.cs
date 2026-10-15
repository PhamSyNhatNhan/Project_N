#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PNPopupMeasurementBatch
{
    public static void Run()
    {
        const string folder = ".utmp/Step4a";
        Directory.CreateDirectory(folder);
        string ownedHost = null;
        try
        {
            var scene = SceneManager.GetActiveScene(); bool dirty = scene.isDirty;
            if (Application.isBatchMode && string.IsNullOrEmpty(scene.path) && !dirty)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Tests/Generated")) AssetDatabase.CreateFolder("Assets/Tests", "Generated");
                ownedHost = "Assets/Tests/Generated/PopupBatch_" + Guid.NewGuid().ToString("N") + ".unity";
                if (!EditorSceneManager.SaveScene(scene, ownedHost)) throw new Exception("Cannot save owned empty batch scene.");
            }
            bool baseline = Environment.GetCommandLineArgs().Contains("-pn-popup-baseline");
            if (baseline)
            {
                File.WriteAllText(folder+"/RenderBefore.txt", PNDamagePopupRenderChecks.Run());
                File.Copy("F:/unity/Project_N/.utmp/PNDamagePopupGlow.png", folder+"/RenderBefore.png", true);
            }
            else
            {
                EveHotPathMetrics.Enabled = EveHotPathMetrics.CountOnly = false;
                File.WriteAllText(folder+"/Native.txt", PNDamagePopupEditorChecks.Run());
                int observed = 0; PNDamagePopupDiagnostics.Observe = (_,_,_) => observed++;
                File.WriteAllText(folder+"/NativeDisabled.txt", PNDamagePopupEditorChecks.Run());
                if (observed != 0 || EveHotPathMetrics.Drain().Count != 0) throw new Exception("Disabled popup diagnostics recorded data.");
                EveHotPathMetrics.Enabled = true;
                File.WriteAllText(folder+"/NativeEnabled.txt", PNDamagePopupEditorChecks.Run());
                if (observed == 0 || !EveHotPathMetrics.Drain().ContainsKey("popup.mesh")) throw new Exception("Enabled diagnostic failed to observe actual popup path.");
                File.WriteAllText(folder+"/Parity.txt", "PASS native assertions in disabled/enabled diagnostic modes; text/elements/lane direct parity: "+Parity());
                PNDamagePopupDiagnostics.Observe = null; EveHotPathMetrics.Enabled = false;
                File.WriteAllText(folder+"/RenderAfter.txt", PNDamagePopupRenderChecks.Run());
                File.Copy("F:/unity/Project_N/.utmp/PNDamagePopupGlow.png", folder+"/RenderAfter.png", true);
                if (scene != SceneManager.GetActiveScene() || scene.isDirty != dirty) throw new Exception("Checks changed authored scene state.");
                File.WriteAllText(folder+"/BuildNormal.txt", PNLifecyclePolicyPlayerBuild.Build());
                File.WriteAllText(folder+"/BuildDevelopment.txt", PNLifecyclePolicyPlayerBuild.BuildDevelopment());
            }
            File.WriteAllText(folder+(baseline?"/BaselineResult.txt":"/BatchResult.txt"), "PASS checks and requested build stages");
            if (ownedHost != null) AssetDatabase.DeleteAsset(ownedHost);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            PNDamagePopupDiagnostics.Observe = null; EveHotPathMetrics.Enabled = EveHotPathMetrics.CountOnly = false;
            File.WriteAllText(folder+"/BatchFailure.txt",e.ToString());
            if (ownedHost != null) AssetDatabase.DeleteAsset(ownedHost);
            EditorApplication.Exit(1);
        }
    }
    private static int Parity()
    {
        var prefab = Resources.Load<GameObject>("PNUI/PNDamagePopup");
        string[] expected = null; int checks = 0;
        foreach (bool enabled in new[] {false,true})
        {
            EveHotPathMetrics.Enabled = enabled;
            var root = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var popup = root.GetComponent<PNDamagePopup>(); var actual = new string[4];
                for (int i=0;i<4;i++)
                {
                    popup.Begin(new[]{0d,1.25d,37.5d,2147483648.75d}[i],new Vector3(.12f*i,.55f,0),Color.cyan,null,i%2==1,
                        i==2?new[]{EveId.Parse("mod:wind"),PNContentIds.Fire,PNContentIds.Fire}:new[]{PNContentIds.Ice,PNContentIds.Lightning});
                    popup.Advance(.44f);
                    actual[i]=popup.DisplayText+"|"+string.Join(",",popup.DisplayedElements.Select(id=>id.ToString()))+"|"+popup.transform.position+"|"+popup.transform.localScale+"|"+popup.Progress;
                }
                if(expected==null) expected=actual;
                else for(int i=0;i<actual.Length;i++){if(actual[i]!=expected[i])throw new Exception("Diagnostic altered popup parity.");checks++;}
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        return checks;
    }
    public static void Extract()
    {
        const string folder=".utmp/Step4a";
        try
        {
            var log="";
            foreach(var file in Directory.GetFiles(folder,"AllocationTrace*.raw",SearchOption.AllDirectories))
                if(!file.Contains("IsolatedProject") && new FileInfo(file).Length > 1000) log+=file+": "+PNBuffAllocationTraceReview.Extract(Path.GetFullPath(file),Path.Combine(Path.GetDirectoryName(file),"Stacks",Path.GetFileNameWithoutExtension(file)))+"\n";
            File.WriteAllText(folder+"/Extract.txt",log); EditorApplication.Exit(0);
        }
        catch(Exception e){File.WriteAllText(folder+"/ExtractFailure.txt",e.ToString());EditorApplication.Exit(1);}
    }
}
#endif
