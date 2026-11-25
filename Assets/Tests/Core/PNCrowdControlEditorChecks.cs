#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using EveEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Docs/BodyMotionPlan.md M3, Edit Mode: crowd_control is one combat tag. The registered input goes through the sender Scale route (captured with the hit together with its penetration), then through the receiver
// Mitigation route; the result is a factor with no upper limit and never negative. The examples of B6/B7 are checked number by number.
public static class PNCrowdControlEditorChecks
{
    public static string Run()
    {
        if (EditorApplication.isPlaying || EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle stopped Edit Mode required.");
        int n = 0; void Check(bool ok, string what) { if (!ok) throw new Exception("Crowd control check " + n + ": " + what); n++; }
        bool Near(double a, double b) => Math.Abs(a - b) < 1e-9;
        bool Throws(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } catch (InvalidOperationException) { return true; } }
        var preview = EditorSceneManager.NewPreviewScene();
        PNStat Entity(EveRegistryHub hub, long id)
        {
            var root = new GameObject("Crowd control entity"); root.SetActive(false); SceneManager.MoveGameObjectToScene(root, preview);
            root.AddComponent<EveEntityIdentity>().Assign(new EveEntityId(id), EveId.Parse("project_n:dummy"), 0);
            var stat = root.AddComponent<PNEnemyStat>(); root.AddComponent<EveEntityLoader>().Load(hub); root.SetActive(true); root.GetComponent<PNBuffManager>().Advance(0); return stat;
        }
        try
        {
            var docs = new PNGameContentSource(Path.Combine(Application.streamingAssetsPath, "BaseContent"), Path.Combine(Application.persistentDataPath, "Mods"), Array.Empty<string>()).ReadDocuments().ToArray();
            var hub = new EveContentBootstrap().Build(new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }, docs);
            EveRegistrySession.Begin(); EveRegistrySession.Start(hub, EveSessionRole.Offline, EveContentCompatibility.Create(hub, docs, "test:crowd_control"));
            var source = Entity(hub, 998701); var target = Entity(hub, 998702); var key = EveId.Parse("test:cc_modifier");
            var cc = PNContentIds.CrowdControl; var alias = EveId.Parse("base:crowd_control");
            double Factor(double input) => target.ReceiveCrowdControl(source.CreateCrowdControl(input));
            Check(hub.Get<PNTagDefinition>().TryGet(cc, out _) && hub.Get<PNTagDefinition>().TryGet(alias, out _), "the tag and its base alias are registered");

            Check(Near(Factor(1.1), 1.1), "no modifier: the factor is the registered input (1.1)");
            Check(Near(Factor(0), 0), "an input of 0 stays 0");

            // Receiver (Mitigation).
            target.SetSource(key, new[] { new EveStatModifier(cc, -1, 0, PNContentIds.Mitigation) });
            Check(Near(Factor(1.1), .1), "dash: Mitigation group 0 -1 gives 1.1 - 1 = 0.1 (a light push survives)");
            target.SetSource(key, new[] { new EveStatModifier(cc, -.5, 1, PNContentIds.Mitigation) });
            Check(Near(Factor(1.1), .55), "boss: Mitigation group 1 -50% gives 0.55");
            target.SetSource(key, new[] { new EveStatModifier(alias, -.5, 1, PNContentIds.Mitigation) });
            Check(Near(Factor(1.1), .55), "the base: alias reaches the same tag");
            target.SetSource(key, new[] { new EveStatModifier(cc, -1, 1, PNContentIds.Mitigation) });
            Check(Near(Factor(1.1), 0), "Mitigation -100% gives 0 (the effect does not happen)");
            target.SetSource(key, new[] { new EveStatModifier(cc, -5, 0, PNContentIds.Mitigation) });
            Check(Near(Factor(1.1), 0), "a flat larger than the value is clamped to 0, never negative");
            target.SetSource(key, new[] { new EveStatModifier(cc, 2, 0, PNContentIds.Mitigation, PNContentIds.Before), new EveStatModifier(cc, -.5, 1, PNContentIds.Mitigation), new EveStatModifier(cc, 1, 0, PNContentIds.Mitigation) });
            Check(Near(Factor(1), (1 + 2) * .5 + 1), "before flat, group multiplier and after flat follow the shared formula ((1+2)*0.5+1)");
            target.SetSource(key, new[] { new EveStatModifier(PNContentIds.Fire, -1, 1, PNContentIds.Mitigation), new EveStatModifier(PNContentIds.Heal, -1, 0, PNContentIds.Mitigation) });
            Check(Near(Factor(1.1), 1.1), "other tags (fire, heal) do not touch crowd control");
            target.RemoveSource(key);

            // Sender (Scale): increases the control.
            source.SetSource(key, new[] { new EveStatModifier(cc, .3, 1, PNContentIds.Scale) });
            Check(Near(Factor(1.1), 1.43), "attacker: Scale group 1 +30% gives 1.43");
            source.SetSource(key, new[] { new EveStatModifier(cc, 9, 1, PNContentIds.Scale) });
            Check(Near(Factor(1.1), 11), "no upper limit (1.1 x 10 = 11)");
            source.SetSource(key, new[] { new EveStatModifier(cc, -1, 1, PNContentIds.Mitigation) });
            target.SetSource(key, new[] { new EveStatModifier(cc, 1, 1, PNContentIds.Scale) });
            Check(Near(Factor(1.1), 1.1), "the sender Mitigation and the receiver Scale are not applied to this hit");
            source.RemoveSource(key); target.RemoveSource(key);

            // Penetration travels with the hit and joins the receiver Mitigation for this hit only.
            source.SetSource(key, new[] { new EveStatModifier(cc, .5, 1, PNContentIds.Penetration) });
            target.SetSource(key, new[] { new EveStatModifier(cc, -.5, 1, PNContentIds.Mitigation) });
            Check(Near(Factor(1.1), 1.1), "penetration +50% cancels the receiver -50% (same group)");
            var pierce = source.CreateCrowdControl(1.1);
            Check(pierce.Penetration.Count == 1 && Near(pierce.Value, 1.1), "the hit carries the Scale-processed value and the penetration");
            source.RemoveSource(key);
            Check(Near(target.ReceiveCrowdControl(pierce), 1.1), "the penetration stays with the hit after the sender loses it");
            Check(Near(Factor(1.1), .55), "and it is not stored on the receiver (the next hit without it is -50%)");
            target.RemoveSource(key);

            // The captured snapshot (a source that no longer lives as a stat) gives the same numbers.
            source.SetSource(key, new[] { new EveStatModifier(cc, .3, 1, PNContentIds.Scale), new EveStatModifier(cc, .2, 1, PNContentIds.Penetration) });
            var snapshot = source.CaptureCombatSource(); source.RemoveSource(key);
            var captured = snapshot.CreateCrowdControl(1.1);
            target.SetSource(key, new[] { new EveStatModifier(cc, -.5, 1, PNContentIds.Mitigation) });
            Check(Near(captured.Value, 1.43) && Near(target.ReceiveCrowdControl(captured), 1.43 * .7), "a captured snapshot keeps Scale and penetration after the source changed (1.43 x (1-0.5+0.2))");
            target.RemoveSource(key);

            // Contract.
            Check(Throws(() => source.CreateCrowdControl(-1)) && Throws(() => source.CreateCrowdControl(double.NaN)) && Throws(() => source.CreateCrowdControl(double.PositiveInfinity)), "a negative or non-finite input is rejected");
            Check(Throws(() => target.ReceiveCrowdControl(null)), "a null hit is rejected");
            Check(Throws(() => new PNCrowdControl(Guid.Empty, source.EntityId, 1, 1, null)), "a hit with no session is rejected");
        }
        finally
        {
            try
            {
                foreach (var r in preview.GetRootGameObjects()) if (r != null) foreach (var l in r.GetComponentsInChildren<EveEntityLoader>(true)) { try { l.Unload(); } catch (Exception) { } }
                foreach (var r in preview.GetRootGameObjects()) if (r != null) r.SetActive(false);
            }
            finally { try { if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End(); } finally { EditorSceneManager.ClosePreviewScene(preview); } }
        }
        return "PASS " + n + " crowd control checks";
    }
}
#endif
