#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class PNBootstrapPlayRunner
{
    private const string Key="ProjectN.S3.TestSettings";
    [Serializable] private sealed class Settings { public string start; public bool enabled; public int options; }
    static PNBootstrapPlayRunner() { EditorApplication.playModeStateChanged+=Restore; }
    private static void Restore(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredEditMode) return;
        string json=SessionState.GetString(Key,""); if(json.Length==0)return;
        var settings=JsonUtility.FromJson<Settings>(json);
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(settings.start);
        EditorSettings.enterPlayModeOptionsEnabled=settings.enabled;
        EditorSettings.enterPlayModeOptions=(EnterPlayModeOptions)settings.options;
        SessionState.EraseString(Key);
        File.WriteAllText(".utmp/P1S3/SettingsRestored.txt","Play start scene and enter-play settings restored; active="+SceneManager.GetActiveScene().path+" dirty="+SceneManager.GetActiveScene().isDirty);
    }
    public static void Prepare(bool disableDomainReload)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetString(Key,"").Length!=0) throw new InvalidOperationException("Idle Editor required.");
        var original=SceneManager.GetActiveScene(); bool dirty=original.isDirty;
        const string path="Assets/Tests/EveEngine/PNBootstrapPlayFixture.unity";
        if(!File.Exists(path))throw new InvalidOperationException("Explicit save-as-copy backup required first.");
        var copy=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
        try
        {
            bool found=false; foreach(var root in copy.GetRootGameObjects()) if(root.GetComponent<PNBootstrapPlayChecks>()!=null)found=true;
            if(!found) { var root=new GameObject("S3 disposable verification");SceneManager.MoveGameObjectToScene(root,copy);root.AddComponent<PNBootstrapPlayChecks>(); }
            EditorSceneManager.SaveScene(copy);
        }
        finally {EditorSceneManager.CloseScene(copy,true);SceneManager.SetActiveScene(original);}
        if(original.isDirty!=dirty)throw new Exception("Original dirty flag changed.");
        var settings=new Settings {start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),enabled=EditorSettings.enterPlayModeOptionsEnabled,options=(int)EditorSettings.enterPlayModeOptions};
        SessionState.SetString(Key,JsonUtility.ToJson(settings));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        EditorSettings.enterPlayModeOptionsEnabled=true;
        EditorSettings.enterPlayModeOptions=disableDomainReload?EnterPlayModeOptions.DisableDomainReload:EnterPlayModeOptions.None;
    }
}
#endif
