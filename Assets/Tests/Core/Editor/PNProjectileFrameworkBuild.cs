#if UNITY_EDITOR
using System;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

// EN: Build a normal Mono Windows Player containing only an empty disposable scene (never touches authored scenes). Run in a clone:
//     Unity -batchmode -projectPath <clone> -executeMethod PNProjectileFrameworkBuild.Build -pn-projectile-framework-build=<output.exe>
// VI: Build Player Mono thường chỉ chứa scene rỗng dùng một lần; chạy trong clone.
public static class PNProjectileFrameworkBuild
{
    public static void Build()
    {
        string target = null;
        foreach (var argument in Environment.GetCommandLineArgs()) if (argument.StartsWith("-pn-projectile-framework-build=", StringComparison.Ordinal)) target = argument.Substring("-pn-projectile-framework-build=".Length);
        if (string.IsNullOrEmpty(target)) throw new ArgumentException("-pn-projectile-framework-build=<exe path> is required.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)));
        string scenePath = "Assets/Tests/Generated/PNProjectileFramework_" + Guid.NewGuid().ToString("N") + ".unity";
        int code = 0;
        try
        {
            if (!AssetDatabase.IsValidFolder("Assets/Tests/Generated")) AssetDatabase.CreateFolder("Assets/Tests", "Generated");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new Exception("Cannot save the disposable scene.");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scenePath }, locationPathName = target, target = BuildTarget.StandaloneWindows64, options = Environment.GetCommandLineArgs().Contains("-pn-projectile-framework-dev") ? BuildOptions.Development : BuildOptions.None });
            File.WriteAllText(Path.ChangeExtension(target, ".buildresult.txt"), report.summary.result + "; errors=" + report.summary.totalErrors + "; warnings=" + report.summary.totalWarnings + "; bytes=" + report.summary.totalSize);
            if (report.summary.result != BuildResult.Succeeded) code = 1;
        }
        catch (Exception error) { File.WriteAllText(Path.ChangeExtension(target, ".buildfailure.txt"), error.ToString()); code = 1; }
        finally { AssetDatabase.DeleteAsset(scenePath); }
        EditorApplication.Exit(code);
    }
}
#endif
