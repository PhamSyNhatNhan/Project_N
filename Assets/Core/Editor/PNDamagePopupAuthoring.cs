#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class PNDamagePopupAuthoring
{
    public static string CreateDefaults()
    {
        const string folder = "Assets/Resources/PNUI";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Resources", "PNUI");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath("14631e750c35ca24a8c8834f3c45a15c"));
        if (font == null) font = TMP_Settings.defaultFontAsset;
        if (font == null) throw new System.InvalidOperationException("Popup needs a TMP font.");
        var shader = Shader.Find("ProjectN/DamagePopupSDF");
        if (shader == null) throw new System.InvalidOperationException("Popup SDF shader failed to import.");
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null, bloomRoot = null;
        try
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/PNDamagePopup.prefab") == null)
            {
                var material = new Material(font.material) { name = "PN Damage Popup HDR", shader = shader };
                material.SetFloat("_EmissionIntensity", 7f); material.SetFloat("_PopupFade", 1f);
                material.SetFloat("_OutlineWidth", .12f); material.EnableKeyword("OUTLINE_ON");
                material.SetColor("_OutlineColor", new Color(.01f, .025f, .035f, 1f));
                AssetDatabase.CreateAsset(material, folder + "/PNDamagePopup.mat");
                root = new GameObject("PNDamagePopup", typeof(RectTransform));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
                root.SetActive(false);
                root.transform.localScale = Vector3.one * .4f;
                var text = root.AddComponent<TextMeshPro>();
                text.font = font; text.fontSharedMaterial = material; text.fontSize = 4.5f;
                text.alignment = TextAlignmentOptions.Center; text.text = "10";
                text.rectTransform.sizeDelta = new Vector2(4f, 1f); text.raycastTarget = false;
                text.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                text.GetComponent<MeshRenderer>().receiveShadows = false;
                var popup = root.AddComponent<PNDamagePopup>();
                popup.Icons = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(folder + "/PNDamagePopupIcons.asset");
                text.spriteAsset = popup.Icons; text.textWrappingMode = TextWrappingModes.NoWrap;
                PrefabUtility.SaveAsPrefabAsset(root, folder + "/PNDamagePopup.prefab");
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/PNDamagePopupBloom.prefab") == null)
            {
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, folder + "/PNDamagePopupBloom.asset");
                var bloom = profile.Add<Bloom>(true); bloom.intensity.value = 1.2f; bloom.threshold.value = 1.1f;
                bloom.scatter.value = .65f; bloom.highQualityFiltering.value = true;
                AssetDatabase.AddObjectToAsset(bloom, profile); EditorUtility.SetDirty(profile);
                bloomRoot = new GameObject("PNDamagePopupBloom");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(bloomRoot, scene);
                bloomRoot.SetActive(false);
                var volume = bloomRoot.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = -100f;
                volume.sharedProfile = profile; volume.weight = 1f;
                bloomRoot.AddComponent<PNDamagePopupBloom>();
                PrefabUtility.SaveAsPrefabAsset(bloomRoot, folder + "/PNDamagePopupBloom.prefab");
            }
            AssetDatabase.SaveAssets();
            return "Popup TMP prefab, HDR material and Bloom profile ready at " + folder;
        }
        finally
        {
            if (root != null) Object.DestroyImmediate(root);
            if (bloomRoot != null) Object.DestroyImmediate(bloomRoot);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
#endif
