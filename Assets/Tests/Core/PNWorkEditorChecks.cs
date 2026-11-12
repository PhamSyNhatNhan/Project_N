#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using EveEngine;
using Newtonsoft.Json.Linq;
using UnityEngine;

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

    private static bool Ok(Action action) { try { action(); return true; } catch (Exception) { return false; } }
    private static bool Throws(Action action, bool expected) { try { action(); return !expected; } catch (Exception) { return expected; } }
    private static bool ReferenceFails(EveRegistryHub hub)
    {
        try { foreach (var definition in hub.Instances().Snapshot().Values) definition.Validate(hub); return false; } catch (Exception) { return true; }
    }
}
#endif
