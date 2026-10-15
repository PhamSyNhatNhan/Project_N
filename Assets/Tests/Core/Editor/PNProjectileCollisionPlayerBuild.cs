#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// EN: Build only a new disposable scene; never save original scenes or change build-scene settings.
// VI: Chỉ build scene thử mới; không lưu scene gốc hay đổi danh sách scene build.
public static class PNProjectileCollisionPlayerBuild
{
    public static string Queue()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)throw new InvalidOperationException("Idle Edit required");
        if(SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Original scene must be clean; test will not save it");
        Directory.CreateDirectory(".utmp/ProjectileCollisionPlayer");
        File.WriteAllText(".utmp/ProjectileCollisionPlayer/BuildProgress.txt","Queued normal Mono Windows64 build");
        Build();return File.ReadAllText(".utmp/ProjectileCollisionPlayer/BuildResult.txt");
    }
    private static void Build()
    {
        var original=SceneManager.GetActiveScene();bool dirty=original.isDirty;string scenes=string.Join("|",EditorBuildSettings.scenes.Select(x=>x.path+":"+x.enabled));
        Scene temporary=default;string prefix="Assets/Tests/Generated/PNCollision_"+Guid.NewGuid().ToString("N"),scenePath=prefix+".unity",materialPath=prefix+".mat";
        try
        {
            if(!AssetDatabase.IsValidFolder("Assets/Tests/Generated"))AssetDatabase.CreateFolder("Assets/Tests","Generated");
            temporary=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            var root=GameObject.CreatePrimitive(PrimitiveType.Quad);root.name="Disabled benchmark shader dependency";SceneManager.MoveGameObjectToScene(root,temporary);
            UnityEngine.Object.DestroyImmediate(root.GetComponent<Collider>());
            var material=new Material(Shader.Find("Hidden/ProjectN/ProjectileBenchmark")){enableInstancing=true};AssetDatabase.CreateAsset(material,materialPath);AssetDatabase.SaveAssetIfDirty(material);
            root.GetComponent<MeshRenderer>().sharedMaterial=material;root.SetActive(false);
            if(!EditorSceneManager.SaveScene(temporary,scenePath))throw new Exception("Cannot save disposable fixture scene");
            SceneManager.SetActiveScene(original);
            File.WriteAllText(".utmp/ProjectileCollisionPlayer/BuildProgress.txt","Building normal StandaloneWindows64 Player (no development/profiler options)");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scenePath},locationPathName=".utmp/ProjectileCollisionPlayer/Build/CollisionBenchmark.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
            File.WriteAllText(".utmp/ProjectileCollisionPlayer/BuildResult.txt",report.summary.result+"; errors="+report.summary.totalErrors+"; warnings="+report.summary.totalWarnings+"; bytes="+report.summary.totalSize+"; duration="+report.summary.totalTime);
            if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Benchmark Player build failed");
        }
        catch(Exception error){File.WriteAllText(".utmp/ProjectileCollisionPlayer/BuildFailure.txt",error.ToString());}
        finally
        {
            if(temporary.IsValid() && temporary.isLoaded)EditorSceneManager.CloseScene(temporary,true);
            if(original.IsValid())SceneManager.SetActiveScene(original);
            AssetDatabase.DeleteAsset(scenePath);AssetDatabase.DeleteAsset(materialPath);
            File.WriteAllText(".utmp/ProjectileCollisionPlayer/EditorBuildAudit.txt","scene="+original.path+";dirtyBefore="+dirty+";dirtyAfter="+original.isDirty+";buildScenesUnchanged="+(scenes==string.Join("|",EditorBuildSettings.scenes.Select(x=>x.path+":"+x.enabled)))+";target="+EditorUserBuildSettings.activeBuildTarget+";playing="+EditorApplication.isPlaying+";timeScale="+Time.timeScale+";Game/Simulator and Play Maximized not changed");
        }
    }
}
#endif
