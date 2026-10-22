#if UNITY_EDITOR
using System;
using System.IO;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PNEnemyPrefabAuthoring
{
    public const string PrefabPath = "Assets/Game/Entity/Enemy/Sentinel/Sentinel.prefab";
    [MenuItem("Tools/Project N/Enemy/Create Sentinel example prefab")]
    public static void CreateSentinel()
    {
        if (Application.isPlaying || File.Exists(PrefabPath)) throw new InvalidOperationException("Create once in Edit Mode; do not overwrite an authored prefab.");
        var original = SceneManager.GetActiveScene(); bool dirty = original.isDirty;
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath)); AssetDatabase.Refresh();
            var root = new GameObject("Sentinel"); root.layer = 7; SceneManager.MoveGameObjectToScene(root, preview);
            root.AddComponent<EveEntityIdentity>(); root.AddComponent<PNEnemyStat>(); root.AddComponent<PNAbilityManager>();
            var clock = root.AddComponent<EveTimeScale>(); clock.MinScale = 0;
            var body = root.AddComponent<Rigidbody>(); body.constraints = RigidbodyConstraints.FreezeRotation; body.interpolation = RigidbodyInterpolation.Interpolate;
            var capsule = root.AddComponent<CapsuleCollider>(); capsule.radius = .2f; capsule.height = 1.4f; capsule.center = Vector3.up * .7f;
            var move = root.AddComponent<PNEnemyMove>(); move.GroundLayers = 1 << 13; move.CanJump = false;
            root.AddComponent<PNSentinelSkill>().ConfigureInitialBehavior("melee");
            root.AddComponent<PNEnemyMeleeBehavior>().ConfigureId("melee"); root.AddComponent<PNEnemyChargeBehavior>().ConfigureId("charge");
            root.AddComponent<EveEntityLoader>();
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule); visual.name = "Placeholder body";
            visual.transform.SetParent(root.transform, false); visual.transform.localPosition = Vector3.up * .7f; visual.transform.localScale = new Vector3(.4f, .7f, .4f);
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(preview);
            if (original.IsValid() && original.isDirty != dirty) throw new InvalidOperationException("Enemy authoring changed the original scene dirty state.");
        }
    }
}
#endif
