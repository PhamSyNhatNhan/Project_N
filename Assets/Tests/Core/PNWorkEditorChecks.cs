#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

// Editor checks for the work of placeable things (Docs/SandboxPlan.md, "Thiết kế work của vật đặt"): the sample content registers and validates, the work Action's payload is validated against the registered work
// types, and the Action really breaks things of a grid world. `Run()` returns the report; the pure rules are in Tests/EveEngine/PNGridWorkTests.cs.
public static class PNWorkEditorChecks
{
    public static string Run()
    {
        var lines = new List<string>(); int failed = 0;
        void Check(bool ok, string what) { lines.Add((ok ? "PASS " : "FAIL ") + what); if (!ok) failed++; }
        var hub = new EveRegistryHub();
        foreach (var module in new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule() }) module.Register(hub);
        Check(hub.WorkTypes().Snapshot().ContainsKey(PNWorkIds.Pickaxe) && hub.WorkTypes().Snapshot().ContainsKey(PNWorkIds.Axe), "pickaxe and axe are registered work types");
        Check(hub.Actions().Snapshot().ContainsKey(PNGameplayContentModule.WorkAction), "the work action is registered");
        var things = hub.Instances().Snapshot().Values.OfType<PNPlaceableDefinition>().ToDictionary(x => x.Id);
        Check(things.TryGetValue(PNPlaceableIds.Tree, out var tree) && tree.Settings.Work != null && tree.Settings.Work.Types.Contains(PNWorkIds.Axe) && !tree.Settings.Breakable, "the sample tree takes axe work and has no health");
        Check(things.TryGetValue(PNPlaceableIds.Rock, out var rock) && rock.Settings.Work != null && rock.Settings.Work.MinPower == 50 && rock.Settings.Width == 2, "the sample rock needs pickaxe power 50");
        Check(things.TryGetValue(PNPlaceableIds.Wall, out var wall) && wall.Settings.Work != null && wall.Settings.Work.Enemy && wall.Settings.Breakable, "the sample wall takes work and damage and allows monsters");
        bool valid = true; try { foreach (var definition in hub.Instances().Snapshot().Values) definition.Validate(hub); } catch (Exception e) { valid = false; lines.Add("  " + e.Message); }
        Check(valid, "every registered instance validates against the registries");
        hub.Instances().Register(new PNPlaceableDefinition(EveId.Parse("test:bad_tree"), typeof(PNPlaceableVanishLogic), () => new PNPlaceableVanishLogic(),
            new JObject { ["footprint"] = new JObject { ["width"] = 1, ["depth"] = 1 }, ["breakable"] = false, ["work"] = new JObject { ["types"] = new JArray("test:shovel"), ["left"] = 5 } }));
        Check(ReferenceFails(hub), "a placeable naming an unregistered work type fails its reference check");
        Check(Ok(() => PNWorkAction.Validate(JObject.Parse("{\"types\":[{\"id\":\"project_n:pickaxe\",\"power\":60}],\"amount\":10,\"radius\":1.2}"), hub)), "a good work action payload validates");
        Check(Throws(() => PNWorkAction.Validate(JObject.Parse("{\"types\":[{\"id\":\"project_n:shovel\",\"power\":1}],\"amount\":1,\"radius\":1}"), hub), true), "a work action naming an unregistered work type is refused");
        Check(Throws(() => PNWorkAction.Validate(JObject.Parse("{\"types\":[{\"id\":\"project_n:axe\",\"power\":1}],\"amount\":1,\"radius\":1,\"value\":{\"amount\":5}}"), hub), true), "damage fields in a work action are refused");
        lines.Add(failed == 0 ? "ALL PASSED" : failed + " FAILED");
        return string.Join("\n", lines);
    }

    public static string RunActionModes()
    {
        if (EveRegistrySession.State != EveSessionState.Stopped) throw new InvalidOperationException("Idle session required.");
        int count = 0;
        void Check(bool passed, string label)
        {
            if (!passed) throw new InvalidOperationException(label);
            count++;
        }

        var scene = EditorSceneManager.NewPreviewScene();
        PNGridWorldHost host = null;
        PNGridWorldController controller = null;
        GameObject actor = null;
        PNTraversal traversal = null;
        try
        {
            var hub = new EveRegistryHub();
            foreach (var module in new EveIContentModule[] { new PNContentModule(), new PNGameplayContentModule(), new PNGridWorkReviewContent() })
                module.Register(hub);
            hub.Seal();
            EveRegistrySession.Begin();
            EveRegistrySession.Start(hub, EveSessionRole.Offline,
                EveContentCompatibility.Create(hub, Array.Empty<EveContentDocument>(), "test:o1_world_motion"));
            var world = EveRegistrySession.Context.Worlds.Create(EveId.Parse("test:world"), new string('a', 64));
            world.ConfigurePhysics(scene, manual: false);
            world.BeginLoading();
            world.CompleteLoading();

            var data = new PNGridWorldData(new PNGridWorldDescriptor(64, 64, .5f, "project_n:flat", 7), new[] { "test:ground" });
            host = PNGridWorldHost.Create(data, new[] { new Color32(32, 32, 32, 255) },
                new[] { PNTerrainIds.Land }, PNTerrainLayers.Assign(hub.Terrains().Snapshot().Values.Select(x => x.Id)));
            SceneManager.MoveGameObjectToScene(host.gameObject, scene);
            controller = PNGridWorldController.Create(world, host, new[] { true });
            var wall = hub.Instances().GetEffectiveKind<PNPlaceableDefinition>(PNPlaceableIds.Wall);
            Check(controller.Placements.TryPlace(wall, 6, 6, 0, null, out var placed, out _), "fixture wall placement");

            actor = new GameObject("O1 work and traversal actor");
            actor.SetActive(false);
            SceneManager.MoveGameObjectToScene(actor, scene);
            var identity = actor.AddComponent<EveEntityIdentity>();
            identity.Assign(new EveEntityId(912901), PNGridWorkReviewContent.Worker, 0);
            identity.BindWorld(world, "main");
            actor.AddComponent<PNPlayerStat>().AsPlayer();
            var skill = actor.AddComponent<PNSkill>();
            actor.AddComponent<EveEntityLoader>().Load(hub);

            (int Worked, int Broken) Work(PNSkillMode mode)
            {
                using var action = (PNWorkAction)skill.CreateAction(PNGridWorkReviewContent.Axe, out var ctx,
                    targets: null, direction: null, position: new Vector3(3.25f, 0, 3.25f));
                ctx.Mode = mode;
                action.Start(ctx);
                action.Perform(ctx);
                action.Exit(ctx);
                return (action.Worked, action.Broken);
            }

            foreach (var mode in new[] { PNSkillMode.Predicted, PNSkillMode.Observed })
            {
                var skipped = Work(mode);
                Check(skipped.Worked == 0 && skipped.Broken == 0 && placed.WorkLeft == 20, mode + " cannot work an authority placement");
            }
            using (identity.HoldGameplay())
                Check(Work(PNSkillMode.Authority).Worked == 0 && placed.WorkLeft == 20, "held actor cannot work a placement");
            Check(Work(PNSkillMode.Authority).Worked == 1 && placed.WorkLeft == 10, "authority deducts work");
            var broken = Work(PNSkillMode.Authority);
            Check(broken.Worked == 1 && broken.Broken == 1 && controller.Placements.At(6, 6) == null,
                "authority reports and removes a broken placement");

            var locomotionId = PNGameplayContentModule.LocomotionAction;
            var payload = new JObject { ["locomotion"] = PNTerrainIds.Air.ToString(), ["duration"] = .5 };
            var locomotionData = new PNSkillData(null, new PNSkillActionData(locomotionId, perform: payload));
            PNLocomotionAction BeginLocomotion(PNSkillMode mode, out PNSkillActionContext ctx)
            {
                ctx = new PNSkillActionContext(skill, PNGridWorkReviewContent.Axe, locomotionData, hub) { Mode = mode };
                var action = (PNLocomotionAction)hub.Actions().Get(locomotionId).Create();
                action.Start(ctx);
                action.Perform(ctx);
                return action;
            }

            foreach (var mode in new[] { PNSkillMode.Predicted, PNSkillMode.Observed })
            {
                using var action = BeginLocomotion(mode, out var ctx);
                Check(!action.Pushed && !action.AdvanceAuxiliary(.25f) && action.AdvanceAuxiliary(.25f),
                    mode + " completes its locomotion timeline without a traversal");
                action.Exit(ctx);
            }
            var body = actor.AddComponent<Rigidbody>();
            body.isKinematic = true;
            traversal = PNTraversal.Begin(actor, hub, PNTerrainIds.Ground);
            var groundLayers = body.excludeLayers.value;
            using (var action = BeginLocomotion(PNSkillMode.Authority, out var ctx))
            {
                Check(action.Pushed && traversal.Effective == PNTerrainIds.Air && body.excludeLayers.value != groundLayers,
                    "authority applies the locomotion and terrain collision layers");
                action.Cancel(ctx);
                Check(!action.Pushed && traversal.PushCount == 0 && traversal.Effective == PNTerrainIds.Ground &&
                    body.excludeLayers.value == groundLayers, "cancel restores locomotion and collision layers");
            }
            using (var action = BeginLocomotion(PNSkillMode.Authority, out var ctx))
            {
                Check(action.AdvanceAuxiliary(.5f), "authority completes the locomotion timeline");
                action.Exit(ctx);
                Check(traversal.PushCount == 0 && body.excludeLayers.value == groundLayers, "Exit releases the locomotion push");
            }
        }
        finally
        {
            try
            {
                traversal?.Clear();
                if (actor != null)
                {
                    actor.GetComponent<EveEntityLoader>()?.Unload();
                    actor.GetComponent<EveEntityIdentity>()?.ResetIdentity();
                }
                controller?.Dispose();
                host?.Dispose();
                if (EveRegistrySession.State != EveSessionState.Stopped) EveRegistrySession.End();
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        return "PASS " + count + " placement and locomotion action checks in a disposable grid world";
    }

    private static bool Ok(Action action) { try { action(); return true; } catch (Exception) { return false; } }
    private static bool Throws(Action action, bool expected) { try { action(); return !expected; } catch (Exception) { return expected; } }
    private static bool ReferenceFails(EveRegistryHub hub)
    {
        try { foreach (var definition in hub.Instances().Snapshot().Values) definition.Validate(hub); return false; } catch (Exception) { return true; }
    }
}
#endif
