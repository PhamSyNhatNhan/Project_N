#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// G13 (Docs/SandboxPlan.md): builds the catalog prefabs of the two training dummies from the scene objects of the Hall (open `PNHall.unity` first). The scene objects are copied, never changed or saved: the copy loses
// what only an authored scene entity has (`EveSceneEntityKey`, `PNSceneEntityRole`) and the old scene shadow (the factory gives every body a `PNGroundShadow`), is made active and saved as a prefab, and the catalog
// asset gets one entry per prefab. Run once through the menu; running it again replaces the prefabs in place (same GUIDs).
public static class PNDummyPrefabBuilder
{
    private const string Folder = "Assets/Game/Entity/Enemy/Dummy/Prefab", Catalog = "Assets/Resources/PNEntities/PNEntityPrefabs.asset";
    private static readonly (string scene, string file, string key)[] Dummies =
    {
        ("Dummy", "Dummy.prefab", "project_n:dummy"),
        ("Dummy Lorica", "DummyLorica.prefab", "project_n:dummy_lorica")
    };

    [MenuItem("Project N/Build/Dummy Prefabs")]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Game/Scenes/Shared/PNHall/PNHall.unity") throw new System.InvalidOperationException("Open PNHall.unity first.");
        Directory.CreateDirectory(Folder);
        var catalog = AssetDatabase.LoadAssetAtPath<PNEntityPrefabCatalog>(Catalog) ?? throw new System.InvalidOperationException("The entity prefab catalog is missing.");
        var entries = catalog.Entries.ToList();
        foreach (var (name, file, key) in Dummies)
        {
            var source = scene.GetRootGameObjects().FirstOrDefault(x => x.name == name) ?? throw new System.InvalidOperationException("The Hall has no object " + name + ".");
            var copy = Object.Instantiate(source);
            copy.name = file.Replace(".prefab", "");
            foreach (var sceneOnly in copy.GetComponents<Component>().Where(c => c != null && (c.GetType().Name == "EveSceneEntityKey" || c.GetType().Name == "PNSceneEntityRole")).ToArray()) Object.DestroyImmediate(sceneOnly);
            foreach (var shadow in copy.GetComponentsInChildren<Transform>(true).Where(t => t != copy.transform && t.name.EndsWith("Ground Shadow")).ToArray()) Object.DestroyImmediate(shadow.gameObject);
            copy.transform.position = Vector3.zero; copy.transform.rotation = Quaternion.identity;
            copy.SetActive(true);
            var path = Folder + "/" + file;
            var prefab = PrefabUtility.SaveAsPrefabAsset(copy, path);
            Object.DestroyImmediate(copy);
            int at = entries.FindIndex(e => e.Key == key);
            var entry = new PNEntityPrefabCatalog.Entry { Key = key, Prefab = prefab };
            if (at >= 0) entries[at] = entry; else entries.Add(entry);
            Debug.Log("Dummy prefab " + path + " -> " + key);
        }
        catalog.Entries = entries.ToArray();
        EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
    }
}
#endif
