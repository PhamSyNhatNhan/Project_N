#if UNITY_EDITOR
using System;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Native check of the summon inheritance step (PNSummonInherit): a real Starforger star loaded in a preview scene under an Eve owner, Edit Mode, no Play Mode.
public static class PNSummonInheritEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle Editor required");
        var scene = EditorSceneManager.NewPreviewScene(); bool started = false; int n = 0; GameObject ownerObject = null, star = null;
        void Check(bool ok, string what) { if (!ok) throw new Exception("Summon inherit check " + n + ": " + what); n++; }
        try
        {
            var docs = new PNGameContentSource(Application.streamingAssetsPath + "/BaseContent", "unused", Array.Empty<string>()).ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
            EveRegistrySession.Begin(); started = true; EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:summon-inherit"));
            PNVisualPacks.Set("project_n", Application.streamingAssetsPath + "/BaseContent");
            ownerObject = new GameObject("Inherit owner"); ownerObject.SetActive(false); SceneManager.MoveGameObjectToScene(ownerObject, scene);
            ownerObject.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(978401), EveId.Parse("project_n:eve"), 0);
            var owner = ownerObject.AddComponent<PNPlayerStat>().AsPlayer(); ownerObject.AddComponent<Rigidbody>().isKinematic = true;
            ownerObject.AddComponent<EveEntityLoader>().Load(hub); ownerObject.SetActive(true);
            Check(owner.IsReady, "the owner is ready");
            owner.SetSource(EveId.Parse("test:owner_buff"), new[]
            {
                new EveStatModifier(PNContentIds.AttackPower, .5d, 1),                                                  // +50% attack: sits on a stat, already inside its final value
                new EveStatModifier(PNContentIds.CritRate, 20d, 0),                                                     // +20 crit rate (flat on a stat)
                new EveStatModifier(PNContentIds.ProjectileDamage, .25d, 1, PNContentIds.Scale),                        // +25% projectile damage: a tag modifier, copied as it is
            });
            double ownerAttack = owner.Evaluate(PNContentIds.AttackPower), ownerCrit = owner.Evaluate(PNContentIds.CritRate), ownerCritDamage = owner.Evaluate(PNContentIds.CritDamage);
            var ownerSource = PNCombatSource.Capture(owner);

            int made = 0;
            PNStat Make(string label, PNCombatSource source, bool withStep)
            {
                star = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Entity/Enemy/Starforger/Prefab/StarforgerStar.prefab"));
                star.name = label; star.SetActive(false); SceneManager.MoveGameObjectToScene(star, scene);
                star.GetComponent<EveEntityIdentity>().Assign(new EveEntityId(978402 + ++made), PNStarforgerCombatContent.StarCharacter, 0);
                if (withStep && star.GetComponent<PNSummonInherit>() == null) star.AddComponent<PNSummonInherit>();
                else if (!withStep && star.GetComponent<PNSummonInherit>() != null) UnityEngine.Object.DestroyImmediate(star.GetComponent<PNSummonInherit>());   // the prefab carries the step: take it off for the "plain summon" case
                if (source != null) (star.GetComponent<PNSummon>() ?? star.AddComponent<PNSummon>()).Begin(source, owner.EntityId, "inherit", PNSummons.NextSequence(), 1d);
                star.GetComponent<EveEntityLoader>().Load(hub); star.SetActive(true);
                return star.GetComponent<PNStat>();
            }
            void Drop() { var summon = star.GetComponent<PNSummon>(); summon?.Clear(); star.GetComponent<EveEntityLoader>()?.Unload(); UnityEngine.Object.DestroyImmediate(star); star = null; }

            // Without the step the summon keeps its own stats (the character declares attack power 1).
            var plain = Make("Star plain", ownerSource, false);
            Check(plain.IsReady && Math.Abs(plain.Evaluate(PNContentIds.AttackPower) - 1d) < 1e-9, "without the step the summon uses its own attack power (1)");
            Drop();

            // With the step: stats take the owner's final values; tag modifiers are copied; resources are not.
            var inherited = Make("Star inherits", ownerSource, true); var step = star.GetComponent<PNSummonInherit>();
            Check(inherited.IsReady && step.Count >= 3, "the step installed its modifiers (" + step.Count + ")");
            Check(Math.Abs(inherited.Evaluate(PNContentIds.AttackPower) - ownerAttack) < 1e-6, "attack power equals the owner's final value " + ownerAttack + " (got " + inherited.Evaluate(PNContentIds.AttackPower) + ")");
            Check(Math.Abs(inherited.Evaluate(PNContentIds.CritRate) - ownerCrit) < 1e-6 && Math.Abs(inherited.Evaluate(PNContentIds.CritDamage) - ownerCritDamage) < 1e-6, "crit rate and crit damage equal the owner's");
            var ownerSnapshot = ownerSource.Snapshot; var summonSnapshot = PNCombatSource.Capture(inherited).Snapshot;
            Check(Math.Abs(summonSnapshot.Calculate(100d, PNContentIds.ProjectileDamage) - ownerSnapshot.Calculate(100d, PNContentIds.ProjectileDamage)) < 1e-6 && ownerSnapshot.Calculate(100d, PNContentIds.ProjectileDamage) > 100d,
                "the projectile_damage tag modifier was copied (same scaled number as the owner)");
            Check(!inherited.Set.TryGetBase(PNContentIds.Health, out _), "no resource was created or copied");
            var sources = inherited.Set.SourceSnapshot();
            Check(sources.ContainsKey(PNSummonInherit.SourceId) && sources[PNSummonInherit.SourceId].All(m => !ownerSnapshot.IsStat(m.Id) || m.Group == 0), "stat modifiers were not copied twice (only flat mirrors sit on stats)");
            Check(!sources.ContainsKey(EveId.Parse("project_n:summon_inherit_trial")), "the trial source is gone");
            // The owner changing afterwards does not reach the summon (the snapshot was taken at the summon).
            owner.SetSource(EveId.Parse("test:owner_buff"), Array.Empty<EveStatModifier>());
            Check(Math.Abs(inherited.Evaluate(PNContentIds.AttackPower) - ownerAttack) < 1e-6, "a later change of the owner does not reach the summon");
            Drop();

            // No owner snapshot (a client, or a spawn that skipped the summoning skill): the step does nothing and the summon keeps its own stats.
            var orphan = Make("Star orphan", null, true);
            Check(orphan.IsReady && star.GetComponent<PNSummonInherit>().Count == 0 && Math.Abs(orphan.Evaluate(PNContentIds.AttackPower) - 1d) < 1e-9, "without an owner snapshot nothing is inherited");
            Drop();
            return "PASS " + n + " summon inherit checks (stats mirrored once, tag modifiers copied, resources and trial source left out, later owner change ignored, no owner = nothing; real star in a preview scene)";
        }
        finally
        {
            try { if (started) EveRegistrySession.End(); }
            finally
            {
                foreach (var o in new[] { star, ownerObject }) if (o != null) UnityEngine.Object.DestroyImmediate(o);
                EditorSceneManager.ClosePreviewScene(scene); PNVisualRuntime.Release(); PNVisualPacks.Clear();
            }
        }
    }
}
#endif
