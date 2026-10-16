#if UNITY_EDITOR
using System;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEngine;
public static class EveSceneEntityKeyValidator
{
    [MenuItem("Project N/Validate Scene Entity Metadata")]
    public static void ValidateOpenScenes()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Validate authored metadata in Edit Mode.");
        var loaders=UnityEngine.Object.FindObjectsByType<EveEntityLoader>(FindObjectsInactive.Include).Where(x=>x.gameObject.scene.IsValid()).ToArray();
        EveSceneEntityKey.ValidateSet(loaders.Select(x=>x.GetComponent<EveSceneEntityKey>()));
        foreach(var loader in loaders)if(loader.gameObject.activeSelf)throw new InvalidOperationException("Authored entity must be inactive before enrollment: "+loader.name);
        Debug.Log("Validated "+loaders.Length+" authored entity keys and gates.");
    }
}
#endif
