#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EveEngine;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class PNDamagePopupEditorChecks
{
    public static string Run()
    {
        if (Application.isPlaying || EveRegistrySession.IsReady) throw new InvalidOperationException("Requires idle Edit Mode.");
        var scene = EditorSceneManager.NewPreviewScene(); bool dirty = SceneManager.GetActiveScene().isDirty;
        var owned = new List<GameObject>(); int checks = 0; bool started = false;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        // Popup checks isolate calculated damage from the separate iframe contract.
        PNDamageResult Hit(PNStat source, PNStat target, double amount, params EveId[] tags)
            => target.ReceiveDamage(source.CreateDamage(amount, true, 0d, tags));
        var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"),
            Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
        var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
        T Entity<T>(long id, string character) where T : PNStat
        {
            var go = new GameObject(typeof(T).Name); go.SetActive(false); owned.Add(go); SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse(character), 1);
            var stat = go.AddComponent<T>(); stat.PreviewDamagePopupsInEditor = true;
            go.AddComponent<EveEntityLoader>().Load(hub); go.SetActive(true); return stat;
        }
        try
        {
            EveRegistrySession.Begin(); started = true;
            EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:popup"));
            var player = Entity<PNPlayerStat>(970001, "project_n:eve");
            // Keep this fixture deterministic when the character's authored crit rate changes.
            player.SetSource(EveId.Parse("test:popup_base_rate"), new[] { new EveStatModifier(PNContentIds.CritRate, -player.Evaluate(PNContentIds.CritRate), 0) });
            var enemy = Entity<PNEnemyStat>(970002, "project_n:dummy");
            var hit = Hit(player, enemy, 10f, PNContentIds.Physical);
            var pool = enemy.DamagePopupPool; owned.Add(pool.gameObject);
            Check(pool.CountActive == 1 && pool.Active[0].DisplayText == "10", "Popup did not show calculated damage exactly once.");
            Check(hit.AppliedDamage == 10d && hit.CalculatedDamage == 10d, "Double HP did not lose exactly ten points at 200M.");
            Hit(enemy, player, 10f, PNContentIds.Physical);
            Check(player.DamagePopupPool == null && !player.CanShowDamagePopup, "Player damage spawned a popup.");
            var popup = pool.Active[0]; var position = popup.transform.position;
            Check(Mathf.Abs(position.x - (enemy.transform.position.x - .12f)) < .00001f, "First popup lane changed.");
            Check(Mathf.Abs(popup.transform.localScale.x - .48f) < .0001f, "Normal scale .4 did not combine with the initial pulse.");
            pool.Advance(.1f);
            Check(popup.transform.position.y > position.y && popup.Progress > 0f, "Popup does not rise/advance.");
            var block = new MaterialPropertyBlock(); popup.GetComponent<MeshRenderer>().GetPropertyBlock(block);
            Check(block.GetFloat("_EmissionIntensity") > 1f && block.GetFloat("_PopupFade") == 1f, "Popup did not hold its early-life HDR glow.");
            pool.Advance(.35f); popup.GetComponent<MeshRenderer>().GetPropertyBlock(block);
            Check(block.GetFloat("_PopupFade") > 0f && block.GetFloat("_PopupFade") < 1f, "Popup does not fade at the end of its lifetime.");
            pool.Advance(1f);
            Check(pool.CountActive == 0 && pool.CountInactive == 1, "Expired popup was not returned to its pool.");
            Hit(player, enemy, 25f, PNContentIds.Physical);
            Check(pool.CountActive == 1 && pool.Active[0] == popup && popup.DisplayText == "25" && popup.Progress == 0f, "Pool did not reset/reuse text and animation.");
            Check(Mathf.Abs(popup.transform.position.x - enemy.transform.position.x) < .00001f, "Second popup lane changed on reuse.");
            pool.Advance(1f);
            var clock = enemy.gameObject.AddComponent<EveTimeScale>();
            long pause = clock.AddTimedModifier(EveId.Parse("test:popup_pause"), EveTimeModifierKind.Scale, -1f, 2f);
            Hit(player, enemy, 10f, PNContentIds.Physical); pool.Advance(.2f);
            Check(pool.Active[0].Progress == 0f, "Popup ignored the receiver's clock pause.");
            clock.RemoveModifier(pause); pool.Advance(1f);
            for (int i = 0; i < 10; i++) Hit(player, enemy, 10f, PNContentIds.Physical);
            Check(pool.CountActive == 10 && pool.Active.All(p => p.DisplayText == "10"), "Ten delivered hits did not create ten independent popups.");
            pool.Advance(1f);
            player.SetSource(EveId.Parse("test:popup_crit"), new[] { new EveStatModifier(PNContentIds.CritRate, 100d, 0) });
            var critical = Hit(player, enemy, 25d, PNContentIds.Physical, PNContentIds.Lightning, PNContentIds.Fire, PNContentIds.Ice, PNContentIds.Fire);
            var critPopup = pool.Active[0];
            Check(critical.IsCritical && critical.CalculatedDamage == 37.5d && critPopup.DisplayText.StartsWith("<sprite name=\"crit\"") && critPopup.DisplayText.Contains("> 38 "),
                "Guaranteed critical damage did not reach the real TMP popup.");
            Check(critPopup.DisplayedElements.SequenceEqual(new[] { PNContentIds.Fire, PNContentIds.Ice, PNContentIds.Lightning }), "Elements were omitted, duplicated or reordered by input tags.");
            Check(Mathf.Abs(critPopup.transform.localScale.x - .54f) < .0001f && critPopup.IsCritical, "Critical scale .45 did not combine with the initial pulse.");
            var info = critPopup.GetComponent<TMPro.TextMeshPro>().textInfo;
            critPopup.GetComponent<MeshRenderer>().GetPropertyBlock(block);
            Check(critPopup.DisplayText.StartsWith("<sprite name=\"crit\" color=#" + ColorUtility.ToHtmlStringRGBA(block.GetColor("_FaceColor"))), "Crit sprite does not match the damage number color.");
            Check(info.characterInfo.Take(info.characterCount).Count(c => c.elementType == TMPro.TMP_TextElementType.Sprite) == 4 && info.characterCount == 10,
                "TMP did not render the crit icon, integer and three elemental sprites.");
            var sprites = info.characterInfo.Take(info.characterCount).Where(c => c.elementType == TMP_TextElementType.Sprite).ToArray();
            float critHeight = sprites[0].topRight.y - sprites[0].bottomLeft.y;
            float critCentre = (sprites[0].topRight.y + sprites[0].bottomLeft.y) * .5f;
            Check(sprites.Skip(1).All(c => Mathf.Abs((c.topRight.y - c.bottomLeft.y) / critHeight - .8f) < .001f &&
                Mathf.Abs((c.topRight.y + c.bottomLeft.y) * .5f - critCentre) < .001f), "Element icons are not 80% size and centred with the full-size crit glyph.");
            var iconRenderer = critPopup.GetComponentsInChildren<MeshRenderer>().First(r => r != critPopup.GetComponent<MeshRenderer>());
            iconRenderer.GetPropertyBlock(block);
            Check(block.GetFloat("_EmissionIntensity") == critPopup.CriticalGlowIntensity && block.GetFloat("_PopupFade") == 1f, "Elemental sprite renderer missed the shared HDR/fade state.");
            pool.Advance(1f); player.RemoveSource(EveId.Parse("test:popup_crit"));
            Hit(player, enemy, 1.25d, PNContentIds.Physical);
            Check(pool.Active[0] == critPopup && !critPopup.IsCritical && critPopup.DisplayText == "1" && critPopup.DisplayedElements.Length == 0,
                "Reused critical/element popup retained old icons, scale or number formatting.");
            pool.Advance(1f);
            // A pool keeps its own prefab reference: a live pool skips prefab resolution, and a pool whose prefab was destroyed takes the full path
            // (fallback resolve) without losing its idle popups.
            Check(pool.PrefabAlive, "Pool reports a destroyed prefab while it is alive.");
            var cloneGo = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("PNUI/PNDamagePopup")); owned.Add(cloneGo); cloneGo.SetActive(false);
            var second = Entity<PNEnemyStat>(970003, "project_n:dummy"); second.DamagePopupPrefab = cloneGo.GetComponent<PNDamagePopup>();
            Hit(player, second, 11f, PNContentIds.Physical);
            var secondPool = second.DamagePopupPool; owned.Add(secondPool.gameObject);
            Check(secondPool.PrefabAlive && secondPool.CountActive == 1 && secondPool.Active[0].DisplayText == "11", "Prefab-owned pool did not show its first hit.");
            secondPool.Advance(1f);
            Check(secondPool.CountActive == 0 && secondPool.CountInactive == 1, "Prefab-owned pool did not return its popup.");
            UnityEngine.Object.DestroyImmediate(cloneGo);
            Check(!secondPool.PrefabAlive, "Pool still reports its destroyed prefab as alive.");
            Hit(player, second, 12f, PNContentIds.Physical);
            Check(secondPool.CountActive == 1 && secondPool.Active[0].DisplayText == "12", "Pool with a destroyed prefab stopped reusing its idle popup.");
            secondPool.Advance(1f);
            var wind = EveId.Parse("mod:wind");
            var generic = pool.Show(12d, Vector3.zero, Color.white, null, false, new[] { wind });
            Check(generic.DisplayText.Contains("sprite name=\"element\"") && generic.DisplayText.Contains("mod:wind"), "Unmapped mod element was silently dropped.");
            pool.Advance(1f);
            enemy.SetCurrent(PNContentIds.Health, 5f);
            // Edit Mode does not run ordinary MonoBehaviour destruction hooks; drive the loader's
            // actual lifecycle explicitly, just as the other disposable entity fixtures do.
            enemy.Died += _ => { enemy.GetComponent<EveEntityLoader>().Unload(); UnityEngine.Object.DestroyImmediate(enemy.gameObject); };
            var killed = Hit(player, enemy, 25f, PNContentIds.Physical);
            Check(killed.Killed && pool != null && pool.CountActive == 1 && pool.Active[0].DisplayText == "25", "Killing hit disappeared with the target or displayed clamped HP loss.");
            pool.Advance(1f); Check(pool == null, "Retired owner pool did not clean up after the final popup.");
            Check(PNDamagePopup.FormatDamage(199999984d) == "199999984" && PNDamagePopup.FormatDamage(1.25d) == "1" &&
                PNDamagePopup.FormatDamage(37.5d) == "38" && PNDamagePopup.FormatDamage(2147483648.75d) == "2147483649", "Popup formatter did not round to integers beyond Int32 range.");
        }
        finally
        {
            foreach (var go in owned) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            if (started) EveRegistrySession.End(); EditorSceneManager.ClosePreviewScene(scene);
        }
        Check(SceneManager.GetActiveScene().isDirty == dirty, "Popup test changed gameplay scene.");
        return "PASS " + checks + " native popup checks: calculated damage, player suppression, ten hits, rise/fade/HDR, pool reuse, pause and killing-hit lifetime.";
    }
}
#endif
