#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PNLifecyclePolicyPlayerBuild
{
    public static string Build() => BuildPlayer(false);
    public static string BuildDevelopment() => BuildPlayer(true);
    private static string BuildPlayer(bool development)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Clean idle Edit required; no authored scene will be saved.");
        const string folder = ".utmp/LifecyclePolicyPlayer"; Directory.CreateDirectory(folder);
        string buildScenes = string.Join("|", EditorBuildSettings.scenes.Select(x => x.path + ":" + x.enabled));
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty; Scene temporary = default;
        string prefix = "Assets/Tests/Generated/PNPolicy_" + Guid.NewGuid().ToString("N"), scenePath = prefix + ".unity", materialPath = prefix + ".mat", buffMaterialPath = prefix + "_buff.mat";
        try
        {
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) != ScriptingImplementation.Mono2x) throw new InvalidOperationException("Fixture requires current Mono backend; does not change project settings.");
            if (!AssetDatabase.IsValidFolder("Assets/Tests/Generated")) AssetDatabase.CreateFolder("Assets/Tests", "Generated");
            temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var root = GameObject.CreatePrimitive(PrimitiveType.Quad); root.name = "Inactive fixture shader dependency"; SceneManager.MoveGameObjectToScene(root, temporary);
            UnityEngine.Object.DestroyImmediate(root.GetComponent<Collider>());
            var material = new Material(Shader.Find("Hidden/ProjectN/ProjectileBenchmark")) { enableInstancing = true }; AssetDatabase.CreateAsset(material, materialPath); AssetDatabase.SaveAssetIfDirty(material);
            var buffMaterial = new Material(Shader.Find("Project_N/VFX/BuffElemental")); AssetDatabase.CreateAsset(buffMaterial, buffMaterialPath); AssetDatabase.SaveAssetIfDirty(buffMaterial);
            root.GetComponent<Renderer>().sharedMaterials = new[] { material, buffMaterial }; root.SetActive(false);
            if (!EditorSceneManager.SaveScene(temporary, scenePath)) throw new Exception("Disposable fixture scene save failed.");
            SceneManager.SetActiveScene(original);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath }, target = BuildTarget.StandaloneWindows64,
                locationPathName = folder + (development ? "/Development/LifecyclePolicy.exe" : "/Build/LifecyclePolicy.exe"), options = development ? BuildOptions.Development : BuildOptions.None });
            if(original.isDirty != dirty) throw new Exception("Build changed authored scene dirty state.");
            string result = report.summary.result + "; errors=" + report.summary.totalErrors + "; warnings=" + report.summary.totalWarnings + "; bytes=" + report.summary.totalSize;
            File.WriteAllText(folder + "/BuildResult.txt", result);
            if (report.summary.result != BuildResult.Succeeded) throw new Exception(result);
            return result;
        }
        catch (Exception error) { File.WriteAllText(folder + "/BuildFailure.txt", error.ToString()); throw; }
        finally
        {
            if (temporary.IsValid() && temporary.isLoaded) EditorSceneManager.CloseScene(temporary, true);
            if (original.IsValid()) SceneManager.SetActiveScene(original);
            AssetDatabase.DeleteAsset(scenePath); AssetDatabase.DeleteAsset(materialPath); AssetDatabase.DeleteAsset(buffMaterialPath);
            File.WriteAllText(folder + "/EditorAudit.txt", "scene=" + original.path + ";dirtyBefore=" + dirty + ";dirtyAfter=" + original.isDirty + ";buildScenesUnchanged=" +
                (buildScenes == string.Join("|", EditorBuildSettings.scenes.Select(x => x.path + ":" + x.enabled))) + ";Game/Simulator/PlayMaximized untouched;timeScale=" + Time.timeScale);
        }
    }
}
#endif
