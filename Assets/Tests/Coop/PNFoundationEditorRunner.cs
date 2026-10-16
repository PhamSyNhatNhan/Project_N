#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEditor.Build.Player;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace ProjectN.Coop.Tests
{
    [InitializeOnLoad]
    public static class PNFoundationEditorRunner
    {
        public const string ScenePath="Assets/Tests/Coop/PNFoundationFixture.unity";
        private const string Key="ProjectN.P1.Foundation.Settings";
        [Serializable] private sealed class Settings{public string start;public bool enabled;public int options;public int fps;public bool background;}
        static PNFoundationEditorRunner(){EditorApplication.playModeStateChanged+=Restore;}
        private static void Restore(PlayModeStateChange state)
        {
            if(state!=PlayModeStateChange.EnteredEditMode)return;
            string json=SessionState.GetString(Key,"");if(json.Length==0)return;
            var settings=JsonUtility.FromJson<Settings>(json);EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(settings.start);
            EditorSettings.enterPlayModeOptionsEnabled=settings.enabled;EditorSettings.enterPlayModeOptions=(EnterPlayModeOptions)settings.options;
            Application.targetFrameRate=settings.fps;Application.runInBackground=settings.background;SessionState.EraseString(Key);
            File.WriteAllText(".utmp/P1S4S10/Restored.txt","scene="+SceneManager.GetActiveScene().path+" dirty="+SceneManager.GetActiveScene().isDirty+" time="+Time.timeScale);
        }
        public static void CreateFixture()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit required.");
            var original=SceneManager.GetActiveScene();bool dirty=original.isDirty;
            EditorSceneManager.SaveScene(original,ScenePath,true);
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            try
            {
                var root=new GameObject("P1 disposable native fixture");SceneManager.MoveGameObjectToScene(root,scene);root.AddComponent<PNFoundationPlayChecks>();
                EditorSceneManager.SaveScene(scene);
            }
            finally{EditorSceneManager.CloseScene(scene,true);SceneManager.SetActiveScene(original);}
            if(original.isDirty!=dirty)throw new Exception("Authored dirty changed.");
        }
        public static void PreparePlay(bool noDomainReload)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Idle required.");
            if(SessionState.GetString(Key,"").Length>0)return; // Dynamic command retries must not overwrite the original settings.
            var settings=new Settings{start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),enabled=EditorSettings.enterPlayModeOptionsEnabled,options=(int)EditorSettings.enterPlayModeOptions,fps=Application.targetFrameRate,background=Application.runInBackground};
            SessionState.SetString(Key,JsonUtility.ToJson(settings));Application.runInBackground=true;Application.targetFrameRate=60;EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            EditorSettings.enterPlayModeOptionsEnabled=true;EditorSettings.enterPlayModeOptions=noDomainReload?EnterPlayModeOptions.DisableDomainReload:EnterPlayModeOptions.None;
        }
        public static void CompileAndroid()=>CompileAndroid(".utmp/P1S4S10");
        public static void CompileAndroid(string folder)
        {
            Directory.CreateDirectory(folder);
            var result=PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings{target=BuildTarget.Android,group=BuildTargetGroup.Android,options=ScriptCompilationOptions.DevelopmentBuild},Path.Combine(folder,"AndroidScripts"));
            File.WriteAllText(Path.Combine(folder,"AndroidCompile.txt"),"assemblies="+result.assemblies.Count+" (script compilation only, no IL2CPP/device evidence)");
        }
    }
}
#endif
