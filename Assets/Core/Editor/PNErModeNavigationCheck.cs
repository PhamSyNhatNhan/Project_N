#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Pathfinding;
public static class PNErModeNavigationCheck
{
    public static string Run()
    {
        if(AstarPath.active!=null)throw new InvalidOperationException("Idle navigation required");
        var scene=EditorSceneManager.NewPreviewScene();GameObject root=null;
        try
        {
            root=new GameObject("Owned navigation check");root.SetActive(false);SceneManager.MoveGameObjectToScene(root,scene);
            var astar=root.AddComponent<AstarPath>();astar.scanOnStartup=false;astar.threadCount=ThreadCount.None;root.SetActive(true);
            var cache=AssetDatabase.LoadAssetAtPath<PNNavigationCache>("Assets/Game/Scenes/Test/ErMode/ErModeNavigation.asset");cache.Validate();astar.data.DeserializeGraphs(cache.CopyGraphData());
            var a=astar.GetNearest(new Vector3(-2,0,0),NNConstraint.Walkable);var b=astar.GetNearest(Vector3.zero,NNConstraint.Walkable);
            if(a.node==null||b.node==null||Vector3.Distance(a.position,new Vector3(-2,0,0))>.4f||Vector3.Distance(b.position,Vector3.zero)>.4f||!PathUtilities.IsPathPossible(a.node,b.node))throw new Exception("Spawn-to-player route is unavailable");
            return "PASS ErMode cached navigation: walkable spawn/player endpoints connected, capsule profile0.2/1.4; temporary graph disposed";
        }
        finally{if(root!=null)UnityEngine.Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
    }
}
#endif
